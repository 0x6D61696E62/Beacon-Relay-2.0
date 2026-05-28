namespace BeaconRelay.LpdReceiver.Options;

public sealed class AdminAuthOptions
{
    public const string SectionName = "AdminAuth";

    public bool Enabled { get; set; }
    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "change-me";
    public string Realm { get; set; } = "Beacon Relay Admin";
}
