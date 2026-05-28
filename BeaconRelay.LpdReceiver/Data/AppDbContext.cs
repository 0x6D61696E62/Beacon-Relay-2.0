using Microsoft.EntityFrameworkCore;

namespace BeaconRelay.LpdReceiver.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ReceivedFileRecord> ReceivedFiles => Set<ReceivedFileRecord>();
    public DbSet<VirtualPrinterRecord> VirtualPrinters => Set<VirtualPrinterRecord>();
    public DbSet<ProcessingRuleRecord> ProcessingRules => Set<ProcessingRuleRecord>();
    public DbSet<RuleFolderDestinationRecord> RuleFolderDestinations => Set<RuleFolderDestinationRecord>();
    public DbSet<RuleForwardDestinationRecord> RuleForwardDestinations => Set<RuleForwardDestinationRecord>();
    public DbSet<RetryPolicyRecord> RetryPolicies => Set<RetryPolicyRecord>();
    public DbSet<DeliveryWorkItemRecord> DeliveryWorkItems => Set<DeliveryWorkItemRecord>();
    public DbSet<DeliveryAttemptRecord> DeliveryAttempts => Set<DeliveryAttemptRecord>();
    public DbSet<PurgePolicyRecord> PurgePolicies => Set<PurgePolicyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ReceivedFileRecord>();
        entity.HasKey(x => x.Id);

        entity.Property(x => x.QueueName).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RemoteHost).HasMaxLength(256).IsRequired();
        entity.Property(x => x.LpdJobId).HasMaxLength(64);
        entity.Property(x => x.OriginalFileName).HasMaxLength(512);
        entity.Property(x => x.StoredFilePath).HasMaxLength(2048);
        entity.Property(x => x.Sha256).HasMaxLength(64);
        entity.Property(x => x.JobName).HasMaxLength(512);
        entity.Property(x => x.UserName).HasMaxLength(256);
        entity.Property(x => x.HostName).HasMaxLength(256);
        entity.Property(x => x.BannerClass).HasMaxLength(256);
        entity.Property(x => x.BannerName).HasMaxLength(256);
        entity.Property(x => x.SourceFileHints).HasMaxLength(2048);
        entity.Property(x => x.ControlFileName).HasMaxLength(512);
        entity.Property(x => x.Status).HasMaxLength(64).IsRequired();

        entity.HasIndex(x => x.ReceivedUtc);
        entity.HasIndex(x => x.Sha256);
        entity.HasIndex(x => x.Status);
        entity.HasIndex(x => x.LpdJobId);

        entity.HasMany<DeliveryWorkItemRecord>()
            .WithOne(x => x.ReceivedFile)
            .HasForeignKey(x => x.ReceivedFileId)
            .OnDelete(DeleteBehavior.Cascade);

        var virtualPrinter = modelBuilder.Entity<VirtualPrinterRecord>();
        virtualPrinter.HasKey(x => x.Id);
        virtualPrinter.Property(x => x.VirtualPrinterName).HasMaxLength(256).IsRequired();
        virtualPrinter.Property(x => x.QueueName).HasMaxLength(256).IsRequired();
        virtualPrinter.Property(x => x.Description).HasMaxLength(1024);
        virtualPrinter.HasIndex(x => x.VirtualPrinterName).IsUnique();
        virtualPrinter.HasIndex(x => x.ListenPort);
        virtualPrinter.HasIndex(x => x.QueueName);

        var rule = modelBuilder.Entity<ProcessingRuleRecord>();
        rule.HasKey(x => x.Id);
        rule.Property(x => x.Name).HasMaxLength(256).IsRequired();
        rule.Property(x => x.MatchOperator).HasMaxLength(16).IsRequired();
        rule.Property(x => x.QueueMatchType).HasMaxLength(32).IsRequired();
        rule.Property(x => x.QueueMatchValue).HasMaxLength(256);
        rule.Property(x => x.SourceIpCidr).HasMaxLength(64);
        rule.HasIndex(x => x.Priority);
        rule.HasIndex(x => x.IsEnabled);
        rule.HasIndex(x => x.Name).IsUnique();

        rule.HasOne(x => x.VirtualPrinter)
            .WithMany()
            .HasForeignKey(x => x.VirtualPrinterId)
            .OnDelete(DeleteBehavior.SetNull);

        var folderDestination = modelBuilder.Entity<RuleFolderDestinationRecord>();
        folderDestination.HasKey(x => x.Id);
        folderDestination.Property(x => x.RootFolder).HasMaxLength(2048).IsRequired();
        folderDestination.Property(x => x.SubfolderPatternType).HasMaxLength(64).IsRequired();
        folderDestination.Property(x => x.SubfolderPattern).HasMaxLength(256).IsRequired();
        folderDestination.Property(x => x.DuplicatePolicy).HasMaxLength(32).IsRequired();
        folderDestination.Property(x => x.UniqueNameMode).HasMaxLength(32);
        folderDestination.Property(x => x.UniqueNameAffix).HasMaxLength(64);
        folderDestination.HasIndex(x => x.RuleId);
        folderDestination.HasIndex(x => x.IsEnabled);
        folderDestination.HasIndex(x => x.RetryPolicyId);

        folderDestination.HasOne(x => x.Rule)
            .WithMany(x => x.FolderDestinations)
            .HasForeignKey(x => x.RuleId)
            .OnDelete(DeleteBehavior.Cascade);

        folderDestination.HasOne(x => x.RetryPolicy)
            .WithMany()
            .HasForeignKey(x => x.RetryPolicyId)
            .OnDelete(DeleteBehavior.SetNull);

        var forwardDestination = modelBuilder.Entity<RuleForwardDestinationRecord>();
        forwardDestination.HasKey(x => x.Id);
        forwardDestination.Property(x => x.Host).HasMaxLength(256).IsRequired();
        forwardDestination.Property(x => x.OutboundQueueName).HasMaxLength(128).IsRequired();
        forwardDestination.Property(x => x.CompressMode).HasMaxLength(32).IsRequired();
        forwardDestination.Property(x => x.PayloadMode).HasMaxLength(32).IsRequired();
        forwardDestination.HasIndex(x => x.RuleId);
        forwardDestination.HasIndex(x => x.IsEnabled);

        forwardDestination.HasOne(x => x.Rule)
            .WithMany(x => x.ForwardDestinations)
            .HasForeignKey(x => x.RuleId)
            .OnDelete(DeleteBehavior.Cascade);

        forwardDestination.HasOne(x => x.RetryPolicy)
            .WithMany(x => x.ForwardDestinations)
            .HasForeignKey(x => x.RetryPolicyId)
            .OnDelete(DeleteBehavior.SetNull);

        var retryPolicy = modelBuilder.Entity<RetryPolicyRecord>();
        retryPolicy.HasKey(x => x.Id);
        retryPolicy.Property(x => x.Name).HasMaxLength(128).IsRequired();
        retryPolicy.Property(x => x.BackoffMode).HasMaxLength(32).IsRequired();
        retryPolicy.HasIndex(x => x.Name).IsUnique();
        retryPolicy.HasIndex(x => x.IsEnabled);

        var purgePolicy = modelBuilder.Entity<PurgePolicyRecord>();
        purgePolicy.HasKey(x => x.Id);
        purgePolicy.Property(x => x.Name).HasMaxLength(128).IsRequired();
        purgePolicy.Property(x => x.ApplyTo).HasMaxLength(64).IsRequired();
        purgePolicy.Property(x => x.TerminalStatusesCsv).HasMaxLength(512);
        purgePolicy.HasIndex(x => x.Name).IsUnique();
        purgePolicy.HasIndex(x => x.IsEnabled);

        var workItem = modelBuilder.Entity<DeliveryWorkItemRecord>();
        workItem.HasKey(x => x.Id);
        workItem.Property(x => x.DestinationType).HasMaxLength(32).IsRequired();
        workItem.Property(x => x.Status).HasMaxLength(32).IsRequired();
        workItem.Property(x => x.LastErrorCode).HasMaxLength(64);
        workItem.Property(x => x.LastErrorMessage).HasMaxLength(2048);
        workItem.Property(x => x.LockedBy).HasMaxLength(128);
        workItem.HasIndex(x => new { x.Status, x.NextAttemptUtc, x.Priority });
        workItem.HasIndex(x => x.ReceivedFileId);
        workItem.HasIndex(x => x.RuleId);
        workItem.HasIndex(x => x.DestinationType);
        workItem.HasIndex(x => x.LockExpiresUtc);

        workItem.HasOne(x => x.Rule)
            .WithMany()
            .HasForeignKey(x => x.RuleId)
            .OnDelete(DeleteBehavior.Cascade);

        var attempt = modelBuilder.Entity<DeliveryAttemptRecord>();
        attempt.HasKey(x => x.Id);
        attempt.Property(x => x.Outcome).HasMaxLength(32).IsRequired();
        attempt.Property(x => x.OutputPath).HasMaxLength(2048);
        attempt.Property(x => x.RemoteHost).HasMaxLength(256);
        attempt.Property(x => x.QueueNameUsed).HasMaxLength(128);
        attempt.Property(x => x.ZipCreatedPath).HasMaxLength(2048);
        attempt.Property(x => x.ErrorCode).HasMaxLength(64);
        attempt.Property(x => x.ErrorMessage).HasMaxLength(2048);
        attempt.HasIndex(x => new { x.WorkItemId, x.AttemptNumber }).IsUnique();
        attempt.HasIndex(x => x.StartedUtc);
        attempt.HasIndex(x => x.Outcome);

        attempt.HasOne(x => x.WorkItem)
            .WithMany(x => x.Attempts)
            .HasForeignKey(x => x.WorkItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
