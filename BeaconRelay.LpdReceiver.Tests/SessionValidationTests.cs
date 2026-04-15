using System.Net;
using System.Net.Sockets;
using System.Text;
using BeaconRelay.LpdReceiver.Options;
using BeaconRelay.LpdReceiver.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace BeaconRelay.LpdReceiver.Tests;

public sealed class SessionValidationTests
{
    [Fact]
    public async Task HandleAsync_RejectsOversizedFile()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new LpdOptions { MaxFileBytes = 4, MaxJobBytes = 100, SessionTimeoutSeconds = 5 });
        var handler = new LpdSessionHandler(options, new ControlFileMetadataParser(), NullLogger<LpdSessionHandler>.Instance);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;

        using var client = new TcpClient();
        await client.ConnectAsync(endpoint.Address, endpoint.Port);
        using var serverClient = await listener.AcceptTcpClientAsync();

        var handleTask = handler.HandleAsync(serverClient, CancellationToken.None);
        await using var stream = client.GetStream();

        await stream.WriteAsync(BuildLine(LpdProtocolParser.ReceiveJobCommand, "print\n"));
        var firstAck = await ReadByteAsync(stream);
        Assert.Equal(0x00, firstAck);

        await stream.WriteAsync(BuildLine(LpdProtocolParser.ReceiveControlFileCommand, "5 cfA001host\n"));
        var nak = await ReadByteAsync(stream);
        Assert.Equal(0x01, nak);

        client.Close();
        var result = await handleTask;
        Assert.False(result.Success);
    }

    [Fact]
    public async Task HandleAsync_RejectsMissingTrailingNul()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new LpdOptions { MaxFileBytes = 1024, MaxJobBytes = 1024, SessionTimeoutSeconds = 5 });
        var handler = new LpdSessionHandler(options, new ControlFileMetadataParser(), NullLogger<LpdSessionHandler>.Instance);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;

        using var client = new TcpClient();
        await client.ConnectAsync(endpoint.Address, endpoint.Port);
        using var serverClient = await listener.AcceptTcpClientAsync();

        var handleTask = handler.HandleAsync(serverClient, CancellationToken.None);
        await using var stream = client.GetStream();

        await stream.WriteAsync(BuildLine(LpdProtocolParser.ReceiveJobCommand, "print\n"));
        Assert.Equal(0x00, await ReadByteAsync(stream));

        await stream.WriteAsync(BuildLine(LpdProtocolParser.ReceiveControlFileCommand, "6 cfA001host\n"));
        Assert.Equal(0x00, await ReadByteAsync(stream));

        await stream.WriteAsync(Encoding.ASCII.GetBytes("Hhost\n"));
        await stream.FlushAsync();
        client.Close();

        var result = await handleTask;
        Assert.False(result.Success);
    }

    private static byte[] BuildLine(byte cmd, string content)
        => [cmd, .. Encoding.ASCII.GetBytes(content)];

    private static async Task<byte> ReadByteAsync(NetworkStream stream)
    {
        var buffer = new byte[1];
        var read = await stream.ReadAsync(buffer);
        Assert.Equal(1, read);
        return buffer[0];
    }
}
