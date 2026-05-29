using System.Security.Claims;
using BeaconRelay.LpdReceiver.Data;
using BeaconRelay.LpdReceiver.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BeaconRelay.LpdReceiver.Api;

public static class ManagementApiEndpoints
{
    public static void MapManagementApi(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization("AnyAuthenticated");

        var rules = api.MapGroup("/rules");
        rules.MapGet("/", GetRulesAsync);
        rules.MapGet("/{id:int}", GetRuleByIdAsync);
        rules.MapPost("/", CreateRuleAsync).RequireAuthorization("SettingsOrAdmin");
        rules.MapPost("/reorder", ReorderRulesAsync).RequireAuthorization("SettingsOrAdmin");
        rules.MapPut("/{id:int}", UpdateRuleAsync).RequireAuthorization("SettingsOrAdmin");
        rules.MapDelete("/{id:int}", DeleteRuleAsync).RequireAuthorization("SettingsOrAdmin");

        var folders = api.MapGroup("/folder-destinations");
        folders.MapPost("/", CreateFolderDestinationAsync).RequireAuthorization("SettingsOrAdmin");
        folders.MapPut("/{id:int}", UpdateFolderDestinationAsync).RequireAuthorization("SettingsOrAdmin");
        folders.MapDelete("/{id:int}", DeleteFolderDestinationAsync).RequireAuthorization("SettingsOrAdmin");

        var forwards = api.MapGroup("/forward-destinations");
        forwards.MapPost("/", CreateForwardDestinationAsync).RequireAuthorization("SettingsOrAdmin");
        forwards.MapPut("/{id:int}", UpdateForwardDestinationAsync).RequireAuthorization("SettingsOrAdmin");
        forwards.MapDelete("/{id:int}", DeleteForwardDestinationAsync).RequireAuthorization("SettingsOrAdmin");

        var delivery = api.MapGroup("/delivery");
        delivery.MapGet("/work-items", GetDeliveryWorkItemsAsync);
        delivery.MapGet("/work-items/{id:guid}", GetDeliveryWorkItemByIdAsync);
        delivery.MapPost("/work-items/{id:guid}/retry-now", RetryDeliveryWorkItemNowAsync).RequireAuthorization("SettingsOrAdmin");
        delivery.MapPost("/work-items/{id:guid}/cancel", CancelDeliveryWorkItemAsync).RequireAuthorization("SettingsOrAdmin");
        delivery.MapGet("/status", GetDeliveryStatusAsync);
        delivery.MapPost("/pause", PauseDeliveryAsync).RequireAuthorization("SettingsOrAdmin");
        delivery.MapPost("/resume", ResumeDeliveryAsync).RequireAuthorization("SettingsOrAdmin");

        var retryPolicies = api.MapGroup("/retry-policies");
        retryPolicies.MapGet("/", GetRetryPoliciesAsync);

        var purge = api.MapGroup("/purge-policies");
        purge.MapGet("/", GetPurgePoliciesAsync);
        purge.MapPut("/{id:int}", UpdatePurgePolicyAsync).RequireAuthorization("SettingsOrAdmin");

        var retention = api.MapGroup("/retention-settings");
        retention.MapGet("/", GetRetentionSettingsAsync);
        retention.MapPut("/", UpdateRetentionSettingsAsync).RequireAuthorization("SettingsOrAdmin");

        var adminUsers = api.MapGroup("/admin-users").RequireAuthorization("AdminOnly");
        adminUsers.MapGet("/", GetAdminUsersAsync);
        adminUsers.MapPost("/", CreateAdminUserAsync);
        adminUsers.MapPut("/{id:int}", UpdateAdminUserAsync);
        adminUsers.MapDelete("/{id:int}", DeleteAdminUserAsync);
    }

    private static async Task<IResult> GetAdminUsersAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var users = await db.AdminUsers
            .AsNoTracking()
            .OrderBy(x => x.Username)
            .Select(x => new AdminUserSummaryResult(
                x.Id,
                x.Username,
                x.Role,
                x.IsEnabled,
                x.CreatedUtc,
                x.UpdatedUtc))
            .ToListAsync(cancellationToken);

