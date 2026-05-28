using BeaconRelay.LpdReceiver.Data;
using Microsoft.EntityFrameworkCore;

namespace BeaconRelay.LpdReceiver.Services;

public sealed class DeliveryDispatcherService(
    IServiceScopeFactory scopeFactory,
    ILogger<DeliveryDispatcherService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LockDuration = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processedAny = await ProcessOneCycleAsync(stoppingToken);
                if (!processedAny)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Delivery dispatcher loop failed.");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    internal Task<bool> ProcessOneCycleAsync(CancellationToken cancellationToken)
    {
        return ProcessOneAsync(cancellationToken);
    }

    private async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var folderHandler = scope.ServiceProvider.GetRequiredService<FolderDeliveryHandler>();
        var forwardHandler = scope.ServiceProvider.GetRequiredService<LpdForwardDeliveryHandler>();

        var nowUtc = DateTime.UtcNow;
        var candidate = await db.DeliveryWorkItems
            .Where(x => (x.Status == DeliveryWorkItemStatus.Pending || x.Status == DeliveryWorkItemStatus.RetryScheduled)
                        && x.NextAttemptUtc <= nowUtc
                        && (x.LockExpiresUtc == null || x.LockExpiresUtc < nowUtc))
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.NextAttemptUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (candidate is null)
        {
            return false;
        }

        candidate.Status = DeliveryWorkItemStatus.InProgress;
        candidate.LockedBy = Environment.MachineName;
        candidate.LockExpiresUtc = nowUtc.Add(LockDuration);
        candidate.UpdatedUtc = nowUtc;
        await db.SaveChangesAsync(cancellationToken);

        await db.Entry(candidate).Reference(x => x.ReceivedFile).LoadAsync(cancellationToken);

        DeliveryExecutionResult result;
        RetryPolicyRecord? retryPolicy = null;

        if (string.Equals(candidate.DestinationType, DeliveryDestinationType.Folder, StringComparison.OrdinalIgnoreCase))
        {
            var destination = await db.RuleFolderDestinations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == candidate.DestinationId, cancellationToken);
            if (destination is null)
            {
                result = DeliveryExecutionResult.Failure("FolderDestinationMissing", $"Folder destination {candidate.DestinationId} was not found.", shouldRetry: false);
            }
            else
            {
                retryPolicy = await ResolveRetryPolicyAsync(db, destination.RetryPolicyId, cancellationToken);
                result = await folderHandler.DeliverAsync(candidate.ReceivedFile, destination, cancellationToken);
            }
        }
        else if (string.Equals(candidate.DestinationType, DeliveryDestinationType.ForwardLpd, StringComparison.OrdinalIgnoreCase))
        {
            var destination = await db.RuleForwardDestinations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == candidate.DestinationId, cancellationToken);
            if (destination is null)
            {
                result = DeliveryExecutionResult.Failure("ForwardDestinationMissing", $"Forward destination {candidate.DestinationId} was not found.", shouldRetry: false);
            }
            else
            {
                retryPolicy = await ResolveRetryPolicyAsync(db, destination.RetryPolicyId, cancellationToken);
                result = await forwardHandler.DeliverAsync(candidate.ReceivedFile, destination, cancellationToken);
            }
        }
        else
        {
            result = DeliveryExecutionResult.Failure("UnknownDestinationType", $"Unknown destination type '{candidate.DestinationType}'.", shouldRetry: false);
        }

        await PersistAttemptOutcomeAsync(db, candidate, result, retryPolicy, cancellationToken);
        return true;
    }

    private static async Task PersistAttemptOutcomeAsync(AppDbContext db, DeliveryWorkItemRecord workItem, DeliveryExecutionResult result, RetryPolicyRecord? retryPolicy, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        workItem.AttemptCount += 1;
        workItem.LastAttemptUtc = nowUtc;
        workItem.UpdatedUtc = nowUtc;
        workItem.LockedBy = null;
        workItem.LockExpiresUtc = null;
        workItem.LastErrorCode = result.ErrorCode;
        workItem.LastErrorMessage = result.ErrorMessage;

        var attempt = new DeliveryAttemptRecord
        {
            WorkItemId = workItem.Id,
            AttemptNumber = workItem.AttemptCount,
            StartedUtc = nowUtc,
            CompletedUtc = nowUtc,
            Outcome = result.Succeeded ? DeliveryAttemptOutcome.Succeeded : DeliveryAttemptOutcome.Failed,
            BytesSentOrCopied = result.BytesProcessed,
            OutputPath = result.OutputPath,
            RemoteHost = result.RemoteHost,
            RemotePort = result.RemotePort,
            QueueNameUsed = result.QueueNameUsed,
            ZipCreatedPath = result.ZipCreatedPath,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage,
        };

        db.DeliveryAttempts.Add(attempt);

        if (result.Succeeded)
        {
            workItem.Status = DeliveryWorkItemStatus.Succeeded;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var canRetry = result.ShouldRetry && (retryPolicy?.MaxAttempts is null || workItem.AttemptCount < retryPolicy.MaxAttempts.Value);
        if (canRetry)
        {
            var delay = CalculateRetryDelay(retryPolicy, workItem.AttemptCount);
            workItem.Status = DeliveryWorkItemStatus.RetryScheduled;
            workItem.NextAttemptUtc = nowUtc.Add(delay);
        }
        else
        {
            workItem.Status = DeliveryWorkItemStatus.Failed;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<RetryPolicyRecord?> ResolveRetryPolicyAsync(AppDbContext db, int? retryPolicyId, CancellationToken cancellationToken)
    {
        if (retryPolicyId.HasValue)
        {
            var selected = await db.RetryPolicies
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == retryPolicyId.Value && x.IsEnabled, cancellationToken);

            if (selected is not null)
            {
                return selected;
            }
        }

        return await db.RetryPolicies
            .AsNoTracking()
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static TimeSpan CalculateRetryDelay(RetryPolicyRecord? retryPolicy, int attemptCount)
    {
        if (retryPolicy is null)
        {
            return TimeSpan.FromSeconds(Math.Min(300, 15 * attemptCount));
        }

        var baseSeconds = Math.Max(1, retryPolicy.InitialDelaySeconds);
        var maxSeconds = Math.Max(baseSeconds, retryPolicy.MaxDelaySeconds);
        var seconds = string.Equals(retryPolicy.BackoffMode, RetryBackoffMode.Fixed, StringComparison.OrdinalIgnoreCase)
            ? baseSeconds
            : Math.Min(maxSeconds, baseSeconds * Math.Pow(2, Math.Max(0, attemptCount - 1)));

        return TimeSpan.FromSeconds(seconds);
    }
}
