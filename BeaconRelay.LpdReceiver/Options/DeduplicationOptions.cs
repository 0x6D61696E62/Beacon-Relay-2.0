namespace BeaconRelay.LpdReceiver.Options;

public enum DuplicateHandlingMode
{
    MarkAndStore = 0,
    SkipWrite = 1,
}

public sealed class DeduplicationOptions
{
    public const string SectionName = "Deduplication";

    public DuplicateHandlingMode Mode { get; set; } = DuplicateHandlingMode.MarkAndStore;
}
