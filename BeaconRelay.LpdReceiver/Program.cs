using BeaconRelay.LpdReceiver.Data;
using BeaconRelay.LpdReceiver.Health;
using BeaconRelay.LpdReceiver.Options;
using BeaconRelay.LpdReceiver.Protocol;
using BeaconRelay.LpdReceiver.Services;
using BeaconRelay.LpdReceiver.Api;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<LpdOptions>(builder.Configuration.GetSection(LpdOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<DeduplicationOptions>(builder.Configuration.GetSection(DeduplicationOptions.SectionName));
builder.Services.Configure<RetentionOptions>(builder.Configuration.GetSection(RetentionOptions.SectionName));
builder.Services.Configure<HealthEndpointOptions>(builder.Configuration.GetSection(HealthEndpointOptions.SectionName));
builder.Services.Configure<AdminAuthOptions>(builder.Configuration.GetSection(AdminAuthOptions.SectionName));

var healthOptions = builder.Configuration.GetSection(HealthEndpointOptions.SectionName).Get<HealthEndpointOptions>() ?? new HealthEndpointOptions();
builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(healthOptions.Port));

var databaseOptions = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
var normalizedConnectionString = SqliteDatabasePath.NormalizeConnectionString(databaseOptions.ConnectionString) ?? databaseOptions.ConnectionString;
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(normalizedConnectionString));

builder.Services.AddSingleton<ListenerState>();
builder.Services.AddSingleton<Sha256Hasher>();
builder.Services.AddSingleton<FileStorageService>();
builder.Services.AddSingleton<ControlFileMetadataParser>();
builder.Services.AddSingleton<LpdSessionHandler>();
builder.Services.AddSingleton<FolderDeliveryHandler>();
builder.Services.AddSingleton<LpdForwardDeliveryHandler>();
builder.Services.AddScoped<ProcessingRuleMatcher>();
builder.Services.AddScoped<DeliveryWorkItemEnqueuer>();
builder.Services.AddScoped<ReceivedFileProcessor>();

builder.Services.AddHostedService<LpdListenerService>();
builder.Services.AddHostedService<RetentionService>();
builder.Services.AddHostedService<DeliveryDispatcherService>();

builder.Services
    .AddHealthChecks()
    .AddCheck<ListenerHealthCheck>("lpd_listener", tags: new[] { "live", "ready" })
    .AddDbContextCheck<AppDbContext>("sqlite", tags: new[] { "ready" });

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational())
    {
        SqliteDatabasePath.EnsureDirectoryExists(db.Database.GetConnectionString());
        await db.Database.MigrateAsync();
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }
}

var effectiveHealthOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthEndpointOptions>>().Value;

app.UseMiddleware<AdminBasicAuthMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthChecks(effectiveHealthOptions.HealthPath, new HealthCheckOptions
{
    Predicate = _ => true,
});

app.MapHealthChecks(effectiveHealthOptions.ReadinessPath, new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

app.MapManagementApi();

await app.RunAsync();

//dotnet ef migrations add AddVirtualPrinters --project BeaconRelay.LpdReceiver

public partial class Program;
