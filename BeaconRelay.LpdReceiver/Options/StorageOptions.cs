namespace BeaconRelay.LpdReceiver.Options;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string OutputDirectory { get; set; } = "data/inbox";
}
