using BeaconRelay.LpdReceiver.Data;
using Microsoft.EntityFrameworkCore;

namespace BeaconRelay.LpdReceiver.Services;

public sealed class RetentionService(
    IServiceScopeFactory scopeFactory,
    ILogger<RetentionService> logger) : BackgroundService
{
    private static readonly TimeSpan FallbackInterval = TimeSpan.FromMinutes(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = await GetRetentionSettingsAsync(stoppingToken);
                if (settings.IsEnabled)
                {
                    await CleanupAsync(settings.RetentionDays, stoppingToken);
                }

                var delay = TimeSpan.FromMinutes(Math.Max(1, settings.IntervalMinutes));
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Retention cleanup failed.");
                await Task.Delay(FallbackInterval, stoppingToken);
            }
        }
    }

    private async Task<RetentionSettingsRecord> GetRetentionSettingsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var settings = await db.RetentionSettings.FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        var seed = new RetentionSettingsRecord
        {
            Id = 1,
            IsEnabled = true,
            RetentionDays = 30,
            IntervalMinutes = 60,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };

        db.RetentionSettings.Add(seed);
        await db.SaveChangesAsync(cancellationToken);
        return seed;
    }

    private async Task CleanupAsync(int retentionDays, CancellationToken cancellationToken)
    {
        var cutoffUtc = DateTime.UtcNow.AddDays(-Math.Max(1, retentionDays));
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var candidates = await db.ReceivedFiles
            .Where(x => x.StoredFilePath != null && x.ReceivedUtc < cutoffUtc && (x.Status == FileRecordStatus.Received || x.Status == FileRecordStatus.DuplicateSkipped))
            .ToListAsync(cancellationToken);

        var deletedCount = 0;
        var missingCount = 0;

        foreach (var record in candidates)
        {
            if (record.StoredFilePath is null)
            {
                continue;
            }

            if (File.Exists(record.StoredFilePath))
            {
                File.Delete(record.StoredFilePath);
                record.Status = FileRecordStatus.ExpiredDeleted;
                deletedCount++;
            }
            else
            {
                record.Status = FileRecordStatus.ExpiredMissing;
                missingCount++;
            }

            record.LastUpdatedUtc = DateTime.UtcNow;
        }

        if (candidates.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await PurgeByPolicyAsync(db, cancellationToken);

        logger.LogInformation(
            "Retention cleanup completed. candidates={CandidateCount}, deleted={DeletedCount}, missing={MissingCount}, cutoffUtc={CutoffUtc}",
            candidates.Count,
            deletedCount,
            missingCount,
            cutoffUtc);
    }

    private async Task PurgeByPolicyAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var policies = await db.PurgePolicies
            .AsNoTracking()
            .Where(x => x.IsEnabled)
            .ToListAsync(cancellationToken);

        foreach (var policy in policies)
        {
            var cutoffUtc = DateTime.UtcNow.AddDays(-Math.Max(1, policy.RetentionDays));

            if (string.Equals(policy.ApplyTo, PurgeApplyTarget.DeliveryAttempts, StringComparison.OrdinalIgnoreCase))
            {
                var deletedAttempts = await db.DeliveryAttempts
                    .Where(x => x.StartedUtc < cutoffUtc)
                    .ExecuteDeleteAsync(cancellationToken);

                if (deletedAttempts > 0)
                {
                    logger.LogInformation(
                        "Policy purge removed delivery attempts. policy={PolicyName}, deleted={DeletedCount}, cutoffUtc={CutoffUtc}",
                        policy.Name,
                        deletedAttempts,
                        cutoffUtc);
                }
            }

            if (string.Equals(policy.ApplyTo, PurgeApplyTarget.DeliveryWorkItemsTerminal, StringComparison.OrdinalIgnoreCase))
            {
                var configuredStatuses = ParseStatuses(policy.TerminalStatusesCsv);
                IReadOnlyCollection<string> terminalStatuses = configuredStatuses.Count > 0
                    ? configuredStatuses
                    : [DeliveryWorkItemStatus.Succeeded, DeliveryWorkItemStatus.Failed, DeliveryWorkItemStatus.Canceled];

                var terminalQuery = db.DeliveryWorkItems
                    .Where(x => x.UpdatedUtc < cutoffUtc && terminalStatuses.Contains(x.Status));

                var ids = await terminalQuery.Select(x => x.Id).ToListAsync(cancellationToken);
                if (ids.Count == 0)
                {
                    continue;
                }

                await db.DeliveryAttempts
                    .Where(x => ids.Contains(x.WorkItemId))
                    .ExecuteDeleteAsync(cancellationToken);

                var deletedItems = await db.DeliveryWorkItems
                    .Where(x => ids.Contains(x.Id))
                    .ExecuteDeleteAsync(cancellationToken);

                if (deletedItems > 0)
                {
                    logger.LogInformation(
                        "Policy purge removed terminal work items. policy={PolicyName}, deleted={DeletedCount}, cutoffUtc={CutoffUtc}",
                        policy.Name,
                        deletedItems,
                        cutoffUtc);
                }
            }
        }
    }

    private static HashSet<string> ParseStatuses(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return [];
        }

        return csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
