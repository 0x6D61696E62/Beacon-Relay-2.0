namespace BeaconRelay.LpdReceiver.Data;

public sealed class AlertSettingsRecord
{
    public int Id { get; set; } = 1;

    public bool MonitorEnabled { get; set; }
    public string? MonitorUrl { get; set; }
    public int MonitorIntervalSeconds { get; set; } = 60;

    public bool EmailEnabled { get; set; }
    public string? EmailSmtpHost { get; set; }
    public int EmailSmtpPort { get; set; } = 587;
    public bool EmailUseSsl { get; set; } = true;
    public string? EmailUsername { get; set; }
    public string? EmailPassword { get; set; }
    public string? EmailFrom { get; set; }
    public string? EmailTo { get; set; }
    public int ListenerDownEmailCooldownMinutes { get; set; } = 30;

    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
