using System.Text;
using BeaconRelay.LpdReceiver.Protocol;

namespace BeaconRelay.LpdReceiver.Tests;

public sealed class ProtocolParserTests
{
    [Fact]
    public void ParseTopLevel_RejectsMalformedLine()
    {
        var ok = LpdProtocolParser.TryParseTopLevel([0x03, (byte)'q'], out _);
        Assert.False(ok);
    }

    [Fact]
    public void ParseSubcommand_RejectsBadLength()
    {
        var line = BuildLine(LpdProtocolParser.ReceiveControlFileCommand, "3x cfA001host");
        var ok = LpdProtocolParser.TryParseReceiveSubcommand(line, out _, out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void ParseSubcommand_ParsesControlFileCommand()
    {
        var line = BuildLine(LpdProtocolParser.ReceiveControlFileCommand, "10 cfA001host");
        var ok = LpdProtocolParser.TryParseReceiveSubcommand(line, out var command, out var length, out var fileName);

        Assert.True(ok);
        Assert.Equal(LpdProtocolParser.ReceiveControlFileCommand, command);
        Assert.Equal(10, length);
        Assert.Equal("cfA001host", fileName);
    }

    private static byte[] BuildLine(byte cmd, string content)
        => [cmd, .. Encoding.ASCII.GetBytes(content)];
}
