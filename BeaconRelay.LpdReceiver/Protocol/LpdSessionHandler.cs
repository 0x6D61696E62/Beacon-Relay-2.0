using System.Net;
using System.Net.Sockets;
using System.Text;
using BeaconRelay.LpdReceiver.Options;
using Microsoft.Extensions.Options;

namespace BeaconRelay.LpdReceiver.Protocol;

public sealed class LpdSessionHandler(
    IOptions<LpdOptions> options,
    ControlFileMetadataParser controlFileMetadataParser,
    ILogger<LpdSessionHandler> logger)
{
    private const byte Ack = 0x00;
    private const byte Nak = 0x01;

    private readonly LpdOptions _options = options.Value;

    public async Task<LpdSessionResult> HandleAsync(TcpClient client, CancellationToken cancellationToken)
    {
        if (client.Client.RemoteEndPoint is not IPEndPoint remoteEndpoint)
        {
            return new LpdSessionResult { Success = false, Error = "Remote endpoint unavailable." };
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.SessionTimeoutSeconds));
        var token = timeoutCts.Token;

        await using var stream = client.GetStream();

        try
        {
            var commandLine = await ReadLineAsync(stream, token);
            var queueName = string.Empty;
            if (commandLine is null || !LpdProtocolParser.TryParseTopLevel(commandLine, out queueName))
            {
                await SendNakAsync(stream, token);
                return new LpdSessionResult { Success = false, QueueName = queueName, Error = "Malformed or unsupported top-level command." };
            }

            await SendAckAsync(stream, token);

            string? rawControl = null;
            string? controlFileName = null;
            var dataFiles = new List<LpdDataFile>();
            var totalBytes = 0L;

            while (true)
            {
                var subLine = await ReadLineAsync(stream, token);
                if (subLine is null)
                {
                    break;
                }

                if (!LpdProtocolParser.TryParseReceiveSubcommand(subLine, out var subCommand, out var byteCount, out var fileName))
                {
                    await SendNakAsync(stream, token);
                    return new LpdSessionResult { Success = false, QueueName = queueName, Error = "Malformed receive-job subcommand." };
                }

                if (byteCount > _options.MaxFileBytes)
                {
                    await SendNakAsync(stream, token);
                    return new LpdSessionResult { Success = false, QueueName = queueName, Error = $"File size {byteCount} exceeds limit {_options.MaxFileBytes}." };
                }

                totalBytes += byteCount;
                if (totalBytes > _options.MaxJobBytes)
                {
                    await SendNakAsync(stream, token);
                    return new LpdSessionResult { Success = false, QueueName = queueName, Error = $"Job size {totalBytes} exceeds limit {_options.MaxJobBytes}." };
                }

                if (subCommand == LpdProtocolParser.ReceiveDataFileCommand && rawControl is null)
                {
                    await SendNakAsync(stream, token);
                    return new LpdSessionResult { Success = false, QueueName = queueName, Error = "Control file must be received before data files." };
                }

                if (subCommand == LpdProtocolParser.ReceiveControlFileCommand && rawControl is not null)
                {
                    await SendNakAsync(stream, token);
                    return new LpdSessionResult { Success = false, QueueName = queueName, Error = "Only one control file is allowed." };
                }

                if (subCommand == LpdProtocolParser.ReceiveDataFileCommand && dataFiles.Count >= _options.MaxFilesPerJob)
                {
                    await SendNakAsync(stream, token);
                    return new LpdSessionResult { Success = false, QueueName = queueName, Error = "Data file count exceeded max files per job." };
                }

                await SendAckAsync(stream, token);
                var payload = await ReadExactAsync(stream, byteCount, token);
                var trailingNul = await ReadExactAsync(stream, 1, token);
                if (trailingNul[0] != 0x00)
                {
                    await SendNakAsync(stream, token);
                    return new LpdSessionResult { Success = false, QueueName = queueName, Error = "Missing required trailing NUL byte." };
                }

                if (subCommand == LpdProtocolParser.ReceiveControlFileCommand)
                {
                    rawControl = Encoding.ASCII.GetString(payload);
                    controlFileName = fileName;
                }
                else
                {
                    dataFiles.Add(new LpdDataFile
                    {
                        Name = fileName,
                        Content = payload,
                    });
                }

                await SendAckAsync(stream, token);
            }

            if (rawControl is null || dataFiles.Count == 0)
            {
                return new LpdSessionResult { Success = false, QueueName = queueName, Error = "Incomplete job. Missing control file or data files." };
            }

            var metadata = controlFileMetadataParser.Parse(rawControl, controlFileName);
            return new LpdSessionResult
            {
                Success = true,
                Job = new LpdReceivedJob
                {
                    QueueName = queueName,
                    RawControlText = rawControl,
                    ControlFileName = controlFileName,
                    Metadata = metadata,
                    DataFiles = dataFiles,
                    RemoteEndpoint = remoteEndpoint,
                },
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("LPD session timed out for {RemoteAddress}:{RemotePort}", remoteEndpoint.Address, remoteEndpoint.Port);
            await TrySendNakAsync(stream, cancellationToken);
            return new LpdSessionResult { Success = false, Error = "Session timeout." };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LPD session failed for {RemoteAddress}:{RemotePort}", remoteEndpoint.Address, remoteEndpoint.Port);
            await TrySendNakAsync(stream, cancellationToken);
            return new LpdSessionResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<byte[]?> ReadLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new List<byte>(128);
        var one = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(one.AsMemory(0, 1), cancellationToken);
            if (read == 0)
            {
                return buffer.Count == 0 ? null : throw new IOException("Unexpected EOF while reading line.");
            }

            if (one[0] == (byte)'\n')
            {
                return buffer.ToArray();
            }

            buffer.Add(one[0]);
            if (buffer.Count > _options.MaxLineLength)
            {
                throw new IOException($"Line length exceeded configured maximum of {_options.MaxLineLength} bytes.");
            }
        }
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;

        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (read == 0)
            {
                throw new IOException("Unexpected EOF while reading payload.");
            }

            offset += read;
        }

        return buffer;
    }

    private static Task SendAckAsync(NetworkStream stream, CancellationToken cancellationToken)
        => stream.WriteAsync(new[] { Ack }, cancellationToken).AsTask();

    private static Task SendNakAsync(NetworkStream stream, CancellationToken cancellationToken)
        => stream.WriteAsync(new[] { Nak }, cancellationToken).AsTask();

    private static async Task TrySendNakAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        try
        {
            await SendNakAsync(stream, cancellationToken);
        }
        catch
        {
            // ignore secondary socket failures
        }
    }
}
