using BeaconRelay.LpdReceiver.Data;

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
    int? RetryPolicyId,
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

public sealed record AdminLoginRequest(
    string Username,
    string Password,
    bool RememberMe);

public sealed record ReorderRulesRequest(
    IReadOnlyList<int> OrderedRuleIds);

public sealed record DeliveryWorkItemQueryResult(
    IReadOnlyList<DeliveryWorkItemRecord> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
