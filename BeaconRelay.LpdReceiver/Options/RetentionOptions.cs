namespace BeaconRelay.LpdReceiver.Options;

public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    public bool Enabled { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
    public int IntervalMinutes { get; set; } = 60;
}
