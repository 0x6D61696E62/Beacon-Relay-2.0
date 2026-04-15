namespace BeaconRelay.LpdReceiver.Options;

public sealed class LpdOptions
{
    public const string SectionName = "Lpd";

    public int Port { get; set; } = 515;
    public int MaxLineLength { get; set; } = 4096;
    public int MaxFileBytes { get; set; } = 50 * 1024 * 1024;
    public int MaxJobBytes { get; set; } = 100 * 1024 * 1024;
    public int MaxFilesPerJob { get; set; } = 16;
    public int SessionTimeoutSeconds { get; set; } = 60;
    public int InitialRestartBackoffSeconds { get; set; } = 1;
    public int MaxRestartBackoffSeconds { get; set; } = 30;
}
