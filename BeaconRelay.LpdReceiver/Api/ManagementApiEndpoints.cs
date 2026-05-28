using BeaconRelay.LpdReceiver.Data;
using Microsoft.EntityFrameworkCore;

namespace BeaconRelay.LpdReceiver.Api;

public static class ManagementApiEndpoints
{
    public static void MapManagementApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        var rules = api.MapGroup("/rules");
        rules.MapGet("/", GetRulesAsync);
        rules.MapGet("/{id:int}", GetRuleByIdAsync);
        rules.MapPost("/", CreateRuleAsync);
        rules.MapPut("/{id:int}", UpdateRuleAsync);
        rules.MapDelete("/{id:int}", DeleteRuleAsync);

        var folders = api.MapGroup("/folder-destinations");
        folders.MapPost("/", CreateFolderDestinationAsync);
        folders.MapPut("/{id:int}", UpdateFolderDestinationAsync);
        folders.MapDelete("/{id:int}", DeleteFolderDestinationAsync);

        var forwards = api.MapGroup("/forward-destinations");
        forwards.MapPost("/", CreateForwardDestinationAsync);
        forwards.MapPut("/{id:int}", UpdateForwardDestinationAsync);
        forwards.MapDelete("/{id:int}", DeleteForwardDestinationAsync);

        var delivery = api.MapGroup("/delivery");
        delivery.MapGet("/work-items", GetDeliveryWorkItemsAsync);
        delivery.MapGet("/work-items/{id:guid}", GetDeliveryWorkItemByIdAsync);
        delivery.MapPost("/work-items/{id:guid}/retry-now", RetryDeliveryWorkItemNowAsync);
        delivery.MapPost("/work-items/{id:guid}/cancel", CancelDeliveryWorkItemAsync);

        var purge = api.MapGroup("/purge-policies");
        purge.MapGet("/", GetPurgePoliciesAsync);
        purge.MapPut("/{id:int}", UpdatePurgePolicyAsync);
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
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var record = new ProcessingRuleRecord
        {
            Name = input.Name,
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
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/rules/{record.Id}", record);
    }

    private static async Task<IResult> UpdateRuleAsync(int id, ProcessingRuleUpsertRequest input, AppDbContext db, CancellationToken cancellationToken)
    {
        var errors = ValidateRule(input);
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
        existing.Priority = input.Priority;
        existing.IsEnabled = input.IsEnabled;
        existing.MatchOperator = input.MatchOperator;
        existing.QueueMatchType = input.QueueMatchType;
        existing.QueueMatchValue = input.QueueMatchValue;
        existing.SourceIpCidr = input.SourceIpCidr;
        existing.VirtualPrinterId = input.VirtualPrinterId;
        existing.StopProcessingOnMatch = input.StopProcessingOnMatch;
        existing.UpdatedUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(existing);
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
            IsQueueOnFailure = input.IsQueueOnFailure,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };

        db.RuleFolderDestinations.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/folder-destinations/{record.Id}", record);
    }

    private static async Task<IResult> UpdateFolderDestinationAsync(int id, RuleFolderDestinationUpsertRequest input, AppDbContext db, CancellationToken cancellationToken)
    {
        var errors = ValidateFolderDestination(input);
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
        existing.IsQueueOnFailure = input.IsQueueOnFailure;
        existing.UpdatedUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
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
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/forward-destinations/{record.Id}", record);
    }

    private static async Task<IResult> UpdateForwardDestinationAsync(int id, RuleForwardDestinationUpsertRequest input, AppDbContext db, CancellationToken cancellationToken)
    {
        var errors = ValidateForwardDestination(input);
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

        await db.SaveChangesAsync(cancellationToken);
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

    private static async Task<IResult> GetDeliveryWorkItemsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var data = await db.DeliveryWorkItems
            .AsNoTracking()
            .Include(x => x.Attempts)
            .OrderByDescending(x => x.CreatedUtc)
            .Take(500)
            .ToListAsync(cancellationToken);

        return Results.Ok(data);
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

    private static async Task<IResult> GetPurgePoliciesAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var data = await db.PurgePolicies.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
        return Results.Ok(data);
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

        return errors;
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
}
