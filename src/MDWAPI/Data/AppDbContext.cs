using MDWAPI.Entities;
using MDWAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ChannelToken> ChannelTokens { get; set; }
    public DbSet<User> Users => Set<User>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<Misc> Misc => Set<Misc>();
    public DbSet<JobLog> JobLogs => Set<JobLog>();
    public DbSet<MkpToken> MkpTokens => Set<MkpToken>();
    public DbSet<Shops> Shops => Set<Shops>();
    public DbSet<Partners> Partners => Set<Partners>();
    public DbSet<ShopeeOrder> ShopeeOrders => Set<ShopeeOrder>();
    public DbSet<ShopeeOrderItem> ShopeeOrderItems => Set<ShopeeOrderItem>();

    //UnifiedOrders
    public DbSet<UnifiedRawOrders> UnifiedRawOrders => Set<UnifiedRawOrders>();
    public DbSet<UnifiedOrderTrans> UnifiedOrderTrans => Set<UnifiedOrderTrans>();
    public DbSet<UnifiedOrderTransItem> UnifiedOrderTransItems => Set<UnifiedOrderTransItem>();
    public DbSet<UnifiedOrders> UnifiedOrders => Set<UnifiedOrders>();
    public DbSet<UnifiedOrderItems> UnifiedOrderItems => Set<UnifiedOrderItems>();
    public DbSet<UnifiedOrderPayments> UnifiedOrderPayments => Set<UnifiedOrderPayments>();
    public DbSet<UnifiedOrderShipments> UnifiedOrderShipments => Set<UnifiedOrderShipments>();
    public DbSet<UnifiedOrderAddresses> UnifiedOrderAddresses => Set<UnifiedOrderAddresses>();
    public DbSet<VUnifiedOrder> VUnifiedOrders => Set<VUnifiedOrder>();

    // --views ADW-- 
    public DbSet<VwOrderMerged> VwOrderMerged { get; set; } = default!;
    public DbSet<VwOrderMergedItem> VwOrderMergedItems { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // dbo
        // ----- User -----
        modelBuilder.HasDefaultSchema("dbo");

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasIndex(x => x.Username).IsUnique();
        });

        modelBuilder.Entity<UserToken>(e =>
        {
            e.ToTable("UserTokens");
            e.HasIndex(x => new { x.Token }).IsUnique();
            e.HasIndex(x => x.ExpiresAt);
        });

        modelBuilder.Entity<Misc>(e =>
        {
            e.ToTable("Misc", "dbo");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Type).HasMaxLength(100).IsRequired();
            e.Property(x => x.Value1).HasMaxLength(200);
            e.Property(x => x.Value2).HasMaxLength(200);
            e.Property(x => x.Value3).HasMaxLength(200);
            e.Property(x => x.Value4).HasMaxLength(200);
            e.Property(x => x.Value5).HasMaxLength(200);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.CreatedAt).IsRequired(false);
            e.Property(x => x.UpdatedAt).IsRequired(false);
        });


        modelBuilder.Entity<JobLog>(e =>
        {
            e.ToTable("JobLogs", "dbo");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.RunId);
            e.HasIndex(x => new { x.Category, x.CreatedAtUtc });
            e.Property(x => x.Category).HasMaxLength(50).IsRequired();
            e.Property(x => x.JobName).HasMaxLength(200);
            e.Property(x => x.Phase).HasMaxLength(20).IsRequired();
            e.Property(x => x.Step).HasMaxLength(100);
            e.Property(x => x.Level).HasMaxLength(10).IsRequired();
            e.Property(x => x.Message).HasMaxLength(4000).IsRequired();
            e.Property(x => x.MetaJson).HasMaxLength(4000);
            e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        });


        // mdw 
        // ChannelTokens
        modelBuilder.Entity<ChannelToken>()
            .ToTable("ChannelTokens", "mdw", t => t.ExcludeFromMigrations())
            .HasKey(t => t.Id);

        // ----- Partner → ตาราง mdw.Partners -----
        modelBuilder.Entity<Partners>(e =>
        {
            e.ToTable("Partners", schema: "mdw");

            // PK
            e.HasKey(x => x.Id);

            // คอลัมน์/ความยาว
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.PartnerKey).HasMaxLength(200).IsRequired();
            e.Property(x => x.Environment).HasMaxLength(50);

            // UNIQUE (PartnerId)
            e.HasIndex(x => x.PartnerId).IsUnique();
        });

        // ----- Shop → ตาราง mdw.Shops -----
        modelBuilder.Entity<Shops>(e =>
        {
            e.ToTable("Shops", schema: "mdw");

            // PK
            e.HasKey(x => x.Id);

            // คอลัมน์/ความยาว
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Country).HasMaxLength(10);
            e.Property(x => x.Password).HasMaxLength(100);

            // UNIQUE (ShopId)
            e.HasIndex(x => x.ShopId).IsUnique();

            // ความสัมพันธ์: Shop (หลาย) → Partner (หนึ่ง)
            e.HasOne(x => x.Partners)
             .WithMany(p => p.Shops)
             .HasForeignKey(x => x.PartnerId)           // FK: Shops.PartnerId → Partners.Id
             .OnDelete(DeleteBehavior.Restrict);        // ป้องกันการลบ Partner ลบ Shop ด้วย
        });

        modelBuilder.Entity<UnifiedRawOrders>(e =>
        {
            e.ToTable("UnifiedRawOrders", "mdw");
            e.HasIndex(x => new { x.Channel, x.ExternalOrderId });
            e.Property(x => x.PulledAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<UnifiedOrders>(e =>
        {
            e.ToTable("UnifiedOrders", "mdw");
            e.HasIndex(x => new { x.Channel, x.ExternalOrderId }).IsUnique();
            e.Property(x => x.IngestedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasMany(x => x.Items).WithOne(x => x.Order).HasForeignKey(x => x.UnifiedOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Payments).WithOne(x => x.Order).HasForeignKey(x => x.UnifiedOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Shipments).WithOne(x => x.Order).HasForeignKey(x => x.UnifiedOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UnifiedOrderTrans>(e =>
        {
            e.ToTable("UnifiedOrderTrans", "mdw");
            e.HasKey(x => x.TransId);
            e.Property(x => x.Platform).HasMaxLength(20).IsRequired();
            e.Property(x => x.Mode).HasMaxLength(20).IsRequired();
            e.Property(x => x.SellerId).HasMaxLength(100);
            e.Property(x => x.BatchNo).HasMaxLength(100);
            e.Property(x => x.Env).HasMaxLength(50);
            e.Property(x => x.TimeRangeField).HasMaxLength(50);
            e.Property(x => x.Notes).HasMaxLength(1000);
        });

        modelBuilder.Entity<UnifiedOrderTransItem>(e =>
        {
            e.ToTable("UnifiedOrderTransItems", "mdw");
            e.HasKey(x => x.ItemId);
            e.Property(x => x.OrderRef).HasMaxLength(100);
            e.Property(x => x.ExternalOrderId).HasMaxLength(100);
            e.Property(x => x.Result).HasMaxLength(20).IsRequired();
            e.HasOne(x => x.Trans)
                .WithMany(h => h.Items)
                .HasForeignKey(x => x.TransId);
        });

        // UnifiedOrders - Configure for trigger compatibility
        modelBuilder.Entity<UnifiedOrders>(e =>
        {
            e.ToTable("UnifiedOrders", "mdw", tb => tb.HasTrigger("TR_UnifiedOrders_Update"));
        });

        modelBuilder.Entity<VUnifiedOrder>(e =>
        {
            e.HasNoKey(); // keyless (view)
            e.ToView("v_UnifiedOrders", "mdw");
        });

        // --views ADW--
        modelBuilder.Entity<VwOrderMerged>()
          .HasNoKey()
          .ToView("vw_OrderMerged", schema: "adw");

        modelBuilder.Entity<VwOrderMergedItem>()
          .HasNoKey()
          .ToView("vw_OrderMergedItems", schema: "adw");
    }

}
