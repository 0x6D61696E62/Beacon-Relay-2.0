using System.Text;

namespace BeaconRelay.LpdReceiver.Protocol;

public static class LpdProtocolParser
{
    public const byte ReceiveJobCommand = 0x02;
    public const byte ReceiveControlFileCommand = 0x02;
    public const byte ReceiveDataFileCommand = 0x03;

    public static bool TryParseTopLevel(ReadOnlySpan<byte> line, out string queueName)
    {
        queueName = string.Empty;
        if (line.Length < 2 || line[0] != ReceiveJobCommand)
        {
            return false;
        }

        queueName = Encoding.ASCII.GetString(line[1..]).Trim();
        return !string.IsNullOrWhiteSpace(queueName);
    }

    public static bool TryParseReceiveSubcommand(ReadOnlySpan<byte> line, out byte subcommand, out int byteCount, out string fileName)
    {
        subcommand = 0;
        byteCount = 0;
        fileName = string.Empty;

        if (line.Length < 4)
        {
            return false;
        }

        subcommand = line[0];
        if (subcommand != ReceiveControlFileCommand && subcommand != ReceiveDataFileCommand)
        {
            return false;
        }

        var payload = Encoding.ASCII.GetString(line[1..]).Trim();
        var separator = payload.IndexOf(' ');
        if (separator <= 0 || separator == payload.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(payload[..separator], out byteCount) || byteCount < 0)
        {
            return false;
        }

        fileName = payload[(separator + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(fileName);
    }
}
