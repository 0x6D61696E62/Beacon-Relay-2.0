namespace BeaconRelay.LpdReceiver.Data;

public sealed class RetryPolicyRecord
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? MaxAttempts { get; set; }
    public int InitialDelaySeconds { get; set; } = 30;
    public string BackoffMode { get; set; } = RetryBackoffMode.Exponential;
    public int MaxDelaySeconds { get; set; } = 3600;
    public int JitterPercent { get; set; } = 10;
    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<RuleForwardDestinationRecord> ForwardDestinations { get; set; } = [];
}

public static class RetryBackoffMode
{
    public const string Fixed = "Fixed";
    public const string Exponential = "Exponential";
}
