using AIVIDEO.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIVIDEO.Server.Data;

/// <summary>
/// Code-first EF Core context targeting PostgreSQL. Schema changes go through migrations:
///   dotnet ef migrations add &lt;Name&gt; --project AIVIDEO.Server
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<GenerationRequest> GenerationRequests => Set<GenerationRequest>();

    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            // Email is the login identifier; a unique index enforces one account per address
            // and backs the lookup on every sign-in.
            entity.HasIndex(e => e.Email).IsUnique();
        });

        modelBuilder.Entity<GenerationRequest>(entity =>
        {
            entity.HasKey(e => e.Id);

            // The polling service scans for non-terminal work every tick; without this index
            // that scan degrades linearly as completed rows accumulate.
            entity.HasIndex(e => new { e.Status, e.NextPollUtc });
            entity.HasIndex(e => e.PolloTaskId);
            entity.HasIndex(e => e.CreatedUtc);
            // Every gallery query filters by owner, newest first.
            entity.HasIndex(e => new { e.UserId, e.CreatedUtc });

            entity.Property(e => e.CostUsd).HasPrecision(18, 6);
            entity.Property(e => e.Credit).HasPrecision(18, 6);

            entity.HasMany(e => e.Assets)
                  .WithOne(a => a.GenerationRequest)
                  .HasForeignKey(a => a.GenerationRequestId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaAsset>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.GenerationRequestId);
            entity.HasIndex(e => e.CreatedUtc);
        });
    }

    public override int SaveChanges()
    {
        StampTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampTimestamps()
    {
        foreach (var entry in ChangeTracker.Entries<GenerationRequest>())
        {
            if (entry.State is EntityState.Modified or EntityState.Added)
            {
                entry.Entity.UpdatedUtc = DateTimeOffset.UtcNow;
            }
        }
    }
}
