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

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<PlayerCharacter> PlayerCharacters => Set<PlayerCharacter>();

    public DbSet<PlayerItem> PlayerItems => Set<PlayerItem>();

    public DbSet<SquadSlot> SquadSlots => Set<SquadSlot>();

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

        // 三张表都用组合主键 / All three use a composite key rather than a surrogate
        // id. The natural key already is the identity - a player owns at most one
        // stack of an item and at most one copy of an operator - so a separate id
        // column would add a way for the same thing to exist twice.

        modelBuilder.Entity<PlayerCharacter>(entity =>
        {
            entity.ToTable("player_characters");
            entity.HasKey(c => new { c.AccountId, c.CharacterId });

            entity.Property(c => c.CharacterId).HasMaxLength(32);

            entity.HasOne(c => c.Player)
                  .WithMany(p => p.Characters)
                  .HasForeignKey(c => c.AccountId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlayerItem>(entity =>
        {
            entity.ToTable("player_items");
            entity.HasKey(i => new { i.AccountId, i.ItemId });

            // 数据库来兜底 / The rule that a stack is never negative is enforced
            // here rather than only in the service, so a Phase 3 purchase that
            // forgets to check cannot overdraw a currency. A CHECK constraint
            // costs nothing and is the last line that always runs.
            entity.ToTable(t => t.HasCheckConstraint("ck_player_items_amount_positive", "amount > 0"));

            entity.HasOne(i => i.Player)
                  .WithMany(p => p.Items)
                  .HasForeignKey(i => i.AccountId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SquadSlot>(entity =>
        {
            entity.ToTable("squad_slots");
            entity.HasKey(s => new { s.AccountId, s.Slot });

            entity.Property(s => s.CharacterId).HasMaxLength(32);

            entity.ToTable(t => t.HasCheckConstraint("ck_squad_slots_range", "slot >= 0 AND slot <= 3"));

            entity.HasOne(s => s.Player)
                  .WithMany(p => p.Squad)
                  .HasForeignKey(s => s.AccountId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(t => t.Id);

            // SHA-256 hex is exactly 64 characters. Unique because presenting a
            // token is a lookup by this column, and two rows sharing one would
            // make that lookup ambiguous at the worst possible moment.
            entity.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(t => t.TokenHash).IsUnique();

            // Revoking a family is a range update over this column, which is the
            // hot path of the reuse response - it runs while an attacker and a
            // real player are both mid-request.
            entity.HasIndex(t => t.FamilyId);

            entity.HasOne(t => t.Account)
                  .WithMany()
                  .HasForeignKey(t => t.AccountId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("idempotency_records");
            entity.HasKey(r => r.Id);

            entity.Property(r => r.Key).HasMaxLength(128).IsRequired();
            entity.Property(r => r.Endpoint).HasMaxLength(128).IsRequired();
            entity.Property(r => r.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(r => r.ResponseBody).IsRequired();

            // 这个唯一键就是幂等本身 / This unique index IS the idempotency
            // guarantee. Two concurrent retries both read nothing and both
            // proceed; the database refuses the second insert, and that refusal
            // is the only thing standing between the player and two accounts.
            // A check in application code cannot do this.
            entity.HasIndex(r => new { r.Key, r.Endpoint }).IsUnique();

            // For the sweep that deletes expired records.
            entity.HasIndex(r => r.ExpiresAt);
        });
    }
}
