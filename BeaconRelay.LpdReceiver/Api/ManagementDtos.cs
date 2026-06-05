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
    bool StopProcessingOnMatch,
    string? HighlightColor);

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

public sealed record RetentionSettingsUpdateRequest(
    bool IsEnabled,
    int RetentionDays,
    int IntervalMinutes);

public sealed record AdminLoginRequest(
    string Username,
    string Password,
    bool RememberMe);

public sealed record AdminUserSummaryResult(
    int Id,
    string Username,
    string Role,
    bool IsEnabled,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record AdminUserCreateRequest(
    string Username,
    string Password,
    string Role,
    bool IsEnabled);

public sealed record AdminUserUpdateRequest(
    string Username,
    string? Password,
    string Role,
    bool IsEnabled);

public sealed record ReorderRulesRequest(
    IReadOnlyList<int> OrderedRuleIds);

public sealed record DeliveryWorkItemQueryResult(
    IReadOnlyList<DeliveryWorkItemRecord> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

/// <summary>
/// Request body for POST /api/delivery/pause.
/// DurationMinutes = null means pause indefinitely.
/// </summary>
public sealed record PauseDeliveryRequest(
    int? DurationMinutes,
    string? Reason);

/// <summary>
/// Returned by GET /api/delivery/status.
/// </summary>
public sealed record DeliveryStatusResponse(
    bool IsPaused,
    DateTime? PausedAtUtc,
    DateTime? ResumeAtUtc,
    string? PauseReason,
    bool IsListening,
    DeliveryQueueStats QueueStats,
    IReadOnlyList<RuleDestinationCountResult> TopRulesByDestinationCount);

public sealed record DeliveryQueueStats(
    int Pending,
    int InProgress,
    int RetryScheduled,
    int Succeeded,
    int Failed,
    int Canceled,
    int Total);

public sealed record RuleDestinationCountResult(
    int RuleId,
    string RuleName,
    int FolderDestinationCount,
    int ForwardDestinationCount,
    int TotalDestinationCount,
    int DeliveredReportCount);

public sealed record AlertSettingsResult(
    bool MonitorEnabled,
    string? MonitorUrl,
    int MonitorIntervalSeconds,
    bool EmailEnabled,
    string? EmailSmtpHost,
    int EmailSmtpPort,
    bool EmailUseSsl,
    string? EmailUsername,
    bool EmailPasswordConfigured,
    string? EmailFrom,
    string? EmailTo,
    int ListenerDownEmailCooldownMinutes,
    DateTime UpdatedUtc);

public sealed record AlertSettingsUpdateRequest(
    bool MonitorEnabled,
    string? MonitorUrl,
    int MonitorIntervalSeconds,
    bool EmailEnabled,
    string? EmailSmtpHost,
    int EmailSmtpPort,
    bool EmailUseSsl,
    string? EmailUsername,
    string? EmailPassword,
    bool ClearEmailPassword,
    string? EmailFrom,
    string? EmailTo,
    int ListenerDownEmailCooldownMinutes);
