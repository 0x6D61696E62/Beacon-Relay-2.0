using Microsoft.EntityFrameworkCore;

namespace BeaconRelay.LpdReceiver.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ReceivedFileRecord> ReceivedFiles => Set<ReceivedFileRecord>();

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
    }
}
