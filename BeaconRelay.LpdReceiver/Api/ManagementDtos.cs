namespace BeaconRelay.LpdReceiver.Api;

public sealed record ProcessingRuleUpsertRequest(
    string Name,
    int Priority,
    bool IsEnabled,
    string MatchOperator,
    string QueueMatchType,
    string? QueueMatchValue,
    string? SourceIpCidr,
    int? VirtualPrinterId,
    bool StopProcessingOnMatch);

public sealed record RuleFolderDestinationUpsertRequest(
    int RuleId,
    bool IsEnabled,
    int DestinationOrder,
    string RootFolder,
    string SubfolderPatternType,
    string SubfolderPattern,
    string DuplicatePolicy,
    string? UniqueNameMode,
    string? UniqueNameAffix,
    bool IsQueueOnFailure);

public sealed record RuleForwardDestinationUpsertRequest(
    int RuleId,
    bool IsEnabled,
    int DestinationOrder,
    string Host,
    int Port,
    string OutboundQueueName,
    string CompressMode,
    string PayloadMode,
    int? RetryPolicyId);

public sealed record PurgePolicyUpdateRequest(
    bool IsEnabled,
    string ApplyTo,
    int RetentionDays,
    string? TerminalStatusesCsv,
    int IntervalMinutes);
