namespace BeaconRelay.LpdReceiver.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = "Data Source=beacon-relay.db";
}
