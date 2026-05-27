namespace BeaconRelay.LpdReceiver.Protocol;

public sealed class ControlFileMetadata
{
    public string? JobName { get; set; }
    public string? UserName { get; set; }
    public string? HostName { get; set; }
    public string? BannerClass { get; set; }
    public string? BannerName { get; set; }
    public string? LpdJobId { get; set; }
    public string? SourceFileHints { get; set; }
}
