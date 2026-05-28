using System.Net;
using System.Net.Sockets;
using System.Text;
using BeaconRelay.LpdReceiver.Data;
using BeaconRelay.LpdReceiver.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BeaconRelay.LpdReceiver.Tests;

public sealed class DeliveryDispatcherServiceTests
{
    [Fact]
    public async Task ProcessOneCycleAsync_FolderSuccess_MarksWorkItemSucceeded()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"beacon-relay-tests-{Guid.NewGuid():N}.db");
        var sourcePath = Path.Combine(Path.GetTempPath(), $"source-{Guid.NewGuid():N}.bin");
        var destinationRoot = Path.Combine(Path.GetTempPath(), $"dest-{Guid.NewGuid():N}");

        await File.WriteAllBytesAsync(sourcePath, Encoding.UTF8.GetBytes("hello"));

        var provider = BuildProvider(dbPath);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var received = new ReceivedFileRecord
        {
            ReceivedUtc = DateTime.UtcNow,
            CreatedUtc = DateTime.UtcNow,
            QueueName = "queueA",
            RemoteHost = "127.0.0.1",
            RemotePort = 515,
            StoredFilePath = sourcePath,
            OriginalFileName = "doc.txt",
            Status = FileRecordStatus.Received,
        };

        db.ReceivedFiles.Add(received);
        await db.SaveChangesAsync();

        var rule = new ProcessingRuleRecord
        {
            Name = "ruleA",
            Priority = 1,
            IsEnabled = true,
            MatchOperator = RuleMatchOperator.And,
            QueueMatchType = QueueMatchTypeValues.Exact,
            QueueMatchValue = "queueA",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };
        db.ProcessingRules.Add(rule);
        await db.SaveChangesAsync();

        var folderDestination = new RuleFolderDestinationRecord
        {
            RuleId = rule.Id,
            IsEnabled = true,
            DestinationOrder = 1,
            RootFolder = destinationRoot,
            SubfolderPatternType = SubfolderPatternTypeValues.DotNetDateFormat,
            SubfolderPattern = "yyyy/MM/dd",
            DuplicatePolicy = DestinationDuplicatePolicy.UniqueName,
            IsQueueOnFailure = true,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };
        db.RuleFolderDestinations.Add(folderDestination);
        await db.SaveChangesAsync();

        db.DeliveryWorkItems.Add(new DeliveryWorkItemRecord
        {
            ReceivedFileId = received.Id,
            RuleId = rule.Id,
            DestinationType = DeliveryDestinationType.Folder,
            DestinationId = folderDestination.Id,
            Status = DeliveryWorkItemStatus.Pending,
            Priority = 1,
            NextAttemptUtc = DateTime.UtcNow.AddSeconds(-1),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var dispatcher = new DeliveryDispatcherService(provider.GetRequiredService<IServiceScopeFactory>(), new DeliveryPauseState(), NullLogger<DeliveryDispatcherService>.Instance);
        var processed = await dispatcher.ProcessOneCycleAsync(CancellationToken.None);

        Assert.True(processed);

        var savedItem = await db.DeliveryWorkItems.AsNoTracking().SingleAsync();
        Assert.Equal(DeliveryWorkItemStatus.Succeeded, savedItem.Status);
        Assert.Equal(1, savedItem.AttemptCount);

        var attempt = await db.DeliveryAttempts.AsNoTracking().SingleAsync();
        Assert.Equal(DeliveryAttemptOutcome.Succeeded, attempt.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(attempt.OutputPath));

        CleanupPath(dbPath);
        CleanupPath(sourcePath);
        CleanupPath(destinationRoot, isDirectory: true);
    }

    [Fact]
    public async Task ProcessOneCycleAsync_FolderFailure_SchedulesRetry()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"beacon-relay-tests-{Guid.NewGuid():N}.db");
        var destinationRoot = Path.Combine(Path.GetTempPath(), $"dest-{Guid.NewGuid():N}");

        var provider = BuildProvider(dbPath);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        db.RetryPolicies.Add(new RetryPolicyRecord
        {
            Name = "test-policy",
            MaxAttempts = 3,
            InitialDelaySeconds = 1,
            BackoffMode = RetryBackoffMode.Fixed,
            MaxDelaySeconds = 10,
            IsEnabled = true,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        });

        var received = new ReceivedFileRecord
        {
            ReceivedUtc = DateTime.UtcNow,
            CreatedUtc = DateTime.UtcNow,
            QueueName = "queueA",
            RemoteHost = "127.0.0.1",
            RemotePort = 515,
            StoredFilePath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.bin"),
            OriginalFileName = "doc.txt",
            Status = FileRecordStatus.Received,
        };

        db.ReceivedFiles.Add(received);
        await db.SaveChangesAsync();

        var rule = new ProcessingRuleRecord
        {
            Name = "ruleA",
            Priority = 1,
            IsEnabled = true,
            MatchOperator = RuleMatchOperator.And,
            QueueMatchType = QueueMatchTypeValues.Exact,
            QueueMatchValue = "queueA",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };
        db.ProcessingRules.Add(rule);
        await db.SaveChangesAsync();

        var folderDestination = new RuleFolderDestinationRecord
        {
            RuleId = rule.Id,
            IsEnabled = true,
            DestinationOrder = 1,
            RootFolder = destinationRoot,
            SubfolderPatternType = SubfolderPatternTypeValues.DotNetDateFormat,
            SubfolderPattern = "yyyy/MM/dd",
            DuplicatePolicy = DestinationDuplicatePolicy.Fail,
            IsQueueOnFailure = true,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };
        db.RuleFolderDestinations.Add(folderDestination);
        await db.SaveChangesAsync();

        db.DeliveryWorkItems.Add(new DeliveryWorkItemRecord
        {
            ReceivedFileId = received.Id,
            RuleId = rule.Id,
            DestinationType = DeliveryDestinationType.Folder,
            DestinationId = folderDestination.Id,
            Status = DeliveryWorkItemStatus.Pending,
            Priority = 1,
            NextAttemptUtc = DateTime.UtcNow.AddSeconds(-1),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var dispatcher = new DeliveryDispatcherService(provider.GetRequiredService<IServiceScopeFactory>(), new DeliveryPauseState(), NullLogger<DeliveryDispatcherService>.Instance);
        var processed = await dispatcher.ProcessOneCycleAsync(CancellationToken.None);

        Assert.True(processed);

        var savedItem = await db.DeliveryWorkItems.AsNoTracking().SingleAsync();
        Assert.Equal(DeliveryWorkItemStatus.RetryScheduled, savedItem.Status);
        Assert.Equal(1, savedItem.AttemptCount);
        Assert.True(savedItem.NextAttemptUtc > DateTime.UtcNow.AddSeconds(-1));

        var attempt = await db.DeliveryAttempts.AsNoTracking().SingleAsync();
        Assert.Equal(DeliveryAttemptOutcome.Failed, attempt.Outcome);

        CleanupPath(dbPath);
        CleanupPath(destinationRoot, isDirectory: true);
    }

    [Fact]
    public async Task ProcessOneCycleAsync_ForwardFailure_SchedulesRetry()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"beacon-relay-tests-{Guid.NewGuid():N}.db");
        var sourcePath = Path.Combine(Path.GetTempPath(), $"source-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(sourcePath, Encoding.UTF8.GetBytes("hello"));

        var provider = BuildProvider(dbPath);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        db.RetryPolicies.Add(new RetryPolicyRecord
        {
            Name = "test-policy",
            MaxAttempts = 3,
            InitialDelaySeconds = 1,
            BackoffMode = RetryBackoffMode.Fixed,
            MaxDelaySeconds = 10,
            IsEnabled = true,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        });

        var received = new ReceivedFileRecord
        {
            ReceivedUtc = DateTime.UtcNow,
            CreatedUtc = DateTime.UtcNow,
            QueueName = "queueA",
            RemoteHost = "127.0.0.1",
            RemotePort = 515,
            StoredFilePath = sourcePath,
            OriginalFileName = "doc.txt",
            Status = FileRecordStatus.Received,
        };

        db.ReceivedFiles.Add(received);
        await db.SaveChangesAsync();

        var rule = new ProcessingRuleRecord
        {
            Name = "ruleA",
            Priority = 1,
            IsEnabled = true,
            MatchOperator = RuleMatchOperator.And,
            QueueMatchType = QueueMatchTypeValues.Exact,
            QueueMatchValue = "queueA",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };
        db.ProcessingRules.Add(rule);
        await db.SaveChangesAsync();

        var forwardDestination = new RuleForwardDestinationRecord
        {
            RuleId = rule.Id,
            IsEnabled = true,
            DestinationOrder = 1,
            Host = "127.0.0.1",
            Port = 65001,
            OutboundQueueName = "queueB",
            CompressMode = ForwardCompressMode.None,
            PayloadMode = ForwardPayloadMode.StoredFile,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };
        db.RuleForwardDestinations.Add(forwardDestination);
        await db.SaveChangesAsync();

        db.DeliveryWorkItems.Add(new DeliveryWorkItemRecord
        {
            ReceivedFileId = received.Id,
            RuleId = rule.Id,
            DestinationType = DeliveryDestinationType.ForwardLpd,
            DestinationId = forwardDestination.Id,
            Status = DeliveryWorkItemStatus.Pending,
            Priority = 1,
            NextAttemptUtc = DateTime.UtcNow.AddSeconds(-1),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var dispatcher = new DeliveryDispatcherService(provider.GetRequiredService<IServiceScopeFactory>(), new DeliveryPauseState(), NullLogger<DeliveryDispatcherService>.Instance);
        var processed = await dispatcher.ProcessOneCycleAsync(CancellationToken.None);

        Assert.True(processed);

        var savedItem = await db.DeliveryWorkItems.AsNoTracking().SingleAsync();
        Assert.Equal(DeliveryWorkItemStatus.RetryScheduled, savedItem.Status);

        var attempt = await db.DeliveryAttempts.AsNoTracking().SingleAsync();
        Assert.Equal(DeliveryAttemptOutcome.Failed, attempt.Outcome);

        CleanupPath(dbPath);
        CleanupPath(sourcePath);
    }

    [Fact]
    public async Task ProcessOneCycleAsync_ForwardSuccess_MarksSucceeded()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"beacon-relay-tests-{Guid.NewGuid():N}.db");
        var sourcePath = Path.Combine(Path.GetTempPath(), $"source-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(sourcePath, Encoding.UTF8.GetBytes("hello"));

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();

            for (var i = 0; i < 5; i++)
            {
                var buffer = new byte[8192];
                _ = await stream.ReadAsync(buffer);
                await stream.WriteAsync(new byte[] { 0 });
                await stream.FlushAsync();
            }
        });

        var provider = BuildProvider(dbPath);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var received = new ReceivedFileRecord
        {
            ReceivedUtc = DateTime.UtcNow,
            CreatedUtc = DateTime.UtcNow,
            QueueName = "queueA",
            RemoteHost = "127.0.0.1",
            RemotePort = 515,
            StoredFilePath = sourcePath,
            OriginalFileName = "doc.txt",
            Status = FileRecordStatus.Received,
        };

        db.ReceivedFiles.Add(received);
        await db.SaveChangesAsync();

        var rule = new ProcessingRuleRecord
        {
            Name = "ruleA",
            Priority = 1,
            IsEnabled = true,
            MatchOperator = RuleMatchOperator.And,
            QueueMatchType = QueueMatchTypeValues.Exact,
            QueueMatchValue = "queueA",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };
        db.ProcessingRules.Add(rule);
        await db.SaveChangesAsync();

        var forwardDestination = new RuleForwardDestinationRecord
        {
            RuleId = rule.Id,
            IsEnabled = true,
            DestinationOrder = 1,
            Host = "127.0.0.1",
            Port = port,
            OutboundQueueName = "queueB",
            CompressMode = ForwardCompressMode.None,
            PayloadMode = ForwardPayloadMode.StoredFile,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };
        db.RuleForwardDestinations.Add(forwardDestination);
        await db.SaveChangesAsync();

        db.DeliveryWorkItems.Add(new DeliveryWorkItemRecord
        {
            ReceivedFileId = received.Id,
            RuleId = rule.Id,
            DestinationType = DeliveryDestinationType.ForwardLpd,
            DestinationId = forwardDestination.Id,
            Status = DeliveryWorkItemStatus.Pending,
            Priority = 1,
            NextAttemptUtc = DateTime.UtcNow.AddSeconds(-1),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var dispatcher = new DeliveryDispatcherService(provider.GetRequiredService<IServiceScopeFactory>(), new DeliveryPauseState(), NullLogger<DeliveryDispatcherService>.Instance);
        var processed = await dispatcher.ProcessOneCycleAsync(CancellationToken.None);

        Assert.True(processed);

        var savedItem = await db.DeliveryWorkItems.AsNoTracking().SingleAsync();
        Assert.Equal(DeliveryWorkItemStatus.Succeeded, savedItem.Status);

        var attempt = await db.DeliveryAttempts.AsNoTracking().SingleAsync();
        Assert.Equal(DeliveryAttemptOutcome.Succeeded, attempt.Outcome);
        Assert.Equal("127.0.0.1", attempt.RemoteHost);
        Assert.Equal("queueB", attempt.QueueNameUsed);

        await serverTask;
        CleanupPath(dbPath);
        CleanupPath(sourcePath);
    }

    private static ServiceProvider BuildProvider(string dbPath)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));
        services.AddSingleton<FolderDeliveryHandler>();
        services.AddSingleton<LpdForwardDeliveryHandler>();
        return services.BuildServiceProvider();
    }

    private static void CleanupPath(string path, bool isDirectory = false)
    {
        try
        {
            if (isDirectory)
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
