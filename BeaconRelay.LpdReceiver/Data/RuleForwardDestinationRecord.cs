namespace BeaconRelay.LpdReceiver.Data;

public sealed class RuleForwardDestinationRecord
{
    public int Id { get; set; }
    public int RuleId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int DestinationOrder { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string OutboundQueueName { get; set; } = string.Empty;
    public string CompressMode { get; set; } = ForwardCompressMode.None;
    public string PayloadMode { get; set; } = ForwardPayloadMode.StoredFile;
    public int? RetryPolicyId { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public ProcessingRuleRecord Rule { get; set; } = default!;
    public RetryPolicyRecord? RetryPolicy { get; set; }
}

public static class ForwardCompressMode
{
    public const string None = "None";
    public const string ZipArchive = "ZipArchive";
}

public static class ForwardPayloadMode
{
    public const string OriginalFile = "OriginalFile";
    public const string StoredFile = "StoredFile";
}
