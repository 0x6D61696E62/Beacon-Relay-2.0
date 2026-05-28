namespace BeaconRelay.LpdReceiver.Data;

using System.Text.Json.Serialization;

public sealed class RuleFolderDestinationRecord
{
    public int Id { get; set; }
    public int RuleId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int DestinationOrder { get; set; }
    public string RootFolder { get; set; } = string.Empty;
    public string SubfolderPatternType { get; set; } = SubfolderPatternTypeValues.DotNetDateFormat;
    public string SubfolderPattern { get; set; } = "yyyy/MM/dd";

    public string DuplicatePolicy { get; set; } = DestinationDuplicatePolicy.UniqueName;
    public string? UniqueNameMode { get; set; } = UniqueNameModeValues.Counter;
    public string? UniqueNameAffix { get; set; }
    public int? RetryPolicyId { get; set; }

    public bool IsQueueOnFailure { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public ProcessingRuleRecord Rule { get; set; } = default!;
    public RetryPolicyRecord? RetryPolicy { get; set; }
}

public static class SubfolderPatternTypeValues
{
    public const string DotNetDateFormat = "DotNetDateFormat";
    public const string TokenTemplate = "TokenTemplate";
}

public static class DestinationDuplicatePolicy
{
    public const string Overwrite = "Overwrite";
    public const string Fail = "Fail";
    public const string UniqueName = "UniqueName";
}

public static class UniqueNameModeValues
{
    public const string Counter = "Counter";
    public const string Timestamp = "Timestamp";
    public const string Guid = "Guid";
    public const string HashFragment = "HashFragment";
}
