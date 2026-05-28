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

    private static async Task<IResult> CreateRuleAsync(ProcessingRuleRecord input, AppDbContext db, CancellationToken cancellationToken)
    {
        input.Id = 0;
        input.CreatedUtc = DateTime.UtcNow;
        input.UpdatedUtc = DateTime.UtcNow;

        db.ProcessingRules.Add(input);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/rules/{input.Id}", input);
    }

    private static async Task<IResult> UpdateRuleAsync(int id, ProcessingRuleRecord input, AppDbContext db, CancellationToken cancellationToken)
    {
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

    private static async Task<IResult> CreateFolderDestinationAsync(RuleFolderDestinationRecord input, AppDbContext db, CancellationToken cancellationToken)
    {
        input.Id = 0;
        input.CreatedUtc = DateTime.UtcNow;
        input.UpdatedUtc = DateTime.UtcNow;

        db.RuleFolderDestinations.Add(input);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/folder-destinations/{input.Id}", input);
    }

    private static async Task<IResult> UpdateFolderDestinationAsync(int id, RuleFolderDestinationRecord input, AppDbContext db, CancellationToken cancellationToken)
    {
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

    private static async Task<IResult> CreateForwardDestinationAsync(RuleForwardDestinationRecord input, AppDbContext db, CancellationToken cancellationToken)
    {
        input.Id = 0;
        input.CreatedUtc = DateTime.UtcNow;
        input.UpdatedUtc = DateTime.UtcNow;

        db.RuleForwardDestinations.Add(input);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/forward-destinations/{input.Id}", input);
    }

    private static async Task<IResult> UpdateForwardDestinationAsync(int id, RuleForwardDestinationRecord input, AppDbContext db, CancellationToken cancellationToken)
    {
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

    private static async Task<IResult> UpdatePurgePolicyAsync(int id, PurgePolicyRecord input, AppDbContext db, CancellationToken cancellationToken)
    {
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
}
