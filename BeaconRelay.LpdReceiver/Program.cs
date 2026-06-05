using BeaconRelay.LpdReceiver.Data;
using BeaconRelay.LpdReceiver.Health;
using BeaconRelay.LpdReceiver.Options;
using BeaconRelay.LpdReceiver.Protocol;
using BeaconRelay.LpdReceiver.Services;
using BeaconRelay.LpdReceiver.Api;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SQLitePCL;

Batteries_V2.Init();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();

builder.Services.Configure<LpdOptions>(builder.Configuration.GetSection(LpdOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<DeduplicationOptions>(builder.Configuration.GetSection(DeduplicationOptions.SectionName));
builder.Services.Configure<RetryOptions>(builder.Configuration.GetSection(RetryOptions.SectionName));
builder.Services.Configure<RetentionOptions>(builder.Configuration.GetSection(RetentionOptions.SectionName));
builder.Services.Configure<HealthEndpointOptions>(builder.Configuration.GetSection(HealthEndpointOptions.SectionName));
builder.Services.Configure<AdminAuthOptions>(builder.Configuration.GetSection(AdminAuthOptions.SectionName));

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "BeaconRelay.Admin";
        options.LoginPath = "/login.html";
        options.AccessDeniedPath = "/login.html";
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AnyAuthenticated", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("SettingsOrAdmin", policy => policy.RequireRole(AdminRoles.Admin, AdminRoles.Settings));
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(AdminRoles.Admin));
});

var healthOptions = builder.Configuration.GetSection(HealthEndpointOptions.SectionName).Get<HealthEndpointOptions>() ?? new HealthEndpointOptions();
builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(healthOptions.Port));

var databaseOptions = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
var normalizedConnectionString = SqliteDatabasePath.NormalizeConnectionString(databaseOptions.ConnectionString) ?? databaseOptions.ConnectionString;
if (!string.IsNullOrWhiteSpace(databaseOptions.Password))
{
    var connectionBuilder = new SqliteConnectionStringBuilder(normalizedConnectionString)
    {
        Password = databaseOptions.Password
    };
    normalizedConnectionString = connectionBuilder.ToString();
}
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(normalizedConnectionString));
builder.Services.AddHttpClient();

builder.Services.AddSingleton<ListenerState>();
builder.Services.AddSingleton<DeliveryPauseState>();
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
builder.Services.AddHostedService<ListenerAlertService>();
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
    LogEfQueryOptions(scope.ServiceProvider, db);
    var retentionDefaults = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RetentionOptions>>().Value;
    if (db.Database.IsRelational())
    {
        SqliteDatabasePath.EnsureDirectoryExists(db.Database.GetConnectionString());
        await db.Database.MigrateAsync();
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }

    if (!await db.RetentionSettings.AnyAsync(x => x.Id == 1))
    {
        db.RetentionSettings.Add(new RetentionSettingsRecord
        {
            Id = 1,
            IsEnabled = retentionDefaults.Enabled,
            RetentionDays = Math.Max(1, retentionDefaults.RetentionDays),
            IntervalMinutes = Math.Max(1, retentionDefaults.IntervalMinutes),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    if (!await db.AdminUsers.AnyAsync())
    {
        var authDefaults = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AdminAuthOptions>>().Value;
        var bootstrapUser = new AdminUserRecord
        {
            Username = authDefaults.Username,
            Role = AdminRoles.Admin,
            IsEnabled = true,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };

        bootstrapUser.PasswordHash = new PasswordHasher<AdminUserRecord>().HashPassword(bootstrapUser, authDefaults.Password);

        db.AdminUsers.Add(bootstrapUser);
        await db.SaveChangesAsync();
    }

    if (!await db.AlertSettings.AnyAsync(x => x.Id == 1))
    {
        db.AlertSettings.Add(new AlertSettingsRecord
        {
            Id = 1,
            MonitorEnabled = false,
            MonitorIntervalSeconds = 60,
            EmailEnabled = false,
            EmailSmtpPort = 587,
            EmailUseSsl = true,
            ListenerDownEmailCooldownMinutes = 30,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    // Restore delivery pause state from DB so a restart does not silently resume paused delivery
    var pauseRecord = await db.DeliveryPauseState.FindAsync(1);
    if (pauseRecord is { IsPaused: true })
    {
        var pauseState = app.Services.GetRequiredService<DeliveryPauseState>();
        pauseState.Pause(pauseRecord.ResumeAtUtc, pauseRecord.Reason);
    }
}

static void LogEfQueryOptions(IServiceProvider serviceProvider, AppDbContext db)
{
    var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("StartupDiagnostics");
    var options = db.GetService<IDbContextOptions>();
    var relational = options.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault();

    var splitBehavior = relational?.QuerySplittingBehavior switch
    {
        QuerySplittingBehavior.SplitQuery => nameof(QuerySplittingBehavior.SplitQuery),
        QuerySplittingBehavior.SingleQuery => nameof(QuerySplittingBehavior.SingleQuery),
        _ => "ProviderDefault(SingleQuery unless overridden)"
    };

    var splitBehaviorSource = relational?.QuerySplittingBehavior is null
        ? "ProviderDefault"
        : "ExplicitConfiguration";

    logger.LogInformation(
        "EF query options active: provider={Provider}, defaultQuerySplittingBehavior={QuerySplittingBehavior}, queryTrackingBehavior={TrackingBehavior}",
        db.Database.ProviderName,
        splitBehavior,
        db.ChangeTracker.QueryTrackingBehavior);

    logger.LogInformation(
        "EF query splitting source: source={Source}, configuredValue={ConfiguredValue}",
        splitBehaviorSource,
        splitBehavior);
}

var effectiveHealthOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthEndpointOptions>>().Value;

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<AdminSessionGateMiddleware>();
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
app.MapAdminAuthApi();

await app.RunAsync();

//dotnet ef migrations add AddVirtualPrinters --project BeaconRelay.LpdReceiver

public partial class Program;
