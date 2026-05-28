using System.Net;
using System.Text;
using System.Text.Json;
using BeaconRelay.LpdReceiver.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
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

    private sealed class BeaconWebAppFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

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

                services.AddDbContext<AppDbContext>(opt => opt.UseInMemoryDatabase($"beacon-tests-{Guid.NewGuid():N}"));

                using var scope = services.BuildServiceProvider().CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.EnsureCreated();
            });
        }
    }
}
