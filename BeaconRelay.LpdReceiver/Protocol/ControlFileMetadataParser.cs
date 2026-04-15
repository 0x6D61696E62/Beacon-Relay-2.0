using System.Text.RegularExpressions;

namespace BeaconRelay.LpdReceiver.Protocol;

public sealed class ControlFileMetadataParser
{
    private static readonly Regex JobIdRegex = new("[a-zA-Z]{2,3}(?<id>\\d+)", RegexOptions.Compiled);

    public ControlFileMetadata Parse(string rawControl, string? controlFileName)
    {
        var metadata = new ControlFileMetadata();
        var sourceHints = new List<string>();

        foreach (var line in rawControl.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 2)
            {
                continue;
            }

            var key = line[0];
            var value = line[1..].Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            switch (key)
            {
                case 'J':
                    metadata.JobName ??= value;
                    break;
                case 'P':
                    metadata.UserName ??= value;
                    break;
                case 'H':
                    metadata.HostName ??= value;
                    break;
                case 'C':
                    metadata.BannerClass ??= value;
                    break;
                case 'L':
                    metadata.BannerName ??= value;
                    break;
                case 'N':
                case 'U':
                    sourceHints.Add(value);
                    break;
            }
        }

        metadata.SourceFileHints = sourceHints.Count == 0 ? null : string.Join(';', sourceHints.Distinct(StringComparer.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(controlFileName))
        {
            var match = JobIdRegex.Match(controlFileName);
            if (match.Success)
            {
                metadata.LpdJobId = match.Groups["id"].Value;
            }
        }

        if (metadata.LpdJobId is null && !string.IsNullOrWhiteSpace(metadata.JobName))
        {
            var digits = new string(metadata.JobName.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrWhiteSpace(digits))
            {
                metadata.LpdJobId = digits;
            }
        }

        return metadata;
    }
}
