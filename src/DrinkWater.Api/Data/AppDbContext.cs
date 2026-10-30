using DrinkWater.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<CupPreset> CupPresets => Set<CupPreset>();
    public DbSet<WaterLog> WaterLogs => Set<WaterLog>();
    public DbSet<DailySummary> DailySummaries => Set<DailySummary>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<SystemLog> SystemLogs => Set<SystemLog>();
    public DbSet<FriendNickname> FriendNicknames => Set<FriendNickname>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Era> Eras => Set<Era>();
    public DbSet<EraGroup> EraGroups => Set<EraGroup>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<HabitSetting> HabitSettings => Set<HabitSetting>();
    public DbSet<SleepLog> SleepLogs => Set<SleepLog>();
    public DbSet<HabitLog> HabitLogs => Set<HabitLog>();
    public DbSet<NeedReward> NeedRewards => Set<NeedReward>();
    public DbSet<UserCurrency> UserCurrencies => Set<UserCurrency>();
    public DbSet<EraLevel> EraLevels => Set<EraLevel>();
    public DbSet<UserEraProgress> UserEraProgresses => Set<UserEraProgress>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.FullName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Role).HasMaxLength(20).IsRequired();
            e.Property(x => x.PasswordHash).IsRequired();
            e.Property(x => x.RequirePhoto).HasDefaultValue(true);
            e.ToTable(t => t.HasCheckConstraint("CK_Users_Role", "\"Role\" IN ('User','Admin')"));
            e.HasOne<User>().WithMany().HasForeignKey(x => x.PartnerId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Follow>(e =>
        {
            e.HasKey(x => new { x.FollowerId, x.FollowingId });
            e.HasOne(x => x.Follower).WithMany(x => x.Following)
                .HasForeignKey(x => x.FollowerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Following).WithMany(x => x.Followers)
                .HasForeignKey(x => x.FollowingId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Follows_NotSelf", "\"FollowerId\" <> \"FollowingId\""));
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.ToTable(t => t.HasCheckConstraint("CK_Follows_Status", "\"Status\" IN ('Pending','Accepted')"));
        });

        b.Entity<CupPreset>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_CupPresets_Volume", "\"VolumeMl\" > 0"));
        });

        b.Entity<WaterLog>(e =>
        {
            e.Property(x => x.Period).HasMaxLength(20).IsRequired();
            e.Property(x => x.Note).HasMaxLength(255);
            e.HasIndex(x => new { x.UserId, x.LogTime });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_WaterLogs_Period", "\"Period\" IN ('Morning','Afternoon','Evening')"));
            e.ToTable(t => t.HasCheckConstraint("CK_WaterLogs_Amount",
                "\"AmountMl\" <> 0 AND \"AmountMl\" BETWEEN -20000 AND 20000"));
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.ToTable(t => t.HasCheckConstraint("CK_WaterLogs_Status",
                "\"Status\" IN ('Approved','Pending','Rejected','Unverified','Deducted')"));
            e.HasIndex(x => new { x.UserId, x.Status });
        });

        b.Entity<DailySummary>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Date }).IsUnique();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Notification>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(100).IsRequired();
            e.Property(x => x.Message).HasMaxLength(255).IsRequired();
            e.Property(x => x.Type).HasMaxLength(30).IsRequired();
            e.HasIndex(x => new { x.ReceiverId, x.IsRead, x.CreatedAt });
        });

        b.Entity<PushSubscription>(e =>
        {
            e.Property(x => x.Endpoint).HasMaxLength(500).IsRequired();
            e.Property(x => x.P256dh).HasMaxLength(200).IsRequired();
            e.Property(x => x.Auth).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Endpoint).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Message>(e =>
        {
            // Tin nhắn thoại không có chữ → Body rỗng hợp lệ.
            e.Property(x => x.Body).HasMaxLength(500).IsRequired();
            e.Property(x => x.AudioUrl).HasMaxLength(500);
            e.HasIndex(x => new { x.SenderId, x.ReceiverId, x.CreatedAt });
            e.HasIndex(x => new { x.ReceiverId, x.ReadAt });
        });

        b.Entity<SystemLog>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.Property(x => x.LogLevel).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.CreatedAt);
        });

        b.Entity<Era>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(80).IsRequired();
            e.Property(x => x.Subtitle).HasMaxLength(160);
            e.Property(x => x.BgFrom).HasMaxLength(20);
            e.Property(x => x.BgTo).HasMaxLength(20);
            e.HasIndex(x => x.Order);
            e.HasOne(x => x.Group).WithMany()
                .HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Levels).WithOne(x => x.Era)
                .HasForeignKey(x => x.EraId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<EraGroup>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(40).IsRequired();
            e.Property(x => x.Color).HasMaxLength(20);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.Order);
            e.HasOne(x => x.Currency).WithMany()
                .HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Currency>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(40).IsRequired();
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.Property(x => x.IconKey).HasMaxLength(30);
            e.Property(x => x.Color).HasMaxLength(20);
            e.HasIndex(x => x.Code).IsUnique();
        });

        b.Entity<NeedReward>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Kind, x.Date }).IsUnique();
            e.Property(x => x.Kind).HasMaxLength(32);
        });

        b.Entity<HabitLog>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(20).IsRequired();
            e.Property(x => x.Note).HasMaxLength(200);
            e.Property(x => x.PhotoUrl).HasMaxLength(300);
            e.Property(x => x.Status).HasMaxLength(12);
            e.HasIndex(x => new { x.UserId, x.Kind, x.Date });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SleepLog>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Date }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<HabitSetting>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(20).IsRequired();
            e.Property(x => x.Times).HasMaxLength(120);
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasIndex(x => new { x.UserId, x.Kind }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UserCurrency>(e =>
        {
            e.HasKey(x => new { x.UserId, x.CurrencyId });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Currency>().WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<EraLevel>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.Artifact).HasMaxLength(120);
            e.Property(x => x.ImageUrl).HasMaxLength(300);
            e.HasIndex(x => new { x.EraId, x.Level }).IsUnique();
        });

        b.Entity<UserEraProgress>(e =>
        {
            e.HasKey(x => new { x.UserId, x.EraId });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Era>().WithMany().HasForeignKey(x => x.EraId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<FriendNickname>(e =>
        {
            e.HasKey(x => new { x.OwnerId, x.TargetId });
            e.Property(x => x.Nickname).HasMaxLength(50).IsRequired();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.TargetId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

