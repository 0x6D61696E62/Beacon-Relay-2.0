namespace BeaconRelay.LpdReceiver.Data;

public sealed class ProcessingRuleRecord
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string MatchOperator { get; set; } = RuleMatchOperator.And;

    public string QueueMatchType { get; set; } = QueueMatchTypeValues.Exact;
    public string? QueueMatchValue { get; set; }
    public string? SourceIpCidr { get; set; }
    public int? VirtualPrinterId { get; set; }
    public bool StopProcessingOnMatch { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public VirtualPrinterRecord? VirtualPrinter { get; set; }
    public ICollection<RuleFolderDestinationRecord> FolderDestinations { get; set; } = [];
    public ICollection<RuleForwardDestinationRecord> ForwardDestinations { get; set; } = [];
}

public static class RuleMatchOperator
{
    public const string And = "And";
    public const string Or = "Or";
}

public static class QueueMatchTypeValues
{
    public const string Exact = "Exact";
    public const string Wildcard = "Wildcard";
    public const string Regex = "Regex";
}
