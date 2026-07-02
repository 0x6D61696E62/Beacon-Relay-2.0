namespace BeaconRelay.LpdReceiver.Data;

/// <summary>
/// Single-row table (Id = 1) for retention cleanup runtime settings.
/// </summary>
public sealed class RetentionSettingsRecord
{
    public int Id { get; set; } = 1;
    public bool IsEnabled { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
    public int IntervalMinutes { get; set; } = 60;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
