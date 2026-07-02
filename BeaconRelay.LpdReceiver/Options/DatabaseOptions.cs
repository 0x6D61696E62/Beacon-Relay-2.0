namespace BeaconRelay.LpdReceiver.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = "Data Source=db/beacon-relay.db";
    public string? Password { get; set; }
}
