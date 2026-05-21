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
    public DbSet<UnifiedOrderLabel> UnifiedOrderLabels => Set<UnifiedOrderLabel>();
    public DbSet<UnifiedOrderLabelDocument> UnifiedOrderLabelDocuments => Set<UnifiedOrderLabelDocument>();
    public DbSet<UnifiedReturns> UnifiedReturns => Set<UnifiedReturns>();

    // --views ADW-- 
    public DbSet<VwOrderMerged> VwOrderMerged { get; set; } = default!;
    public DbSet<VwOrderMergedItem> VwOrderMergedItems { get; set; } = default!;

    // ── mbw: Member Loyalty System ──
    public DbSet<Member> Members_Mbw => Set<Member>();
    public DbSet<MemberIdentity> MemberIdentities => Set<MemberIdentity>();
    public DbSet<MemberPlatformAccount> MemberPlatformAccounts => Set<MemberPlatformAccount>();
    public DbSet<MemberMappingRequest> MemberMappingRequests => Set<MemberMappingRequest>();
    public DbSet<MemberMappingEvidence> MemberMappingEvidence => Set<MemberMappingEvidence>();
    public DbSet<FormSubmission> FormSubmissions => Set<FormSubmission>();
    public DbSet<OrderMemberLink> OrderMemberLinks => Set<OrderMemberLink>();
    public DbSet<OrderClaim> OrderClaims => Set<OrderClaim>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public DbSet<PointPolicy> PointPolicies => Set<PointPolicy>();
    public DbSet<PointAccount> PointAccounts => Set<PointAccount>();
    public DbSet<PointLedgerEntry> PointLedger => Set<PointLedgerEntry>();
    public DbSet<PointExpiration> PointExpirations => Set<PointExpiration>();
    public DbSet<PointAdjustment> PointAdjustments => Set<PointAdjustment>();
    public DbSet<RewardCatalog> RewardCatalog => Set<RewardCatalog>();
    public DbSet<RewardCode> RewardCodes => Set<RewardCode>();
    public DbSet<RewardRedemption> RewardRedemptions => Set<RewardRedemption>();
    public DbSet<EarnFormula> EarnFormulas => Set<EarnFormula>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<LineMessageInboxEntry> LineMessageInbox => Set<LineMessageInboxEntry>();
    public DbSet<AdminRole> AdminRoles => Set<AdminRole>();
    public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();
    public DbSet<WebhookInboxEntry> WebhookInbox => Set<WebhookInboxEntry>();
    public DbSet<ApiCallLog> ApiCallLogs => Set<ApiCallLog>();
    public DbSet<LineOaConfig> LineOaConfigs => Set<LineOaConfig>();
    public DbSet<MemberChannel> MemberChannels => Set<MemberChannel>();
    public DbSet<RegistrationProductOption> RegistrationProductOptions => Set<RegistrationProductOption>();
    public DbSet<MemberRegistrationAnswer> MemberRegistrationAnswers => Set<MemberRegistrationAnswer>();
    public DbSet<ContentDocument> ContentDocuments => Set<ContentDocument>();
    public DbSet<MemberConsentLog> MemberConsentLogs => Set<MemberConsentLog>();
    public DbSet<TierMaster> TierMasters => Set<TierMaster>();
    public DbSet<MemberTierHistory> MemberTierHistories => Set<MemberTierHistory>();
    public DbSet<MemberNotification> MemberNotifications => Set<MemberNotification>();
    public DbSet<RewardFulfillment> RewardFulfillments => Set<RewardFulfillment>();
    public DbSet<RewardRedemptionHistory> RewardRedemptionHistories => Set<RewardRedemptionHistory>();
    public DbSet<ThailandAddress> ThailandAddresses => Set<ThailandAddress>();

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

        modelBuilder.Entity<UnifiedOrderLabel>(e =>
        {
            e.ToTable("UnifiedOrderLabels", "mdw");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Channel, x.OrderExternalNo });
            e.Property(x => x.CreatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<UnifiedOrderLabelDocument>(e =>
        {
            e.ToTable("UnifiedOrderLabelDocument", "mdw");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.OrderSn);
            e.Property(x => x.FetchedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.BillingWeight).HasPrecision(18, 4);
            e.Property(x => x.CodAmount).HasPrecision(18, 2);
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


        // ═══════════════════════════════════════════
        // mbw — Member Loyalty System
        // ═══════════════════════════════════════════

        // 1. Members
        modelBuilder.Entity<Member>(e =>
        {
            e.ToTable("Members", "mbw");
            e.HasKey(x => x.MemberId);
            e.HasIndex(x => x.MemberCode).IsUnique();
            e.HasIndex(x => x.Phone).HasFilter("[Phone] IS NOT NULL");
            e.HasIndex(x => x.Email).HasFilter("[Email] IS NOT NULL");
            e.HasIndex(x => x.Status);
            e.Property(x => x.Status).HasDefaultValue("Active");
            e.Property(x => x.RegisteredAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 2. MemberIdentities
        modelBuilder.Entity<MemberIdentity>(e =>
        {
            e.ToTable("MemberIdentities", "mbw");
            e.HasKey(x => x.MemberIdentityId);
            e.HasIndex(x => new { x.ProviderType, x.ProviderUserKey }).IsUnique();
            e.HasIndex(x => x.MemberId);
            e.Property(x => x.LinkedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.IsActive).HasDefaultValue(true);
        });

        // 3. MemberPlatformAccounts
        modelBuilder.Entity<MemberPlatformAccount>(e =>
        {
            e.ToTable("MemberPlatformAccounts", "mbw");
            e.HasKey(x => x.MemberPlatformAccountId);
            e.HasIndex(x => new { x.PlatformType, x.ShopId, x.PlatformAccountKey }).IsUnique();
            e.HasIndex(x => x.MemberId);
            e.Property(x => x.VerifiedStatus).HasDefaultValue("Pending");
            e.Property(x => x.LinkMethod).HasDefaultValue("MANUAL");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.ConfidenceScore).HasPrecision(5, 4);
            // cross-schema FK → mdw.Shops
            e.HasOne(x => x.Shop)
             .WithMany()
             .HasForeignKey(x => x.ShopId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // 4. MemberMappingRequests
        modelBuilder.Entity<MemberMappingRequest>(e =>
        {
            e.ToTable("MemberMappingRequests", "mbw");
            e.HasKey(x => x.RequestId);
            e.HasIndex(x => x.MemberId);
            e.HasIndex(x => new { x.RequestStatus, x.CreatedAt });
            e.Property(x => x.SourceType).HasDefaultValue("ADMIN");
            e.Property(x => x.RequestStatus).HasDefaultValue("Pending");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.ConfidenceScore).HasPrecision(5, 4);
            e.HasMany(x => x.Evidences).WithOne(x => x.Request).HasForeignKey(x => x.RequestId);
        });

        // 5. MemberMappingEvidence
        modelBuilder.Entity<MemberMappingEvidence>(e =>
        {
            e.ToTable("MemberMappingEvidence", "mbw");
            e.HasKey(x => x.EvidenceId);
            e.HasIndex(x => x.RequestId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 6. FormSubmissions
        modelBuilder.Entity<FormSubmission>(e =>
        {
            e.ToTable("FormSubmissions", "mbw");
            e.HasKey(x => x.SubmissionId);
            e.HasIndex(x => x.MemberId);
            e.HasIndex(x => new { x.ProcessStatus, x.CreatedAt });
            e.Property(x => x.ProcessStatus).HasDefaultValue("Pending");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 7. OrderMemberLinks
        modelBuilder.Entity<OrderMemberLink>(e =>
        {
            e.ToTable("OrderMemberLinks", "mbw");
            e.HasKey(x => x.OrderMemberLinkId);
            e.HasIndex(x => x.UnifiedOrderId);
            e.HasIndex(x => x.MemberId);
            e.Property(x => x.LinkedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            // cross-schema FK → mdw.UnifiedOrders
            e.HasOne(x => x.UnifiedOrder)
             .WithMany()
             .HasForeignKey(x => x.UnifiedOrderId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // 8. OrderClaims
        modelBuilder.Entity<OrderClaim>(e =>
        {
            e.ToTable("OrderClaims", "mbw");
            e.HasKey(x => x.ClaimId);
            e.HasIndex(x => x.MemberId);
            e.HasIndex(x => x.UnifiedOrderId);
            e.HasIndex(x => new { x.ClaimStatus, x.CreatedAt });
            e.Property(x => x.ClaimStatus).HasDefaultValue("Pending");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.UnifiedOrder)
             .WithMany()
             .HasForeignKey(x => x.UnifiedOrderId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // 9. OrderStatusHistory
        modelBuilder.Entity<OrderStatusHistory>(e =>
        {
            e.ToTable("OrderStatusHistory", "mbw");
            e.HasKey(x => x.StatusHistoryId);
            e.HasIndex(x => x.UnifiedOrderId);
            e.HasIndex(x => x.ChangedAt);
            e.Property(x => x.Source).HasDefaultValue("SYNC");
            e.Property(x => x.ChangedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.UnifiedOrder)
             .WithMany()
             .HasForeignKey(x => x.UnifiedOrderId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // 10. PointPolicies
        modelBuilder.Entity<PointPolicy>(e =>
        {
            e.ToTable("PointPolicies", "mbw");
            e.HasKey(x => x.PolicyId);
            e.HasIndex(x => new { x.IsActive, x.EffectiveFrom, x.EffectiveTo });
            e.Property(x => x.PlatformType).HasDefaultValue("ALL");
            e.Property(x => x.EarnFormula).HasDefaultValue("AMOUNT_DIV_100");
            e.Property(x => x.EarnRate).HasPrecision(10, 4).HasDefaultValue(1.0m);
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 11. PointAccounts
        modelBuilder.Entity<PointAccount>(e =>
        {
            e.ToTable("PointAccounts", "mbw");
            e.HasKey(x => x.PointAccountId);
            e.HasIndex(x => x.MemberId).IsUnique();
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.Member)
             .WithOne(x => x.PointAccount)
             .HasForeignKey<PointAccount>(x => x.MemberId);
        });

        // 12. PointLedger
        modelBuilder.Entity<PointLedgerEntry>(e =>
        {
            e.ToTable("PointLedger", "mbw");
            e.HasKey(x => x.LedgerId);
            e.HasIndex(x => new { x.MemberId, x.OccurredAt });
            e.HasIndex(x => new { x.TxnType, x.OccurredAt });
            e.HasIndex(x => new { x.RefType, x.RefId });
            e.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
            e.Property(x => x.OccurredAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 13. PointExpirations
        modelBuilder.Entity<PointExpiration>(e =>
        {
            e.ToTable("PointExpirations", "mbw");
            e.HasKey(x => x.ExpirationId);
            e.HasIndex(x => new { x.MemberId, x.ExpiresAt });
            e.HasIndex(x => new { x.Status, x.ExpiresAt });
            e.Property(x => x.Status).HasDefaultValue("Active");
        });

        // 14. PointAdjustments
        modelBuilder.Entity<PointAdjustment>(e =>
        {
            e.ToTable("PointAdjustments", "mbw");
            e.HasKey(x => x.AdjustmentId);
            e.HasIndex(x => new { x.MemberId, x.CreatedAt });
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 15. RewardCatalog
        modelBuilder.Entity<RewardCatalog>(e =>
        {
            e.ToTable("RewardCatalog", "mbw");
            e.HasKey(x => x.RewardId);
            e.HasIndex(x => new { x.IsActive, x.ValidFrom, x.ValidTo });
            e.Property(x => x.RewardType).HasDefaultValue("DISCOUNT_CODE");
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 16. RewardCodes
        modelBuilder.Entity<RewardCode>(e =>
        {
            e.ToTable("RewardCodes", "mbw");
            e.HasKey(x => x.RewardCodeId);
            e.HasIndex(x => new { x.RewardId, x.Status });
            e.HasIndex(x => x.Code);
            e.Property(x => x.Status).HasDefaultValue("Available");
            e.HasOne(x => x.Reward)
             .WithMany(x => x.Codes)
             .HasForeignKey(x => x.RewardId);
            // ไม่มี inverse navigation ฝั่ง RewardRedemption เพื่อป้องกัน EF Core สับสน
            e.HasOne(x => x.Redemption)
             .WithMany()
             .HasForeignKey(x => x.RedemptionId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // 17. RewardRedemptions
        modelBuilder.Entity<RewardRedemption>(e =>
        {
            e.ToTable("RewardRedemptions", "mbw");
            e.HasKey(x => x.RedemptionId);
            e.HasIndex(x => new { x.MemberId, x.CreatedAt });
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.Property(x => x.Status).HasDefaultValue("Reserved");
            e.Property(x => x.ReservedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.Reward)
             .WithMany(x => x.Redemptions)
             .HasForeignKey(x => x.RewardId);
        });

        // 18. OutboxMessages
        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("OutboxMessages", "mbw");
            e.HasKey(x => x.OutboxId);
            e.HasIndex(x => x.MemberId);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.Property(x => x.Channel).HasDefaultValue("LINE");
            e.Property(x => x.Status).HasDefaultValue("Pending");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 19. LineMessageInbox
        modelBuilder.Entity<LineMessageInboxEntry>(e =>
        {
            e.ToTable("LineMessageInbox", "mbw");
            e.HasKey(x => x.MessageEventId);
            e.HasIndex(x => new { x.LineUserId, x.CreatedAt });
            e.HasIndex(x => new { x.ProcessStatus, x.CreatedAt });
            e.Property(x => x.ProcessStatus).HasDefaultValue("New");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 20. AdminRoles
        modelBuilder.Entity<AdminRole>(e =>
        {
            e.ToTable("AdminRoles", "mbw");
            e.HasKey(x => x.RoleId);
            e.HasIndex(x => x.RoleName).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 21. AdminAuditLogs
        modelBuilder.Entity<AdminAuditLog>(e =>
        {
            e.ToTable("AdminAuditLogs", "mbw");
            e.HasKey(x => x.AuditId);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => new { x.ActionType, x.CreatedAt });
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            // cross-schema FK → dbo.Users
            e.HasOne(x => x.User)
             .WithMany()
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // 22. WebhookInbox
        modelBuilder.Entity<WebhookInboxEntry>(e =>
        {
            e.ToTable("WebhookInbox", "mbw");
            e.HasKey(x => x.InboxId);
            e.HasIndex(x => x.EventKey).IsUnique();
            e.HasIndex(x => new { x.Source, x.EventType, x.CreatedAt });
            e.HasIndex(x => new { x.ProcessStatus, x.CreatedAt });
            e.Property(x => x.ProcessStatus).HasDefaultValue("New");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 23. ApiCallLogs
        modelBuilder.Entity<ApiCallLog>(e =>
        {
            e.ToTable("ApiCallLogs", "mbw");
            e.HasKey(x => x.ApiLogId);
            e.HasIndex(x => new { x.ApiName, x.CreatedAt });
            e.HasIndex(x => x.RequestRef).HasFilter("[RequestRef] IS NOT NULL");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 24. EarnFormulas
        modelBuilder.Entity<EarnFormula>(e =>
        {
            e.ToTable("EarnFormulas", "mbw");
            e.HasKey(x => x.FormulaId);
            e.HasIndex(x => x.FormulaCode).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 25. LineOaConfigs
        modelBuilder.Entity<LineOaConfig>(e =>
        {
            e.ToTable("LineOaConfigs", "mbw");
            e.HasKey(x => x.LineOaConfigId);
            e.HasIndex(x => x.CompanysId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 26. MemberChannels
        modelBuilder.Entity<MemberChannel>(e =>
        {
            e.ToTable("MemberChannels", "mbw");
            e.HasKey(x => x.ChannelId);
            e.HasIndex(x => x.ChannelCode).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 27. RegistrationProductOptions
        modelBuilder.Entity<RegistrationProductOption>(e =>
        {
            e.ToTable("RegistrationProductOptions", "mbw");
            e.HasKey(x => x.OptionId);
            e.HasIndex(x => x.OptionCode).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 28. MemberRegistrationAnswers
        modelBuilder.Entity<MemberRegistrationAnswer>(e =>
        {
            e.ToTable("MemberRegistrationAnswers", "mbw");
            e.HasKey(x => x.AnswerId);
            e.HasIndex(x => new { x.MemberId, x.OptionId }).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 29. ContentDocuments
        modelBuilder.Entity<ContentDocument>(e =>
        {
            e.ToTable("ContentDocuments", "mbw");
            e.HasKey(x => x.DocumentId);
            e.HasIndex(x => new { x.DocumentType, x.VersionNo, x.LanguageCode }).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 30. MemberConsentLogs
        modelBuilder.Entity<MemberConsentLog>(e =>
        {
            e.ToTable("MemberConsentLogs", "mbw");
            e.HasKey(x => x.ConsentLogId);
            e.HasIndex(x => new { x.MemberId, x.AcceptedAt });
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 31. TierMasters
        modelBuilder.Entity<TierMaster>(e =>
        {
            e.ToTable("TierMasters", "mbw");
            e.HasKey(x => x.TierId);
            e.HasIndex(x => x.TierCode).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 32. MemberTierHistories
        modelBuilder.Entity<MemberTierHistory>(e =>
        {
            e.ToTable("MemberTierHistories", "mbw");
            e.HasKey(x => x.MemberTierHistoryId);
            e.HasIndex(x => new { x.MemberId, x.CalculatedAt });
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 33. MemberNotifications
        modelBuilder.Entity<MemberNotification>(e =>
        {
            e.ToTable("MemberNotifications", "mbw");
            e.HasKey(x => x.NotificationId);
            e.HasIndex(x => new { x.MemberId, x.CreatedAt });
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 34. RewardFulfillments
        modelBuilder.Entity<RewardFulfillment>(e =>
        {
            e.ToTable("RewardFulfillments", "mbw");
            e.HasKey(x => x.FulfillmentId);
            e.HasIndex(x => x.RedemptionId).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // 35. RewardRedemptionHistories
        modelBuilder.Entity<RewardRedemptionHistory>(e =>
        {
            e.ToTable("RewardRedemptionHistories", "mbw");
            e.HasKey(x => x.RedemptionHistoryId);
            e.HasIndex(x => new { x.RedemptionId, x.ChangedAt });
        });

        // 36. ThailandAddress
        modelBuilder.Entity<ThailandAddress>(e =>
        {
            e.ToTable("ThailandAddress", "mbw");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.province);
            e.HasIndex(x => x.district);
            e.HasIndex(x => x.tambonID);
        });

        // UnifiedOrderAddresses — Lat/Long ใช้ precision พิเศษ
        modelBuilder.Entity<UnifiedOrderAddresses>(e =>
        {
            e.Property(x => x.Latitude).HasPrecision(9, 6);
            e.Property(x => x.Longitude).HasPrecision(9, 6);
        });

        // ── Global decimal precision default (18,2) สำหรับทุก decimal ที่ยังไม่ได้กำหนด ──
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties()
                .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            {
                if (property.GetPrecision() == null)
                {
                    property.SetPrecision(18);
                    property.SetScale(2);
                }
            }
        }
    }

}
