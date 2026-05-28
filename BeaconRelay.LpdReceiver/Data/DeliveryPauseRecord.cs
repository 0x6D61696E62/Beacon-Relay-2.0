namespace BeaconRelay.LpdReceiver.Data;

/// <summary>
/// Single-row table (Id = 1) persisting delivery pause state across restarts.
/// </summary>
public sealed class DeliveryPauseRecord
{
    public int Id { get; set; } = 1;
    public bool IsPaused { get; set; }
    public DateTime? PausedAtUtc { get; set; }
    public DateTime? ResumeAtUtc { get; set; }
    public string? Reason { get; set; }
}