        return Results.Ok(users);
    }

    private static async Task<IResult> CreateAdminUserAsync(AdminUserCreateRequest input, AppDbContext db, CancellationToken cancellationToken)
    {
        var errors = ValidateAdminUser(input.Username, input.Password, input.Role, isCreate: true);
        if (await db.AdminUsers.AnyAsync(x => x.Username == input.Username, cancellationToken))
        {
            errors[nameof(input.Username)] = ["A user with this username already exists."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var user = new AdminUserRecord
        {
            Username = input.Username.Trim(),
            Role = input.Role,
            IsEnabled = input.IsEnabled,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };

        user.PasswordHash = new PasswordHasher<AdminUserRecord>().HashPassword(user, input.Password);

        db.AdminUsers.Add(user);
        var saveError = await TrySaveChangesAsync(db, cancellationToken);
        if (saveError is not null)
        {
            return saveError;
        }

        return Results.Created($"/api/admin-users/{user.Id}", new AdminUserSummaryResult(user.Id, user.Username, user.Role, user.IsEnabled, user.CreatedUtc, user.UpdatedUtc));
    }

    private static async Task<IResult> UpdateAdminUserAsync(int id, AdminUserUpdateRequest input, HttpContext context, AppDbContext db, CancellationToken cancellationToken)
    {
        var errors = ValidateAdminUser(input.Username, input.Password, input.Role, isCreate: false);
        if (await db.AdminUsers.AnyAsync(x => x.Id != id && x.Username == input.Username, cancellationToken))
        {
            errors[nameof(input.Username)] = ["A user with this username already exists."];
        }

        var currentUserIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isCurrentUser = int.TryParse(currentUserIdClaim, out var currentUserId) && currentUserId == id;
        if (isCurrentUser && !string.Equals(input.Role, AdminRoles.Admin, StringComparison.Ordinal))
        {
            errors[nameof(input.Role)] = ["You cannot change your own role away from Admin."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var user = await db.AdminUsers.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (user is null)
        {
            return Results.NotFound();
        }

        user.Username = input.Username.Trim();
        user.Role = input.Role;
        user.IsEnabled = input.IsEnabled;
        user.UpdatedUtc = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(input.Password))
        {
            user.PasswordHash = new PasswordHasher<AdminUserRecord>().HashPassword(user, input.Password);
        }

        var saveError = await TrySaveChangesAsync(db, cancellationToken);
        if (saveError is not null)
        {
            return saveError;
        }

        return Results.Ok(new AdminUserSummaryResult(user.Id, user.Username, user.Role, user.IsEnabled, user.CreatedUtc, user.UpdatedUtc));
    }

    private static async Task<IResult> DeleteAdminUserAsync(int id, AppDbContext db, CancellationToken cancellationToken)
    {
        var user = await db.AdminUsers.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (user is null)
        {
            return Results.NotFound();
        }

        var remainingAdmins = await db.AdminUsers.CountAsync(x => x.Id != id && x.Role == AdminRoles.Admin && x.IsEnabled, cancellationToken);
        if (user.Role == AdminRoles.Admin && user.IsEnabled && remainingAdmins == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(AdminUserRecord.Role)] = ["Cannot remove the last enabled administrator."]
            });
        }

        db.AdminUsers.Remove(user);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetRulesAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var data = await db.ProcessingRules
            .AsNoTracking()
            .Include(x => x.VirtualPrinter)
            .Include(x => x.FolderDestinations)
            .Include(x => x.ForwardDestinations)
            .OrderBy(x => x.Priority)
            .ToListAsync(cancellationToken);

        return Results.Ok(data);
    }

    private static async Task<IResult> GetRuleByIdAsync(int id, AppDbContext db, CancellationToken cancellationToken)
    {
        var data = await db.ProcessingRules
            .AsNoTracking()
            .Include(x => x.VirtualPrinter)
            .Include(x => x.FolderDestinations)
            .Include(x => x.ForwardDestinations)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return data is null ? Results.NotFound() : Results.Ok(data);
    }

    private static async Task<IResult> CreateRuleAsync(ProcessingRuleUpsertRequest input, AppDbContext db, CancellationToken cancellationToken)
    {
        var errors = ValidateRule(input);
        if (await db.ProcessingRules.AnyAsync(x => x.Name == input.Name, cancellationToken))
        {
            errors[nameof(input.Name)] = ["A rule with this name already exists."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var record = new ProcessingRuleRecord
        {
            Name = input.Name,
            HighlightColor = NormalizeHighlightColor(input.HighlightColor),
            Priority = input.Priority,
            IsEnabled = input.IsEnabled,
            MatchOperator = input.MatchOperator,
            QueueMatchType = input.QueueMatchType,
            QueueMatchValue = input.QueueMatchValue,
            SourceIpCidr = input.SourceIpCidr,
            VirtualPrinterId = input.VirtualPrinterId,
            StopProcessingOnMatch = input.StopProcessingOnMatch,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };

        db.ProcessingRules.Add(record);
        var saveError = await TrySaveChangesAsync(db, cancellationToken);
        if (saveError is not null)
        {
            return saveError;
        }

        return Results.Created($"/api/rules/{record.Id}", record);
    }

    private static async Task<IResult> UpdateRuleAsync(int id, ProcessingRuleUpsertRequest input, AppDbContext db, CancellationToken cancellationToken)
    {
        var errors = ValidateRule(input);
        if (await db.ProcessingRules.AnyAsync(x => x.Id != id && x.Name == input.Name, cancellationToken))
        {
            errors[nameof(input.Name)] = ["A rule with this name already exists."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var existing = await db.ProcessingRules.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        existing.Name = input.Name;
        existing.HighlightColor = NormalizeHighlightColor(input.HighlightColor);
        existing.Priority = input.Priority;
        existing.IsEnabled = input.IsEnabled;
        existing.MatchOperator = input.MatchOperator;
        existing.QueueMatchType = input.QueueMatchType;
        existing.QueueMatchValue = input.QueueMatchValue;
        existing.SourceIpCidr = input.SourceIpCidr;
        existing.VirtualPrinterId = input.VirtualPrinterId;
        existing.StopProcessingOnMatch = input.StopProcessingOnMatch;
        existing.UpdatedUtc = DateTime.UtcNow;

        var saveError = await TrySaveChangesAsync(db, cancellationToken);
        if (saveError is not null)
        {
            return saveError;
        }

        return Results.Ok(existing);
    }

    private static async Task<IResult> ReorderRulesAsync(ReorderRulesRequest input, AppDbContext db, CancellationToken cancellationToken)
    {
        if (input.OrderedRuleIds.Count == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(input.OrderedRuleIds)] = ["At least one rule id is required."] });
        }

        var rules = await db.ProcessingRules
            .Where(x => input.OrderedRuleIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        if (rules.Count != input.OrderedRuleIds.Count)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(input.OrderedRuleIds)] = ["One or more rule ids were not found."] });
        }

        for (var index = 0; index < input.OrderedRuleIds.Count; index++)
        {
            var rule = rules[input.OrderedRuleIds[index]];
            rule.Priority = (index + 1) * 10;
            rule.UpdatedUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> DeleteRuleAsync(int id, AppDbContext db, CancellationToken cancellationToken)
    {
        var existing = await db.ProcessingRules.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        db.ProcessingRules.Remove(existing);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateFolderDestinationAsync(RuleFolderDestinationUpsertRequest input, AppDbContext db, CancellationToken cancellationToken)
    {
        var errors = ValidateFolderDestination(input);
        await AppendFolderDestinationForeignKeyErrorsAsync(input, errors, db, cancellationToken);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var record = new RuleFolderDestinationRecord
        {
            RuleId = input.RuleId,
            IsEnabled = input.IsEnabled,
            DestinationOrder = input.DestinationOrder,
            RootFolder = input.RootFolder,
            SubfolderPatternType = input.SubfolderPatternType,
            SubfolderPattern = input.SubfolderPattern,
            DuplicatePolicy = input.DuplicatePolicy,
            UniqueNameMode = input.UniqueNameMode,
            UniqueNameAffix = input.UniqueNameAffix,
            RetryPolicyId = input.RetryPolicyId,
            IsQueueOnFailure = input.IsQueueOnFailure,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };

        db.RuleFolderDestinations.Add(record);
        var saveError = await TrySaveChangesAsync(db, cancellationToken);
        if (saveError is not null)
        {
            return saveError;
        }

        return Results.Created($"/api/folder-destinations/{record.Id}", record);
    }

    private static async Task<IResult> UpdateFolderDestinationAsync(int id, RuleFolderDestinationUpsertRequest input, AppDbContext db, CancellationToken cancellationToken)
    {
        var errors = ValidateFolderDestination(input);
        await AppendFolderDestinationForeignKeyErrorsAsync(input, errors, db, cancellationToken);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var existing = await db.RuleFolderDestinations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        existing.IsEnabled = input.IsEnabled;
        existing.DestinationOrder = input.DestinationOrder;
        existing.RootFolder = input.RootFolder;
        existing.SubfolderPatternType = input.SubfolderPatternType;
        existing.SubfolderPattern = input.SubfolderPattern;
        existing.DuplicatePolicy = input.DuplicatePolicy;
        existing.UniqueNameMode = input.UniqueNameMode;
        existing.UniqueNameAffix = input.UniqueNameAffix;
        existing.RetryPolicyId = input.RetryPolicyId;
        existing.IsQueueOnFailure = input.IsQueueOnFailure;
        existing.UpdatedUtc = DateTime.UtcNow;

        var saveError = await TrySaveChangesAsync(db, cancellationToken);
        if (saveError is not null)
        {
            return saveError;
        }

        return Results.Ok(existing);
    }

    private static async Task<IResult> DeleteFolderDestinationAsync(int id, AppDbContext db, CancellationToken cancellationToken)
    {
        var existing = await db.RuleFolderDestinations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        db.RuleFolderDestinations.Remove(existing);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateForwardDestinationAsync(RuleForwardDestinationUpsertRequest input, AppDbContext db, CancellationToken cancellationToken)
    {
        var errors = ValidateForwardDestination(input);
        await AppendForwardDestinationForeignKeyErrorsAsync(input, errors, db, cancellationToken);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var record = new RuleForwardDestinationRecord
        {
            RuleId = input.RuleId,
            IsEnabled = input.IsEnabled,
            DestinationOrder = input.DestinationOrder,
            Host = input.Host,
            Port = input.Port,
            OutboundQueueName = input.OutboundQueueName,
            CompressMode = input.CompressMode,
            PayloadMode = input.PayloadMode,
            RetryPolicyId = input.RetryPolicyId,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };

        db.RuleForwardDestinations.Add(record);
        var saveError = await TrySaveChangesAsync(db, cancellationToken);
        if (saveError is not null)
        {
            return saveError;
        }

        return Results.Created($"/api/forward-destinations/{record.Id}", record);
    }

    private static async Task<IResult> UpdateForwardDestinationAsync(int id, RuleForwardDestinationUpsertRequest input, AppDbContext db, CancellationToken cancellationToken)
    {
        var errors = ValidateForwardDestination(input);
        await AppendForwardDestinationForeignKeyErrorsAsync(input, errors, db, cancellationToken);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var existing = await db.RuleForwardDestinations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        existing.IsEnabled = input.IsEnabled;
        existing.DestinationOrder = input.DestinationOrder;
        existing.Host = input.Host;
        existing.Port = input.Port;
        existing.OutboundQueueName = input.OutboundQueueName;
        existing.CompressMode = input.CompressMode;
        existing.PayloadMode = input.PayloadMode;
        existing.RetryPolicyId = input.RetryPolicyId;
        existing.UpdatedUtc = DateTime.UtcNow;

        var saveError = await TrySaveChangesAsync(db, cancellationToken);
        if (saveError is not null)
        {
            return saveError;
        }

        return Results.Ok(existing);
    }

    private static async Task<IResult> DeleteForwardDestinationAsync(int id, AppDbContext db, CancellationToken cancellationToken)
    {
        var existing = await db.RuleForwardDestinations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        db.RuleForwardDestinations.Remove(existing);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetDeliveryWorkItemsAsync(AppDbContext db, string? status, string? search, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.DeliveryWorkItems
            .AsNoTracking()
            .Include(x => x.Attempts)
            .Include(x => x.ReceivedFile)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                x.DestinationType.Contains(term)
                || x.Status.Contains(term)
                || (x.LastErrorMessage != null && x.LastErrorMessage.Contains(term))
                || (x.ReceivedFile != null && x.ReceivedFile.QueueName.Contains(term))
                || (x.ReceivedFile != null && x.ReceivedFile.OriginalFileName != null && x.ReceivedFile.OriginalFileName.Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(page, totalPages);

        var items = await query
            .OrderByDescending(x => x.CreatedUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var result = new DeliveryWorkItemQueryResult(items, page, pageSize, totalCount, totalPages);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetDeliveryWorkItemByIdAsync(Guid id, AppDbContext db, CancellationToken cancellationToken)
    {
        var data = await db.DeliveryWorkItems
            .AsNoTracking()
            .Include(x => x.Attempts)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return data is null ? Results.NotFound() : Results.Ok(data);
    }

    private static async Task<IResult> RetryDeliveryWorkItemNowAsync(Guid id, AppDbContext db, CancellationToken cancellationToken)
    {
        var item = await db.DeliveryWorkItems.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
        {
            return Results.NotFound();
        }

        item.Status = DeliveryWorkItemStatus.RetryScheduled;
        item.NextAttemptUtc = DateTime.UtcNow;
        item.LockedBy = null;
        item.LockExpiresUtc = null;
        item.UpdatedUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(item);
    }

    private static async Task<IResult> CancelDeliveryWorkItemAsync(Guid id, AppDbContext db, CancellationToken cancellationToken)
    {
        var item = await db.DeliveryWorkItems.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
        {
            return Results.NotFound();
        }

        item.Status = DeliveryWorkItemStatus.Canceled;
        item.LockedBy = null;
        item.LockExpiresUtc = null;
        item.UpdatedUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(item);
    }

    private static async Task<IResult> GetDeliveryStatusAsync(
        AppDbContext db,
        DeliveryPauseState pauseState,
        ListenerState listenerState,
        CancellationToken cancellationToken)
    {
        var snapshot = pauseState.Snapshot;

        var stats = await db.DeliveryWorkItems
            .AsNoTracking()
            .GroupBy(_ => true)
            .Select(g => new
            {
                Pending = g.Count(x => x.Status == DeliveryWorkItemStatus.Pending),
                InProgress = g.Count(x => x.Status == DeliveryWorkItemStatus.InProgress),
                RetryScheduled = g.Count(x => x.Status == DeliveryWorkItemStatus.RetryScheduled),
                Succeeded = g.Count(x => x.Status == DeliveryWorkItemStatus.Succeeded),
                Failed = g.Count(x => x.Status == DeliveryWorkItemStatus.Failed),
                Canceled = g.Count(x => x.Status == DeliveryWorkItemStatus.Canceled),
                Total = g.Count()
            })
            .FirstOrDefaultAsync(cancellationToken);

        var queueStats = stats is null
            ? new DeliveryQueueStats(0, 0, 0, 0, 0, 0, 0)
            : new DeliveryQueueStats(
                stats.Pending,
                stats.InProgress,
                stats.RetryScheduled,
                stats.Succeeded,
                stats.Failed,
                stats.Canceled,
                stats.Total);

        var topRuleRows = await db.ProcessingRules
            .AsNoTracking()
            .Select(x => new
            {
                x.Id,
                x.Name,
                FolderDestinationCount = x.FolderDestinations.Count,
                ForwardDestinationCount = x.ForwardDestinations.Count,
                TotalDestinationCount = x.FolderDestinations.Count + x.ForwardDestinations.Count
            })
            .OrderByDescending(x => x.TotalDestinationCount)
            .ThenBy(x => x.Name)
            .Take(10)
            .ToListAsync(cancellationToken);

        var topRuleIds = topRuleRows.Select(x => x.Id).ToList();
        var deliveredReportCounts = await db.DeliveryWorkItems
            .AsNoTracking()
            .Where(x => x.Status == DeliveryWorkItemStatus.Succeeded && topRuleIds.Contains(x.RuleId))
            .GroupBy(x => x.RuleId)
            .Select(g => new
            {
                RuleId = g.Key,
                DeliveredReportCount = g.Select(x => x.ReceivedFileId).Distinct().Count()
            })
            .ToDictionaryAsync(x => x.RuleId, x => x.DeliveredReportCount, cancellationToken);

        var topRules = topRuleRows
            .Select(x => new RuleDestinationCountResult(
                x.Id,
                x.Name,
                x.FolderDestinationCount,
                x.ForwardDestinationCount,
                x.TotalDestinationCount,
                deliveredReportCounts.GetValueOrDefault(x.Id, 0)))
            .ToList();

        return Results.Ok(new DeliveryStatusResponse(
            snapshot.IsPaused,
            snapshot.PausedAtUtc,
            snapshot.ResumeAtUtc,
            snapshot.Reason,
            listenerState.IsListening,
            queueStats,
            topRules));
    }

    private static async Task<IResult> PauseDeliveryAsync(
        PauseDeliveryRequest input,
        AppDbContext db,
        DeliveryPauseState pauseState,
        CancellationToken cancellationToken)
    {
        if (input.DurationMinutes.HasValue && input.DurationMinutes.Value <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(input.DurationMinutes)] = ["DurationMinutes must be greater than 0 when specified."]
            });
        }

        var resumeAt = input.DurationMinutes.HasValue
            ? DateTime.UtcNow.AddMinutes(input.DurationMinutes.Value)
            : (DateTime?)null;

        pauseState.Pause(resumeAt, input.Reason);

        // Persist across restarts
        var record = await db.DeliveryPauseState.FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (record is null)
        {
            record = new DeliveryPauseRecord { Id = 1 };
            db.DeliveryPauseState.Add(record);
        }

        record.IsPaused = true;
        record.PausedAtUtc = pauseState.PausedAtUtc;
        record.ResumeAtUtc = resumeAt;
        record.Reason = input.Reason;
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(pauseState.Snapshot);
    }

    private static async Task<IResult> ResumeDeliveryAsync(
        AppDbContext db,
        DeliveryPauseState pauseState,
        CancellationToken cancellationToken)
    {
        pauseState.Resume();

        var record = await db.DeliveryPauseState.FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (record is not null)
        {
            record.IsPaused = false;
            record.ResumeAtUtc = null;
            record.PausedAtUtc = null;
            record.Reason = null;
            await db.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(pauseState.Snapshot);
    }

    private static async Task<IResult> GetPurgePoliciesAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var data = await db.PurgePolicies.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
        return Results.Ok(data);
    }

    private static async Task<IResult> GetRetryPoliciesAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var data = await db.RetryPolicies.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
        return Results.Ok(data);
    }

    private static async Task<IResult> GetRetentionSettingsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var settings = await db.RetentionSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, cancellationToken)
            ?? new RetentionSettingsRecord();
        return Results.Ok(settings);
    }

    private static async Task<IResult> UpdateRetentionSettingsAsync(RetentionSettingsUpdateRequest input, AppDbContext db, CancellationToken cancellationToken)
    {
        var errors = ValidateRetentionSettings(input);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var settings = await db.RetentionSettings.FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (settings is null)
        {
            settings = new RetentionSettingsRecord { Id = 1, CreatedUtc = DateTime.UtcNow };
            db.RetentionSettings.Add(settings);
        }

        settings.IsEnabled = input.IsEnabled;
        settings.RetentionDays = input.RetentionDays;
        settings.IntervalMinutes = input.IntervalMinutes;
        settings.UpdatedUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(settings);
    }

    private static async Task<IResult> UpdatePurgePolicyAsync(int id, PurgePolicyUpdateRequest input, AppDbContext db, CancellationToken cancellationToken)
    {
        var errors = ValidatePurgePolicy(input);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var existing = await db.PurgePolicies.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        existing.IsEnabled = input.IsEnabled;
        existing.ApplyTo = input.ApplyTo;
        existing.RetentionDays = input.RetentionDays;
        existing.TerminalStatusesCsv = input.TerminalStatusesCsv;
        existing.IntervalMinutes = input.IntervalMinutes;
        existing.UpdatedUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(existing);
    }

    private static Dictionary<string, string[]> ValidateRule(ProcessingRuleUpsertRequest input)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            errors[nameof(input.Name)] = ["Name is required."];
        }

        if (input.Priority < 0)
        {
            errors[nameof(input.Priority)] = ["Priority must be 0 or greater."];
        }

        if (!string.Equals(input.MatchOperator, RuleMatchOperator.And, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(input.MatchOperator, RuleMatchOperator.Or, StringComparison.OrdinalIgnoreCase))
        {
            errors[nameof(input.MatchOperator)] = ["MatchOperator must be And or Or."];
        }

        if (!string.Equals(input.QueueMatchType, QueueMatchTypeValues.Exact, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(input.QueueMatchType, QueueMatchTypeValues.Wildcard, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(input.QueueMatchType, QueueMatchTypeValues.Regex, StringComparison.OrdinalIgnoreCase))
        {
            errors[nameof(input.QueueMatchType)] = ["QueueMatchType must be Exact, Wildcard, or Regex."];
        }

        if (!string.IsNullOrWhiteSpace(input.HighlightColor) && !IsHexColor(input.HighlightColor))
        {
            errors[nameof(input.HighlightColor)] = ["HighlightColor must be a valid hex color like #3A7BD5."];
        }

        return errors;
    }

    private static string? NormalizeHighlightColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith('#'))
        {
            trimmed = "#" + trimmed;
        }

        return trimmed.ToUpperInvariant();
    }

    private static bool IsHexColor(string value)
    {
        var text = value.Trim();
        if (text.Length == 6)
        {
            text = "#" + text;
        }

        if (text.Length != 7 || text[0] != '#')
        {
            return false;
        }

        for (var i = 1; i < text.Length; i++)
        {
            var c = text[i];
            var isHexDigit = (c >= '0' && c <= '9')
                || (c >= 'a' && c <= 'f')
                || (c >= 'A' && c <= 'F');
            if (!isHexDigit)
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<string, string[]> ValidateFolderDestination(RuleFolderDestinationUpsertRequest input)
    {
        var errors = new Dictionary<string, string[]>();

        if (input.RuleId <= 0)
        {
            errors[nameof(input.RuleId)] = ["RuleId is required."];
        }

        if (string.IsNullOrWhiteSpace(input.RootFolder))
        {
            errors[nameof(input.RootFolder)] = ["RootFolder is required."];
        }

        if (string.IsNullOrWhiteSpace(input.SubfolderPattern))
        {
            errors[nameof(input.SubfolderPattern)] = ["SubfolderPattern is required."];
        }

        if (!string.Equals(input.SubfolderPatternType, SubfolderPatternTypeValues.DotNetDateFormat, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(input.SubfolderPatternType, SubfolderPatternTypeValues.TokenTemplate, StringComparison.OrdinalIgnoreCase))
        {
            errors[nameof(input.SubfolderPatternType)] = ["SubfolderPatternType must be DotNetDateFormat or TokenTemplate."];
        }

        if (!string.Equals(input.DuplicatePolicy, DestinationDuplicatePolicy.Overwrite, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(input.DuplicatePolicy, DestinationDuplicatePolicy.Fail, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(input.DuplicatePolicy, DestinationDuplicatePolicy.UniqueName, StringComparison.OrdinalIgnoreCase))
        {
            errors[nameof(input.DuplicatePolicy)] = ["DuplicatePolicy must be Overwrite, Fail, or UniqueName."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateForwardDestination(RuleForwardDestinationUpsertRequest input)
    {
        var errors = new Dictionary<string, string[]>();

        if (input.RuleId <= 0)
        {
            errors[nameof(input.RuleId)] = ["RuleId is required."];
        }

        if (string.IsNullOrWhiteSpace(input.Host))
        {
            errors[nameof(input.Host)] = ["Host is required."];
        }

        if (input.Port < 1 || input.Port > 65535)
        {
            errors[nameof(input.Port)] = ["Port must be 1-65535."];
        }

        if (string.IsNullOrWhiteSpace(input.OutboundQueueName))
        {
            errors[nameof(input.OutboundQueueName)] = ["OutboundQueueName is required."];
        }

        if (!string.Equals(input.CompressMode, ForwardCompressMode.None, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(input.CompressMode, ForwardCompressMode.ZipArchive, StringComparison.OrdinalIgnoreCase))
        {
            errors[nameof(input.CompressMode)] = ["CompressMode must be None or ZipArchive."];
        }

        if (!string.Equals(input.PayloadMode, ForwardPayloadMode.OriginalFile, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(input.PayloadMode, ForwardPayloadMode.StoredFile, StringComparison.OrdinalIgnoreCase))
        {
            errors[nameof(input.PayloadMode)] = ["PayloadMode must be OriginalFile or StoredFile."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateRetentionSettings(RetentionSettingsUpdateRequest input)
    {
        var errors = new Dictionary<string, string[]>();

        if (input.RetentionDays < 1)
        {
            errors[nameof(input.RetentionDays)] = ["RetentionDays must be at least 1."];
        }

        if (input.IntervalMinutes < 1)
        {
            errors[nameof(input.IntervalMinutes)] = ["IntervalMinutes must be at least 1."];
        }

        return errors;
    }

    private static async Task AppendFolderDestinationForeignKeyErrorsAsync(
        RuleFolderDestinationUpsertRequest input,
        Dictionary<string, string[]> errors,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await db.ProcessingRules.AnyAsync(x => x.Id == input.RuleId, cancellationToken))
        {
            errors[nameof(input.RuleId)] = ["RuleId does not exist."];
        }

        if (input.RetryPolicyId.HasValue
            && !await db.RetryPolicies.AnyAsync(x => x.Id == input.RetryPolicyId.Value, cancellationToken))
        {
            errors[nameof(input.RetryPolicyId)] = ["RetryPolicyId does not exist."];
        }
    }

    private static async Task AppendForwardDestinationForeignKeyErrorsAsync(
        RuleForwardDestinationUpsertRequest input,
        Dictionary<string, string[]> errors,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await db.ProcessingRules.AnyAsync(x => x.Id == input.RuleId, cancellationToken))
        {
            errors[nameof(input.RuleId)] = ["RuleId does not exist."];
        }

        if (input.RetryPolicyId.HasValue
            && !await db.RetryPolicies.AnyAsync(x => x.Id == input.RetryPolicyId.Value, cancellationToken))
        {
            errors[nameof(input.RetryPolicyId)] = ["RetryPolicyId does not exist."];
        }
    }

    private static async Task<IResult?> TrySaveChangesAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException ex)
        {
            var detail = ex.InnerException?.Message ?? ex.Message;
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["database"] = [$"Failed to save configuration. {detail}"]
            });
        }
    }

    private static Dictionary<string, string[]> ValidatePurgePolicy(PurgePolicyUpdateRequest input)
    {
        var errors = new Dictionary<string, string[]>();

        if (!string.Equals(input.ApplyTo, PurgeApplyTarget.DeliveryAttempts, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(input.ApplyTo, PurgeApplyTarget.DeliveryWorkItemsTerminal, StringComparison.OrdinalIgnoreCase))
        {
            errors[nameof(input.ApplyTo)] = ["ApplyTo must be DeliveryAttempts or DeliveryWorkItemsTerminal."];
        }

        if (input.RetentionDays < 1)
        {
            errors[nameof(input.RetentionDays)] = ["RetentionDays must be at least 1."];
        }

        if (input.IntervalMinutes < 1)
        {
            errors[nameof(input.IntervalMinutes)] = ["IntervalMinutes must be at least 1."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateAdminUser(string username, string? password, string role, bool isCreate)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(username))
        {
            errors[nameof(username)] = ["Username is required."];
        }
        else if (username.Trim().Length > 128)
        {
            errors[nameof(username)] = ["Username must be 128 characters or fewer."];
        }

        if (isCreate && string.IsNullOrWhiteSpace(password))
        {
            errors[nameof(password)] = ["Password is required."];
        }
        else if (!string.IsNullOrWhiteSpace(password) && password.Trim().Length < 8)
        {
            errors[nameof(password)] = ["Password must be at least 8 characters."];
        }

        var isValidRole = string.Equals(role, AdminRoles.Admin, StringComparison.Ordinal)
            || string.Equals(role, AdminRoles.Settings, StringComparison.Ordinal)
            || string.Equals(role, AdminRoles.ReadOnly, StringComparison.Ordinal);

        if (!isValidRole)
        {
            errors[nameof(role)] = ["Role must be Admin, Settings, or ReadOnly."];
        }

        return errors;
    }
}
