using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using BeaconRelay.LpdReceiver.Data;
using BeaconRelay.LpdReceiver.Protocol;
using Microsoft.EntityFrameworkCore;

namespace BeaconRelay.LpdReceiver.Services;

public sealed class ProcessingRuleMatcher(AppDbContext dbContext)
{
    public async Task<IReadOnlyList<ProcessingRuleRecord>> GetMatchingRulesAsync(LpdReceivedJob job, CancellationToken cancellationToken)
    {
        var rules = await dbContext.ProcessingRules
            .AsNoTracking()
            .AsSplitQuery()
            .Where(x => x.IsEnabled)
            .Include(x => x.VirtualPrinter)
            .Include(x => x.FolderDestinations.Where(d => d.IsEnabled))
            .Include(x => x.ForwardDestinations.Where(d => d.IsEnabled))
            .OrderBy(x => x.Priority)
            .ToListAsync(cancellationToken);

        var matches = new List<ProcessingRuleRecord>();

        foreach (var rule in rules)
        {
            if (!MatchesRule(rule, job))
            {
                continue;
            }

            matches.Add(rule);

            if (rule.StopProcessingOnMatch)
            {
                break;
            }
        }

        return matches;
    }

    private static bool MatchesRule(ProcessingRuleRecord rule, LpdReceivedJob job)
    {
        var checks = new List<bool>();

        if (!string.IsNullOrWhiteSpace(rule.QueueMatchValue))
        {
            checks.Add(MatchesQueue(rule, job.QueueName));
        }

        if (!string.IsNullOrWhiteSpace(rule.SourceIpCidr))
        {
            checks.Add(MatchesSourceIp(rule, job.RemoteEndpoint.Address));
        }

        if (rule.VirtualPrinterId.HasValue)
        {
            checks.Add(MatchesVirtualPrinter(rule, job.QueueName));
        }

        if (checks.Count == 0)
        {
            return false;
        }

        return string.Equals(rule.MatchOperator, RuleMatchOperator.Or, StringComparison.OrdinalIgnoreCase)
            ? checks.Any(x => x)
            : checks.All(x => x);
    }

    private static bool MatchesQueue(ProcessingRuleRecord rule, string queueName)
    {
        if (string.IsNullOrWhiteSpace(rule.QueueMatchValue))
        {
            return false;
        }

        return rule.QueueMatchType switch
        {
            QueueMatchTypeValues.Exact => string.Equals(rule.QueueMatchValue, queueName, StringComparison.OrdinalIgnoreCase),
            QueueMatchTypeValues.Wildcard => WildcardMatch(rule.QueueMatchValue, queueName),
            QueueMatchTypeValues.Regex => Regex.IsMatch(queueName, rule.QueueMatchValue, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => false,
        };
    }

    private static bool MatchesSourceIp(ProcessingRuleRecord rule, IPAddress sourceAddress)
    {
        if (string.IsNullOrWhiteSpace(rule.SourceIpCidr))
        {
            return false;
        }

        var cidr = rule.SourceIpCidr.Trim();
        var separatorIndex = cidr.IndexOf('/');

        if (separatorIndex < 0)
        {
            return IPAddress.TryParse(cidr, out var exactAddress) && exactAddress.Equals(sourceAddress);
        }

        var ipPart = cidr[..separatorIndex];
        var prefixPart = cidr[(separatorIndex + 1)..];

        if (!IPAddress.TryParse(ipPart, out var networkAddress))
        {
            return false;
        }

        if (!int.TryParse(prefixPart, out var prefixLength))
        {
            return false;
        }

        if (networkAddress.AddressFamily != sourceAddress.AddressFamily)
        {
            return false;
        }

        var bitLength = networkAddress.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;

        if (prefixLength < 0 || prefixLength > bitLength)
        {
            return false;
        }

        return IsInPrefix(sourceAddress.GetAddressBytes(), networkAddress.GetAddressBytes(), prefixLength);
    }

    private static bool MatchesVirtualPrinter(ProcessingRuleRecord rule, string queueName)
    {
        return rule.VirtualPrinter is not null
            && string.Equals(rule.VirtualPrinter.QueueName, queueName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool WildcardMatch(string pattern, string value)
    {
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(value, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsInPrefix(byte[] address, byte[] network, int prefixLength)
    {
        var fullBytes = prefixLength / 8;
        var remainderBits = prefixLength % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (address[i] != network[i])
            {
                return false;
            }
        }

        if (remainderBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xFF << (8 - remainderBits));
        return (address[fullBytes] & mask) == (network[fullBytes] & mask);
    }
}
