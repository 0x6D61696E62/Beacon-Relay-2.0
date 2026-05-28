namespace BeaconRelay.LpdReceiver.Services;

public sealed class DeliveryExecutionResult
{
    public bool Succeeded { get; init; }
    public bool ShouldRetry { get; init; } = true;
    public long? BytesProcessed { get; init; }
    public string? OutputPath { get; init; }
    public string? RemoteHost { get; init; }
    public int? RemotePort { get; init; }
    public string? QueueNameUsed { get; init; }
    public string? ZipCreatedPath { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static DeliveryExecutionResult Success(long? bytesProcessed = null, string? outputPath = null, string? remoteHost = null, int? remotePort = null, string? queueNameUsed = null, string? zipCreatedPath = null)
        => new()
        {
            Succeeded = true,
            ShouldRetry = false,
            BytesProcessed = bytesProcessed,
            OutputPath = outputPath,
            RemoteHost = remoteHost,
            RemotePort = remotePort,
            QueueNameUsed = queueNameUsed,
            ZipCreatedPath = zipCreatedPath,
        };

    public static DeliveryExecutionResult Failure(string errorCode, string errorMessage, bool shouldRetry = true)
        => new()
        {
            Succeeded = false,
            ShouldRetry = shouldRetry,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
        };
}
