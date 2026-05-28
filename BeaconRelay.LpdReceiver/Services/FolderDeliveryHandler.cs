using BeaconRelay.LpdReceiver.Data;

namespace BeaconRelay.LpdReceiver.Services;

public sealed class FolderDeliveryHandler
{
    public async Task<DeliveryExecutionResult> DeliverAsync(ReceivedFileRecord receivedFile, RuleFolderDestinationRecord destination, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(receivedFile.StoredFilePath) || !File.Exists(receivedFile.StoredFilePath))
        {
            return DeliveryExecutionResult.Failure("SourceMissing", "Stored source file path is missing or not found.", destination.IsQueueOnFailure);
        }

        var sourcePath = receivedFile.StoredFilePath;
        var sourceInfo = new FileInfo(sourcePath);

        var subfolder = ResolveSubfolder(destination, receivedFile.ReceivedUtc);
        var targetRoot = Path.GetFullPath(destination.RootFolder);
        var targetDirectory = Path.Combine(targetRoot, subfolder);
        Directory.CreateDirectory(targetDirectory);

        var baseName = FileNameSanitizer.Sanitize(receivedFile.OriginalFileName, Path.GetFileName(sourcePath));
        var destinationPath = Path.Combine(targetDirectory, baseName);

        var resolvedPath = ResolveDuplicatePath(destinationPath, destination.DuplicatePolicy, destination.UniqueNameMode, destination.UniqueNameAffix);
        if (resolvedPath is null)
        {
            return DeliveryExecutionResult.Failure("DuplicateConflict", $"Destination file already exists: {destinationPath}", destination.IsQueueOnFailure);
        }

        await using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
        await using (var destinationStream = new FileStream(resolvedPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await sourceStream.CopyToAsync(destinationStream, cancellationToken);
            await destinationStream.FlushAsync(cancellationToken);
        }

        return DeliveryExecutionResult.Success(bytesProcessed: sourceInfo.Length, outputPath: resolvedPath);
    }

    private static string ResolveSubfolder(RuleFolderDestinationRecord destination, DateTime receivedUtc)
    {
        if (string.Equals(destination.SubfolderPatternType, SubfolderPatternTypeValues.DotNetDateFormat, StringComparison.OrdinalIgnoreCase))
        {
            return receivedUtc.ToString(destination.SubfolderPattern);
        }

        var template = destination.SubfolderPattern;
        return template
            .Replace("{yyyy}", receivedUtc.ToString("yyyy"), StringComparison.Ordinal)
            .Replace("{MM}", receivedUtc.ToString("MM"), StringComparison.Ordinal)
            .Replace("{dd}", receivedUtc.ToString("dd"), StringComparison.Ordinal)
            .Replace("{HH}", receivedUtc.ToString("HH"), StringComparison.Ordinal)
            .Replace("{mm}", receivedUtc.ToString("mm"), StringComparison.Ordinal)
            .Replace("{ss}", receivedUtc.ToString("ss"), StringComparison.Ordinal);
    }

    private static string? ResolveDuplicatePath(string destinationPath, string duplicatePolicy, string? uniqueNameMode, string? uniqueNameAffix)
    {
        if (!File.Exists(destinationPath))
        {
            return destinationPath;
        }

        if (string.Equals(duplicatePolicy, DestinationDuplicatePolicy.Overwrite, StringComparison.OrdinalIgnoreCase))
        {
            return destinationPath;
        }

        if (string.Equals(duplicatePolicy, DestinationDuplicatePolicy.Fail, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var dir = Path.GetDirectoryName(destinationPath)!;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(destinationPath);
        var extension = Path.GetExtension(destinationPath);
        var affix = string.IsNullOrWhiteSpace(uniqueNameAffix) ? "" : uniqueNameAffix.Trim();

        for (var i = 1; i <= 10000; i++)
        {
            var suffix = uniqueNameMode switch
            {
                UniqueNameModeValues.Timestamp => DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),
                UniqueNameModeValues.Guid => Guid.NewGuid().ToString("N"),
                UniqueNameModeValues.HashFragment => Guid.NewGuid().ToString("N")[..8],
                _ => i.ToString("D4"),
            };

            var candidate = Path.Combine(dir, $"{fileNameWithoutExtension}{affix}_{suffix}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
