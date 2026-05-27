using System.Net;

namespace BeaconRelay.LpdReceiver.Protocol;

public sealed class LpdDataFile
{
    public required string Name { get; init; }
    public required byte[] Content { get; init; }
}

public sealed class LpdReceivedJob
{
    public required string QueueName { get; init; }
    public required string RawControlText { get; init; }
    public required string? ControlFileName { get; init; }
    public required ControlFileMetadata Metadata { get; init; }
    public required IReadOnlyList<LpdDataFile> DataFiles { get; init; }
    public required IPEndPoint RemoteEndpoint { get; init; }
}

public sealed class LpdSessionResult
{
    public bool Success { get; init; }
    public LpdReceivedJob? Job { get; init; }
    public string? Error { get; init; }
    public string? QueueName { get; init; }
}
