using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using BeaconRelay.LpdReceiver.Data;

namespace BeaconRelay.LpdReceiver.Services;

public sealed class LpdForwardDeliveryHandler
{
    public async Task<DeliveryExecutionResult> DeliverAsync(ReceivedFileRecord receivedFile, RuleForwardDestinationRecord destination, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(receivedFile.StoredFilePath) || !File.Exists(receivedFile.StoredFilePath))
        {
            return DeliveryExecutionResult.Failure("SourceMissing", "Stored source file path is missing or not found.");
        }

        byte[] payload;
        var safeName = FileNameSanitizer.Sanitize(receivedFile.OriginalFileName);
        string? zipAuditMarker = null;

        payload = await File.ReadAllBytesAsync(receivedFile.StoredFilePath, cancellationToken);

        if (string.Equals(destination.CompressMode, ForwardCompressMode.ZipArchive, StringComparison.OrdinalIgnoreCase))
        {
            payload = CreateZipPayload(payload, safeName);
            zipAuditMarker = $"memory:{safeName}.zip";
            safeName = Path.ChangeExtension(safeName, ".zip");
        }

        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(destination.Host, destination.Port, cancellationToken);
            await using var stream = tcp.GetStream();

            await SendReceiveJobCommandAsync(stream, destination.OutboundQueueName, cancellationToken);
            await SendControlFileAsync(stream, safeName, receivedFile, cancellationToken);
            await SendDataFileAsync(stream, safeName, payload, cancellationToken);

            return DeliveryExecutionResult.Success(
                bytesProcessed: payload.LongLength,
                remoteHost: destination.Host,
                remotePort: destination.Port,
                queueNameUsed: destination.OutboundQueueName,
                zipCreatedPath: zipAuditMarker);
        }
        catch (Exception ex)
        {
            return DeliveryExecutionResult.Failure("ForwardFailed", ex.Message);
        }
    }

    private static async Task SendReceiveJobCommandAsync(NetworkStream stream, string queueName, CancellationToken cancellationToken)
    {
        var bytes = Encoding.ASCII.GetBytes($"\x02{queueName}\n");
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        await ReadAckOrThrowAsync(stream, cancellationToken);
    }

    private static async Task SendControlFileAsync(NetworkStream stream, string fileName, ReceivedFileRecord receivedFile, CancellationToken cancellationToken)
    {
        var localHost = Environment.MachineName;
        var dataFileName = $"dfA001{localHost}";
        var controlFileName = $"cfA001{localHost}";

        var controlText = string.Join('\n', new[]
        {
            $"H{localHost}",
            $"P{(string.IsNullOrWhiteSpace(receivedFile.UserName) ? "beacon" : receivedFile.UserName)}",
            $"J{(string.IsNullOrWhiteSpace(receivedFile.JobName) ? fileName : receivedFile.JobName)}",
            $"N{fileName}",
            $"U{dataFileName}",
        }) + "\n";

        var controlBytes = Encoding.ASCII.GetBytes(controlText);
        var cmdBytes = Encoding.ASCII.GetBytes($"\x02{controlBytes.Length} {controlFileName}\n");
        await stream.WriteAsync(cmdBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        await ReadAckOrThrowAsync(stream, cancellationToken);

        await stream.WriteAsync(controlBytes, cancellationToken);
        await stream.WriteAsync(new byte[] { 0 }, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        await ReadAckOrThrowAsync(stream, cancellationToken);
    }

    private static async Task SendDataFileAsync(NetworkStream stream, string fileName, byte[] payload, CancellationToken cancellationToken)
    {
        var localHost = Environment.MachineName;
        var dataFileName = $"dfA001{localHost}";
        var cmdBytes = Encoding.ASCII.GetBytes($"\x03{payload.Length} {dataFileName}\n");
        await stream.WriteAsync(cmdBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        await ReadAckOrThrowAsync(stream, cancellationToken);

        await stream.WriteAsync(payload, cancellationToken);
        await stream.WriteAsync(new byte[] { 0 }, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        await ReadAckOrThrowAsync(stream, cancellationToken);
    }

    private static async Task ReadAckOrThrowAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var ack = new byte[1];
        var bytesRead = await stream.ReadAsync(ack, cancellationToken);
        if (bytesRead != 1 || ack[0] != 0)
        {
            throw new InvalidOperationException("LPD forward destination returned a non-ACK response.");
        }
    }

    private static byte[] CreateZipPayload(byte[] sourcePayload, string originalName)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entryName = string.IsNullOrWhiteSpace(originalName) ? "payload.bin" : originalName;
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            entryStream.Write(sourcePayload, 0, sourcePayload.Length);
        }

        memory.Position = 0;
        return memory.ToArray();
    }
}
