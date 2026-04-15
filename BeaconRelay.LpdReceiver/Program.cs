using BeaconRelay.LpdReceiver.Data;
using BeaconRelay.LpdReceiver.Health;
using BeaconRelay.LpdReceiver.Options;
using BeaconRelay.LpdReceiver.Protocol;
using BeaconRelay.LpdReceiver.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<LpdOptions>(builder.Configuration.GetSection(LpdOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<DeduplicationOptions>(builder.Configuration.GetSection(DeduplicationOptions.SectionName));
builder.Services.Configure<RetentionOptions>(builder.Configuration.GetSection(RetentionOptions.SectionName));
builder.Services.Configure<HealthEndpointOptions>(builder.Configuration.GetSection(HealthEndpointOptions.SectionName));

var healthOptions = builder.Configuration.GetSection(HealthEndpointOptions.SectionName).Get<HealthEndpointOptions>() ?? new HealthEndpointOptions();
builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(healthOptions.Port));

var databaseOptions = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(databaseOptions.ConnectionString));

builder.Services.AddSingleton<ListenerState>();
builder.Services.AddSingleton<Sha256Hasher>();
builder.Services.AddSingleton<FileStorageService>();
builder.Services.AddSingleton<ControlFileMetadataParser>();
builder.Services.AddSingleton<LpdSessionHandler>();
builder.Services.AddScoped<ReceivedFileProcessor>();

builder.Services.AddHostedService<LpdListenerService>();
builder.Services.AddHostedService<RetentionService>();

builder.Services
    .AddHealthChecks()
    .AddCheck<ListenerHealthCheck>("lpd_listener", tags: new[] { "live", "ready" })
    .AddDbContextCheck<AppDbContext>("sqlite", tags: new[] { "ready" });

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

var effectiveHealthOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthEndpointOptions>>().Value;

app.MapHealthChecks(effectiveHealthOptions.HealthPath, new HealthCheckOptions
{
    Predicate = _ => true,
});

app.MapHealthChecks(effectiveHealthOptions.ReadinessPath, new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

await app.RunAsync();
