using BeaconRelay.LpdReceiver.Options;
using Microsoft.Extensions.Options;

namespace BeaconRelay.LpdReceiver.Services;

public sealed class FileStorageService
{
    private readonly string _outputDirectory;

    public FileStorageService(IOptions<StorageOptions> options)
    {
        _outputDirectory = Path.GetFullPath(options.Value.OutputDirectory);
        Directory.CreateDirectory(_outputDirectory);
    }

    public async Task<string> StoreAsync(byte[] content, DateTime receivedUtc, string queueName, string? jobId, string? originalFileName, int fileIndex, CancellationToken cancellationToken)
    {
        var safeName = FileNameSanitizer.Sanitize(originalFileName);
        var safeQueue = FileNameSanitizer.Sanitize(queueName, "queue");
        var safeJob = FileNameSanitizer.Sanitize(jobId, "nojid");

        var finalName = $"{receivedUtc:yyyyMMddTHHmmssfffffffZ}_{safeQueue}_{safeJob}_{fileIndex:D2}_{safeName}";
        var destinationPath = Path.Combine(_outputDirectory, finalName);
        var fullDestinationPath = Path.GetFullPath(destinationPath);

        if (!fullDestinationPath.StartsWith(_outputDirectory, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Resolved output path escaped output directory.");
        }

        var tempPath = Path.Combine(_outputDirectory, $".{Guid.NewGuid():N}.tmp");

        await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await stream.WriteAsync(content.AsMemory(0, content.Length), cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(tempPath, fullDestinationPath, overwrite: false);
        return fullDestinationPath;
    }
}
