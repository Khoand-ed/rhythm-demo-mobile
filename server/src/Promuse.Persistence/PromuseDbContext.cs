using Microsoft.EntityFrameworkCore;
using Promuse.Contracts.Missions;
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

    public DbSet<ShopOffer> ShopOffers => Set<ShopOffer>();

    public DbSet<Purchase> Purchases => Set<Purchase>();

    public DbSet<Run> Runs => Set<Run>();

    public DbSet<Stage> Stages => Set<Stage>();

    public DbSet<MissionDefinition> MissionDefinitions => Set<MissionDefinition>();

    public DbSet<MissionRewardDefinition> MissionRewardDefinitions => Set<MissionRewardDefinition>();

    public DbSet<MissionCounter> MissionCounters => Set<MissionCounter>();

    public DbSet<MissionClaim> MissionClaims => Set<MissionClaim>();

    public DbSet<MissionRewardClaim> MissionRewardClaims => Set<MissionRewardClaim>();

    /// <summary>
    /// Readable, stable ids for the seeded catalogue - obviously seed data at a
    /// glance in psql, and identical on every machine.
    /// </summary>
    private static ShopOffer Offer(int n, (int Id, int Amount) sell, (int Id, int Amount) price) => new()
    {
        Id = new Guid($"11111111-0000-0000-0000-{n:D12}"),
        SellItemId = sell.Id,
        SellAmount = sell.Amount,
        PriceItemId = price.Id,
        PriceAmount = price.Amount,
        IsActive = true,
        SortOrder = n,
    };

    private static MissionDefinition Mission(
        MissionTab tab, string id, string description, MissionGoal goal, int target, int points, int order) =>
        new()
        {
            Tab = tab, MissionId = id, Description = description,
            Goal = goal, Target = target, Points = points, SortOrder = order, IsActive = true,
        };

    private static MissionRewardDefinition Reward(
        MissionTab tab, string id, int requiredPoints, int itemId, int amount, int order) =>
        new()
        {
            Tab = tab, RewardId = id, RequiredPoints = requiredPoints,
            ItemId = itemId, Amount = amount, SortOrder = order, IsActive = true,
        };

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

        // 定义是内容, 进度是玩家的 / Definitions are content and are seeded; the
        // three tables after them are per player and start empty.

        modelBuilder.Entity<MissionDefinition>(entity =>
        {
            entity.ToTable("mission_definitions");
            entity.HasKey(m => new { m.Tab, m.MissionId });
            entity.Property(m => m.MissionId).HasMaxLength(64);
            entity.Property(m => m.Description).HasMaxLength(200).IsRequired();

            // 枚举存字符串 / Stored as text rather than an ordinal. An ordinal
            // silently remaps every stored row when someone reorders the enum,
            // and these values also appear in the period keys and in the API.
            entity.Property(m => m.Tab).HasConversion<string>().HasMaxLength(16);
            entity.Property(m => m.Goal).HasConversion<string>().HasMaxLength(32);

            entity.ToTable(t => t.HasCheckConstraint(
                "ck_mission_definitions_target_positive", "target > 0 AND points > 0"));

            entity.HasData(
                Mission(MissionTab.Daily, "daily.play1", "Clear any song 1 time(s)", MissionGoal.PlaySong, 1, 1, 1),
                Mission(MissionTab.Daily, "daily.play2", "Clear any song 2 time(s)", MissionGoal.PlaySong, 2, 2, 2),
                Mission(MissionTab.Daily, "daily.buy1", "Purchase any item from the Store 1 time(s)", MissionGoal.BuyShopItem, 1, 3, 3),
                Mission(MissionTab.Weekly, "weekly.play5", "Clear any song 5 time(s)", MissionGoal.PlaySong, 5, 2, 1),
                Mission(MissionTab.Weekly, "weekly.play10", "Clear any song 10 time(s)", MissionGoal.PlaySong, 10, 3, 2),
                Mission(MissionTab.Weekly, "weekly.buy3", "Purchase any item from the Store 3 time(s)", MissionGoal.BuyShopItem, 3, 5, 3));
        });

        modelBuilder.Entity<MissionRewardDefinition>(entity =>
        {
            entity.ToTable("mission_reward_definitions");
            entity.HasKey(r => new { r.Tab, r.RewardId });
            entity.Property(r => r.RewardId).HasMaxLength(64);
            entity.Property(r => r.Tab).HasConversion<string>().HasMaxLength(16);

            entity.ToTable(t => t.HasCheckConstraint(
                "ck_mission_rewards_positive", "required_points > 0 AND amount > 0"));

            entity.HasData(
                Reward(MissionTab.Daily, "daily.r1", 1, 2, 500, 1),
                Reward(MissionTab.Daily, "daily.r2", 2, 1, 100, 2),
                Reward(MissionTab.Daily, "daily.r3", 3, 6, 3, 3),
                Reward(MissionTab.Weekly, "weekly.r1", 3, 2, 2000, 1),
                Reward(MissionTab.Weekly, "weekly.r2", 6, 1, 300, 2),
                Reward(MissionTab.Weekly, "weekly.r3", 10, 7, 5, 3));
        });

        modelBuilder.Entity<MissionCounter>(entity =>
        {
            entity.ToTable("mission_counters");

            // 周期键在主键里 / The period key is part of the identity, which is
            // what makes the reset implicit: a new day is a new key, a new key
            // has no row, and no row reads as zero. No scheduled job to wipe
            // anything, and therefore no scheduled job that can fail to run.
            entity.HasKey(c => new { c.AccountId, c.Tab, c.Goal, c.PeriodKey });

            entity.Property(c => c.PeriodKey).HasMaxLength(16);
            entity.Property(c => c.Tab).HasConversion<string>().HasMaxLength(16);
            entity.Property(c => c.Goal).HasConversion<string>().HasMaxLength(32);

            entity.HasOne(c => c.Player).WithMany()
                  .HasForeignKey(c => c.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MissionClaim>(entity =>
        {
            entity.ToTable("mission_claims");

            // 存在即已领 / The row existing is the claim, so claiming twice is a
            // primary key violation rather than a check the service has to
            // remember - and a concurrent double-claim is refused by the database.
            entity.HasKey(c => new { c.AccountId, c.Tab, c.MissionId, c.PeriodKey });

            entity.Property(c => c.MissionId).HasMaxLength(64);
            entity.Property(c => c.PeriodKey).HasMaxLength(16);
            entity.Property(c => c.Tab).HasConversion<string>().HasMaxLength(16);

            entity.HasOne(c => c.Player).WithMany()
                  .HasForeignKey(c => c.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MissionRewardClaim>(entity =>
        {
            entity.ToTable("mission_reward_claims");
            entity.HasKey(c => new { c.AccountId, c.Tab, c.RewardId, c.PeriodKey });

            entity.Property(c => c.RewardId).HasMaxLength(64);
            entity.Property(c => c.PeriodKey).HasMaxLength(16);
            entity.Property(c => c.Tab).HasConversion<string>().HasMaxLength(16);

            entity.HasOne(c => c.Player).WithMany()
                  .HasForeignKey(c => c.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Stage>(entity =>
        {
            entity.ToTable("stages");
            entity.HasKey(st => st.StageId);
            entity.Property(st => st.StageId).HasMaxLength(64);

            entity.ToTable(t => t.HasCheckConstraint(
                "ck_stages_cost_not_negative", "stamina_cost >= 0"));

            // 项目里真实存在的三张谱 / The three charts that actually exist in
            // Assets/Beatmaps. The costs are new numbers - nothing in the project
            // ever set one - chosen so a full level-1 bar of 82 is roughly ten
            // attempts, and the hard chart costs double.
            entity.HasData(
                new Stage { StageId = "stage_001", StaminaCost = 6 },
                new Stage { StageId = "stage_AIW", StaminaCost = 6 },
                new Stage { StageId = "stage_AIW_hard", StaminaCost = 12 });
        });

        modelBuilder.Entity<ShopOffer>(entity =>
        {
            entity.ToTable("shop_offers");
            entity.HasKey(o => o.Id);
            entity.HasIndex(o => o.SortOrder);

            entity.ToTable(t => t.HasCheckConstraint(
                "ck_shop_offers_amounts_positive", "sell_amount > 0 AND price_amount > 0"));

            // 和客户端的 ShopItemDataList 一致 / The same six offers the client
            // currently reads out of ShopItemDataList.asset, seeded so the shop
            // looks identical the moment it starts being served from here.
            //
            // 固定 GUID / Fixed ids rather than generated ones, because HasData
            // goes into the migration: a new Guid on every scaffold would produce
            // a migration that deletes and recreates the catalogue each time.
            entity.HasData(
                Offer(1, sell: (10, 1), price: (6, 240)),
                Offer(2, sell: (2, 4000), price: (6, 10)),
                Offer(3, sell: (11, 1), price: (6, 10)),
                Offer(4, sell: (1, 100), price: (6, 40)),
                Offer(5, sell: (12, 1), price: (6, 8)),
                Offer(6, sell: (13, 1), price: (6, 12)));
        });

        modelBuilder.Entity<Purchase>(entity =>
        {
            entity.ToTable("purchases");
            entity.HasKey(p => p.Id);

            // The ledger is read "what did this player buy, most recent first".
            entity.HasIndex(p => new { p.AccountId, p.CreatedAt });

            entity.ToTable(t => t.HasCheckConstraint(
                "ck_purchases_quantity_positive", "quantity > 0"));

            entity.HasOne(p => p.Player)
                  .WithMany()
                  .HasForeignKey(p => p.AccountId)
                  .OnDelete(DeleteBehavior.Cascade);

            // 不是 Cascade / Restrict, not Cascade: retiring an offer must not
            // delete the record of what people paid for it. IsActive is how an
            // offer leaves the shop.
            entity.HasOne(p => p.Offer)
                  .WithMany()
                  .HasForeignKey(p => p.ShopOfferId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Run>(entity =>
        {
            entity.ToTable("runs");
            entity.HasKey(r => r.Id);

            entity.Property(r => r.StageId).HasMaxLength(64).IsRequired();
            entity.HasIndex(r => new { r.AccountId, r.StartedAt });

            entity.HasOne(r => r.Player)
                  .WithMany()
                  .HasForeignKey(r => r.AccountId)
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
