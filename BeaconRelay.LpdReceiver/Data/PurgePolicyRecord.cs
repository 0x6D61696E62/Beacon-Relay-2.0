namespace BeaconRelay.LpdReceiver.Data;

public sealed class PurgePolicyRecord
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string ApplyTo { get; set; } = PurgeApplyTarget.DeliveryAttempts;
    public int RetentionDays { get; set; } = 30;
    public string? TerminalStatusesCsv { get; set; }
    public int IntervalMinutes { get; set; } = 60;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public static class PurgeApplyTarget
{
    public const string DeliveryAttempts = "DeliveryAttempts";
    public const string DeliveryWorkItemsTerminal = "DeliveryWorkItemsTerminal";
}
