using Microsoft.EntityFrameworkCore;
using MoD.SecureProxy.Data.Entities;

namespace MoD.SecureProxy.Data;

public sealed class ProxyDbContext(DbContextOptions<ProxyDbContext> options) : DbContext(options)
{
    public DbSet<Destination> Destinations => Set<Destination>();
    public DbSet<Operation> Operations => Set<Operation>();
    public DbSet<ParameterRule> ParameterRules => Set<ParameterRule>();

    public DbSet<HeaderRule> HeaderRules => Set<HeaderRule>();
    public DbSet<EncryptionProfile> EncryptionProfiles => Set<EncryptionProfile>();
    public DbSet<RequestLog> RequestLogs => Set<RequestLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Destination>(entity =>
        {
            entity.ToTable("Destinations");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Name).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.BaseUrl).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<Operation>(entity =>
        {
            entity.ToTable("Operations");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OperationKey).IsUnique();
            entity.Property(x => x.OperationKey).HasMaxLength(150).IsRequired();
            entity.Property(x => x.HttpMethod).HasMaxLength(10).IsRequired();
            entity.Property(x => x.RelativePath).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.AllowUnknownParameters).HasDefaultValue(false);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasOne(x => x.Destination)
                .WithMany(x => x.Operations)
                .HasForeignKey(x => x.DestinationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.EncryptionProfile)
                .WithMany(x => x.Operations)
                .HasForeignKey(x => x.EncryptionProfileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ParameterRule>(entity =>
        {
            entity.ToTable("ParameterRules");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OperationId, x.ParameterName }).IsUnique();
            entity.Property(x => x.ParameterName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.DataType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Location).HasConversion<string>().HasMaxLength(10);
            entity.HasOne(x => x.Operation)
                .WithMany(x => x.ParameterRules)
                .HasForeignKey(x => x.OperationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(x => x.MinimumValue).HasPrecision(18, 4);
            entity.Property(x => x.MaximumValue).HasPrecision(18, 4);
        });

        modelBuilder.Entity<HeaderRule>(entity =>
        {
            entity.ToTable("HeaderRules");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OperationId, x.HeaderName }).IsUnique();
            entity.Property(x => x.HeaderName).HasMaxLength(256).IsRequired();
            entity.HasOne(x => x.Operation)
                .WithMany(x => x.HeaderRules)
                .HasForeignKey(x => x.OperationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EncryptionProfile>(entity =>
        {
            entity.ToTable("EncryptionProfiles");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Name).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.KeySourceReference).HasMaxLength(512).IsRequired();
            entity.Property(x => x.MetadataJson).HasColumnType("nvarchar(max)");
        });

        modelBuilder.Entity<RequestLog>(entity =>
        {
            entity.ToTable("RequestLogs");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.CorrelationId);
            entity.HasIndex(x => x.StartedAtUtc);
            entity.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.OperationKey).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Outcome).HasMaxLength(40).IsRequired();
            entity.Property(x => x.FailureReason).HasMaxLength(300);
            entity.HasOne<Destination>()
                .WithMany()
                .HasForeignKey(x => x.DestinationId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
