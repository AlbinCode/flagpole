using Flagpole.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flagpole.Api.Data;

public sealed class FlagpoleDbContext(DbContextOptions<FlagpoleDbContext> options) : DbContext(options)
{
    public DbSet<FeatureFlag> Flags => Set<FeatureFlag>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FeatureFlag>(flag =>
        {
            flag.ToTable("flags");
            flag.HasKey(f => f.Id);
            flag.Property(f => f.Id).ValueGeneratedNever();
            flag.HasIndex(f => f.Key).IsUnique();
            flag.Property(f => f.Key).HasMaxLength(63).IsRequired();
            flag.Property(f => f.Description).HasMaxLength(FlagRules.MaxDescriptionLength);
            flag.HasMany(f => f.Environments).WithOne().HasForeignKey("FlagId").OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FlagEnvironment>(environment =>
        {
            environment.ToTable("flag_environments");
            environment.HasKey(e => e.Id);

            // Ids are set in code. Without this EF assumes an environment added to
            // an existing flag is already in the database and tries an UPDATE.
            environment.Property(e => e.Id).ValueGeneratedNever();
            environment.Property(e => e.Name).HasMaxLength(32).IsRequired();
            environment.HasIndex("FlagId", nameof(FlagEnvironment.Name)).IsUnique();
            environment.Property(e => e.TargetUsers);
        });

        modelBuilder.Entity<AuditEntry>(audit =>
        {
            audit.ToTable("audit_entries");
            audit.HasKey(a => a.Id);
            audit.HasIndex(a => a.FlagKey);
            audit.Property(a => a.FlagKey).HasMaxLength(63).IsRequired();
            audit.Property(a => a.Action).HasMaxLength(64).IsRequired();
            audit.Property(a => a.Actor).HasMaxLength(64).IsRequired();
            audit.Property(a => a.Environment).HasMaxLength(32);
        });
    }
}

/// <summary>
/// Who changed what and when. Flags change production behaviour without a
/// deploy, so every change is recorded.
/// </summary>
public sealed class AuditEntry
{
    public long Id { get; set; }

    public required string FlagKey { get; set; }

    public required string Action { get; set; }

    public string? Environment { get; set; }

    public required string Actor { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public string? Details { get; set; }
}

/// <summary>
/// Lets "dotnet ef migrations add" create the context without starting the app.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FlagpoleDbContext>
{
    public FlagpoleDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<FlagpoleDbContext>()
            .UseNpgsql("Host=localhost;Database=flagpole")
            .Options);
}
