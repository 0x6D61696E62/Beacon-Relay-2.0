using System.Net;
using System.Text;
using System.Text.Json;
using BeaconRelay.LpdReceiver.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BeaconRelay.LpdReceiver.Tests;

public sealed class ApiValidationTests
{
    [Fact]
    public async Task CreateRule_InvalidMatchOperator_ReturnsBadRequest()
    {
        await using var factory = new BeaconWebAppFactory();
        using var client = factory.CreateClient();

        var payload = """
        {
          "name": "invalid-rule",
          "priority": 1,
          "isEnabled": true,
          "matchOperator": "BadValue",
          "queueMatchType": "Exact",
          "queueMatchValue": "queueA",
          "sourceIpCidr": null,
          "virtualPrinterId": null,
          "stopProcessingOnMatch": false
        }
        """;

        using var response = await client.PostAsync("/api/rules", new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("MatchOperator", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateFolderDestination_EmptyRootFolder_ReturnsBadRequest()
    {
        await using var factory = new BeaconWebAppFactory();
        using var client = factory.CreateClient();

        var payload = """
        {
          "ruleId": 1,
          "isEnabled": true,
          "destinationOrder": 1,
          "rootFolder": "",
          "subfolderPatternType": "DotNetDateFormat",
          "subfolderPattern": "yyyy/MM/dd",
          "duplicatePolicy": "UniqueName",
          "uniqueNameMode": "Counter",
          "uniqueNameAffix": null,
          "isQueueOnFailure": true
        }
        """;

        using var response = await client.PostAsync("/api/folder-destinations", new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("RootFolder", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateForwardDestination_BadPort_ReturnsBadRequest()
    {
        await using var factory = new BeaconWebAppFactory();
        using var client = factory.CreateClient();

        var payload = """
        {
          "ruleId": 1,
          "isEnabled": true,
          "destinationOrder": 1,
          "host": "127.0.0.1",
          "port": 70000,
          "outboundQueueName": "queueB",
          "compressMode": "None",
          "payloadMode": "StoredFile",
          "retryPolicyId": null
        }
        """;

        using var response = await client.PostAsync("/api/forward-destinations", new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Port", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminPage_IsServed()
    {
        await using var factory = new BeaconWebAppFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/admin/index.html");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Beacon Relay Admin", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiRoute_WhenAuthEnabled_WithoutCredentials_ReturnsUnauthorized()
    {
        await using var factory = new BeaconWebAppFactory(enableAdminAuth: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/rules");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminRoute_WhenAuthEnabled_WithoutCredentials_RedirectsToLogin()
    {
        await using var factory = new BeaconWebAppFactory(enableAdminAuth: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/admin/index.html");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login.html", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminRoute_WhenAuthEnabled_WithSessionCookie_ReturnsOk()
    {
        await using var factory = new BeaconWebAppFactory(enableAdminAuth: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        await LoginAsync(client);

        using var response = await client.GetAsync("/admin/index.html");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Beacon Relay Admin", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiRoute_WhenAuthEnabled_WithSessionCookie_AllowsValidationResponse()
    {
        await using var factory = new BeaconWebAppFactory(enableAdminAuth: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        await LoginAsync(client);

        var payload = """
        {
          "name": "invalid-rule",
          "priority": 1,
          "isEnabled": true,
          "matchOperator": "BadValue",
          "queueMatchType": "Exact",
          "queueMatchValue": "queueA",
          "sourceIpCidr": null,
          "virtualPrinterId": null,
          "stopProcessingOnMatch": false
        }
        """;

        using var response = await client.PostAsync("/api/rules", new StringContent(payload, Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("MatchOperator", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WithInvalidPassword_ReturnsUnauthorized()
    {
        await using var factory = new BeaconWebAppFactory(enableAdminAuth: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.PostAsync("/auth/login", new StringContent("{\"username\":\"admin\",\"password\":\"wrong\",\"rememberMe\":false}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

        [Fact]
        public async Task CreateFolderDestination_MissingRuleIdReference_ReturnsBadRequest()
        {
                await using var factory = new BeaconWebAppFactory();
                using var client = factory.CreateClient();

                var payload = """
                {
                    "ruleId": 999,
                    "isEnabled": true,
                    "destinationOrder": 1,
                    "rootFolder": "data/routed",
                    "subfolderPatternType": "DotNetDateFormat",
                    "subfolderPattern": "yyyy/MM/dd",
                    "duplicatePolicy": "UniqueName",
                    "uniqueNameMode": "Counter",
                    "uniqueNameAffix": null,
                    "retryPolicyId": null,
                    "isQueueOnFailure": true
                }
                """;

                using var response = await client.PostAsync("/api/folder-destinations", new StringContent(payload, Encoding.UTF8, "application/json"));
                var body = await response.Content.ReadAsStringAsync();

                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Contains("RuleId", body, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("does not exist", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GetRules_WithDestinations_DoesNotFailWithSerializationCycle()
        {
                await using var factory = new BeaconWebAppFactory();
                using var client = factory.CreateClient();

                var uniqueRuleName = $"cycle-check-rule-{Guid.NewGuid():N}";

                using (var scope = factory.Services.CreateScope())
                {
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var rule = new ProcessingRuleRecord
                        {
                                Name = uniqueRuleName,
                                Priority = 1,
                                IsEnabled = true,
                                MatchOperator = RuleMatchOperator.And,
                                QueueMatchType = QueueMatchTypeValues.Exact,
                                QueueMatchValue = "main",
                                StopProcessingOnMatch = false,
                                CreatedUtc = DateTime.UtcNow,
                                UpdatedUtc = DateTime.UtcNow,
                        };

                        db.ProcessingRules.Add(rule);
                        await db.SaveChangesAsync();

                        db.RuleFolderDestinations.Add(new RuleFolderDestinationRecord
                        {
                                RuleId = rule.Id,
                                IsEnabled = true,
                                DestinationOrder = 1,
                                RootFolder = "data/routed",
                                SubfolderPatternType = SubfolderPatternTypeValues.DotNetDateFormat,
                                SubfolderPattern = "yyyy/MM/dd",
                                DuplicatePolicy = DestinationDuplicatePolicy.UniqueName,
                                UniqueNameMode = UniqueNameModeValues.Counter,
                                IsQueueOnFailure = true,
                                CreatedUtc = DateTime.UtcNow,
                                UpdatedUtc = DateTime.UtcNow,
                        });

                        await db.SaveChangesAsync();
                }

                using var getRulesResponse = await client.GetAsync("/api/rules");
                var getRulesBody = await getRulesResponse.Content.ReadAsStringAsync();

                Assert.Equal(HttpStatusCode.OK, getRulesResponse.StatusCode);
        Assert.Contains(uniqueRuleName, getRulesBody, StringComparison.OrdinalIgnoreCase);
        }

    private sealed class BeaconWebAppFactory(bool enableAdminAuth = false) : WebApplicationFactory<Program>
    {
        private readonly InMemoryDatabaseRoot _databaseRoot = new();
        private readonly string _databaseName = $"beacon-tests-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("AdminAuth:Enabled", enableAdminAuth ? "true" : "false");
            builder.UseSetting("AdminAuth:Username", "admin");
            builder.UseSetting("AdminAuth:Password", "test-secret");

            builder.ConfigureServices(services =>
            {
                var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
                foreach (var descriptor in hosted)
                {
                    services.Remove(descriptor);
                }

                var dbDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (dbDescriptor is not null)
                {
                    services.Remove(dbDescriptor);
                }

                services.AddDbContext<AppDbContext>(opt => opt.UseInMemoryDatabase(_databaseName, _databaseRoot));

                using var scope = services.BuildServiceProvider().CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.EnsureCreated();
            });
        }
    }

    private static async Task LoginAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/auth/login", new StringContent("{\"username\":\"admin\",\"password\":\"test-secret\",\"rememberMe\":false}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
