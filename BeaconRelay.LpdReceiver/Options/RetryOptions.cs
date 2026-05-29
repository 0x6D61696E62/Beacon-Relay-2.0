namespace BeaconRelay.LpdReceiver.Options;

public sealed class RetryOptions
{
    public const string SectionName = "Retry";

    // Applies only when a destination has no explicit retry policy max attempts.
    public int? DefaultMaxAttempts { get; set; } = 5;
}
