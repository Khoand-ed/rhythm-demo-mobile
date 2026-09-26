using Microsoft.EntityFrameworkCore;
using Promuse.Persistence.Entities;

namespace Promuse.Persistence;

/// <summary>
/// The whole model. Configured here in <see cref="OnModelCreating"/> rather than
/// with attributes on the entities, so the entity classes stay readable as a
/// description of the game and the database's opinions live in one file.
/// </summary>
public class PromuseDbContext(DbContextOptions<PromuseDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Player> Players => Set<Player>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Account>(entity =>
        {
            entity.ToTable("accounts");
            entity.HasKey(a => a.Id);

            // Both filtered: a guest has no username and a linked account may
            // have no device, so a plain unique index would collide every second
            // row on NULL. Postgres allows many NULLs in a unique index, but the
            // filter also keeps the index to the rows that can actually clash.
            entity.HasIndex(a => a.DeviceId)
                  .IsUnique()
                  .HasFilter("device_id IS NOT NULL");

            entity.HasIndex(a => a.Username)
                  .IsUnique()
                  .HasFilter("username IS NOT NULL");

            entity.Property(a => a.DeviceId).HasMaxLength(128);
            entity.Property(a => a.Username).HasMaxLength(24);
            entity.Property(a => a.PasswordHash).HasMaxLength(256);
        });

        modelBuilder.Entity<Player>(entity =>
        {
            entity.ToTable("players");
            entity.HasKey(p => p.AccountId);

            entity.HasOne(p => p.Account)
                  .WithOne(a => a.Player)
                  .HasForeignKey<Player>(p => p.AccountId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Property(p => p.DisplayName).HasMaxLength(24).IsRequired();
            entity.Property(p => p.DesktopCharacterId).HasMaxLength(32);

            // Optimistic concurrency, wired to EF rather than left to the
            // service layer. With this, a write built from a stale read throws
            // DbUpdateConcurrencyException instead of overwriting - which is
            // what the contract's 412 is made of. Doing it by hand in each
            // handler would mean one forgotten check is one silent data loss.
            entity.Property(p => p.StateVersion).IsConcurrencyToken();
        });
    }
}
