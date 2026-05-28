namespace BeaconRelay.LpdReceiver.Data;

public sealed class DeliveryWorkItemRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReceivedFileId { get; set; }
    public int RuleId { get; set; }
    public string DestinationType { get; set; } = DeliveryDestinationType.Folder;
    public int DestinationId { get; set; }
    public string Status { get; set; } = DeliveryWorkItemStatus.Pending;
    public int Priority { get; set; }
    public int AttemptCount { get; set; }

    public DateTime NextAttemptUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastAttemptUtc { get; set; }

    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }

    public string? LockedBy { get; set; }
    public DateTime? LockExpiresUtc { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public ReceivedFileRecord ReceivedFile { get; set; } = default!;
    public ProcessingRuleRecord Rule { get; set; } = default!;
    public ICollection<DeliveryAttemptRecord> Attempts { get; set; } = [];
}

public static class DeliveryDestinationType
{
    public const string Folder = "Folder";
    public const string ForwardLpd = "ForwardLpd";
}

public static class DeliveryWorkItemStatus
{
    public const string Pending = "Pending";
    public const string InProgress = "InProgress";
    public const string RetryScheduled = "RetryScheduled";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Canceled = "Canceled";
}
