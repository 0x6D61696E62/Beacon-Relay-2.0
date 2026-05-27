using BeaconRelay.LpdReceiver.Data;
using BeaconRelay.LpdReceiver.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BeaconRelay.LpdReceiver.Services;

public sealed class RetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<RetentionOptions> options,
    ILogger<RetentionService> logger) : BackgroundService
{
    private readonly RetentionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_options.Enabled)
                {
                    await CleanupAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Retention cleanup failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes)), stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        var cutoffUtc = DateTime.UtcNow.AddDays(-Math.Max(1, _options.RetentionDays));
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

        logger.LogInformation(
            "Retention cleanup completed. candidates={CandidateCount}, deleted={DeletedCount}, missing={MissingCount}, cutoffUtc={CutoffUtc}",
            candidates.Count,
            deletedCount,
            missingCount,
            cutoffUtc);
    }
}
