namespace BeaconRelay.LpdReceiver.Data;

using System.Text.Json.Serialization;

public sealed class DeliveryAttemptRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkItemId { get; set; }
    public int AttemptNumber { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string Outcome { get; set; } = DeliveryAttemptOutcome.Failed;

    public long? BytesSentOrCopied { get; set; }
    public string? OutputPath { get; set; }
    public string? RemoteHost { get; set; }
    public int? RemotePort { get; set; }
    public string? QueueNameUsed { get; set; }
    public string? ZipCreatedPath { get; set; }

    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    [JsonIgnore]
    public DeliveryWorkItemRecord WorkItem { get; set; } = default!;
}

public static class DeliveryAttemptOutcome
{
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Canceled = "Canceled";
}
