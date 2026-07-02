using BeaconRelay.LpdReceiver.Data;
using BeaconRelay.LpdReceiver.Options;
using BeaconRelay.LpdReceiver.Protocol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BeaconRelay.LpdReceiver.Services;

public sealed class ReceivedFileProcessor(
    AppDbContext dbContext,
    FileStorageService fileStorageService,
    Sha256Hasher sha256Hasher,
    ProcessingRuleMatcher ruleMatcher,
    DeliveryWorkItemEnqueuer workItemEnqueuer,
    IOptions<DeduplicationOptions> dedupOptions,
    ILogger<ReceivedFileProcessor> logger)
{
    private readonly DeduplicationOptions _dedupOptions = dedupOptions.Value;

    public async Task PersistSuccessfulSessionAsync(LpdReceivedJob job, CancellationToken cancellationToken)
    {
        var receivedUtc = DateTime.UtcNow;
        var matchedRules = await ruleMatcher.GetMatchingRulesAsync(job, cancellationToken);

        for (var i = 0; i < job.DataFiles.Count; i++)
        {
            var dataFile = job.DataFiles[i];
            var hash = sha256Hasher.ComputeHex(dataFile.Content);
            var byteLength = dataFile.Content.LongLength;

            var existing = await dbContext.ReceivedFiles
                .AsNoTracking()
                .Where(x => x.Sha256 == hash && x.ByteLength == byteLength)
                .OrderBy(x => x.ReceivedUtc)
                .FirstOrDefaultAsync(cancellationToken);

            var isDuplicate = existing is not null;
            var status = FileRecordStatus.Received;
            string? storedPath;

            if (DeduplicationPolicy.ShouldSkipWrite(_dedupOptions.Mode, isDuplicate))
            {
                storedPath = existing!.StoredFilePath;
                status = FileRecordStatus.DuplicateSkipped;
            }
            else
            {
                storedPath = await fileStorageService.StoreAsync(
                    dataFile.Content,
                    receivedUtc,
                    job.QueueName,
                    job.Metadata.LpdJobId,
                    dataFile.Name,
                    i + 1,
                    cancellationToken);
            }

            var record = new ReceivedFileRecord
            {
                ReceivedUtc = receivedUtc,
                CreatedUtc = DateTime.UtcNow,
                QueueName = job.QueueName,
                RemoteHost = job.RemoteEndpoint.Address.ToString(),
                RemotePort = job.RemoteEndpoint.Port,
                LpdJobId = job.Metadata.LpdJobId,
                OriginalFileName = dataFile.Name,
                StoredFilePath = storedPath,
                ByteLength = byteLength,
                Sha256 = hash,
                JobName = job.Metadata.JobName,
                UserName = job.Metadata.UserName,
                HostName = job.Metadata.HostName,
                BannerClass = job.Metadata.BannerClass,
                BannerName = job.Metadata.BannerName,
                SourceFileHints = job.Metadata.SourceFileHints,
                ControlFileName = job.ControlFileName,
                RawControlText = job.RawControlText,
                Status = status,
                IsDuplicate = isDuplicate,
                DuplicateOfId = existing?.Id,
            };

            dbContext.ReceivedFiles.Add(record);
            workItemEnqueuer.EnqueueForReceivedFile(record, matchedRules, receivedUtc);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Persisted LPD job queue={QueueName}, jobId={JobId}, files={FileCount}, remote={RemoteAddress}:{RemotePort}",
            job.QueueName,
            job.Metadata.LpdJobId,
            job.DataFiles.Count,
            job.RemoteEndpoint.Address,
            job.RemoteEndpoint.Port);
    }

    public async Task PersistFailedSessionAsync(string remoteHost, int remotePort, string? queueName, string error, CancellationToken cancellationToken)
    {
        dbContext.ReceivedFiles.Add(new ReceivedFileRecord
        {
            ReceivedUtc = DateTime.UtcNow,
            CreatedUtc = DateTime.UtcNow,
            QueueName = queueName ?? "unknown",
            RemoteHost = remoteHost,
            RemotePort = remotePort,
            Status = FileRecordStatus.SessionFailed,
            ErrorDetails = error,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
