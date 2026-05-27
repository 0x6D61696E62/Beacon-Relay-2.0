namespace BeaconRelay.LpdReceiver.Data;

public sealed class ReceivedFileRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime ReceivedUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? LastUpdatedUtc { get; set; }

    public string QueueName { get; set; } = string.Empty;
    public string RemoteHost { get; set; } = string.Empty;
    public int RemotePort { get; set; }

    public string? LpdJobId { get; set; }
    public string? OriginalFileName { get; set; }
    public string? StoredFilePath { get; set; }
    public long ByteLength { get; set; }
    public string? Sha256 { get; set; }

    public string? JobName { get; set; }
    public string? UserName { get; set; }
    public string? HostName { get; set; }
    public string? BannerClass { get; set; }
    public string? BannerName { get; set; }
    public string? SourceFileHints { get; set; }

    public string? ControlFileName { get; set; }
    public string? RawControlText { get; set; }

    public string Status { get; set; } = FileRecordStatus.Received;
    public bool IsDuplicate { get; set; }
    public Guid? DuplicateOfId { get; set; }
    public string? ErrorDetails { get; set; }
}

public static class FileRecordStatus
{
    public const string Received = "Received";
    public const string DuplicateSkipped = "DuplicateSkipped";
    public const string SessionFailed = "SessionFailed";
    public const string ExpiredDeleted = "ExpiredDeleted";
    public const string ExpiredMissing = "ExpiredMissing";
}
