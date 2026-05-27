namespace BeaconRelay.LpdReceiver.Options;

public sealed class HealthEndpointOptions
{
    public const string SectionName = "Health";

    public int Port { get; set; } = 8080;
    public string HealthPath { get; set; } = "/healthz";
    public string ReadinessPath { get; set; } = "/readyz";
}
