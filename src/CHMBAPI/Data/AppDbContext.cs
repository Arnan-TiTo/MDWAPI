using CHMBAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace CHMBAPI.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Member Loyalty Tables
    public DbSet<Members_Mbw> Members_Mbw { get; set; }
    public DbSet<MemberIdentity> MemberIdentities { get; set; }
    public DbSet<MemberPlatformAccount> MemberPlatformAccounts { get; set; }
    public DbSet<MemberMappingRequest> MemberMappingRequests { get; set; }
    public DbSet<MemberMappingEvidence> MemberMappingEvidences { get; set; }
    public DbSet<LineOaConfig> LineOaConfigs { get; set; }
    public DbSet<LineMessageInboxEntry> LineMessageInbox { get; set; }
    public DbSet<OrderMemberLink> OrderMemberLinks { get; set; }

    // Points System
    public DbSet<PointAccount> PointAccounts { get; set; }
    public DbSet<PointLedgerEntry> PointLedgerEntries { get; set; }
    public DbSet<PointExpiration> PointExpirations { get; set; }
    public DbSet<PointAdjustment> PointAdjustments { get; set; }
    public DbSet<PointPolicy> PointPolicies { get; set; }

    // Rewards System
    public DbSet<RewardCatalog> RewardCatalogs { get; set; }
    public DbSet<RewardCode> RewardCodes { get; set; }
    public DbSet<MemberRedemption> MemberRedemptions { get; set; }
    public DbSet<RewardFulfillment> RewardFulfillments { get; set; }
    public DbSet<RewardRedemptionHistory> RewardRedemptionHistories { get; set; }

    // Tiers System
    public DbSet<TierMaster> TierMasters { get; set; }
    public DbSet<MemberTierHistory> MemberTierHistories { get; set; }

    // Masters
    public DbSet<MemberChannel> MemberChannels { get; set; }
    public DbSet<RegistrationProductOption> RegistrationProductOptions { get; set; }
    public DbSet<ThailandAddress> ThailandAddresses { get; set; }

    // Forms & Consent
    public DbSet<MemberRegistrationAnswer> MemberRegistrationAnswers { get; set; }
    public DbSet<MemberConsentLog> MemberConsentLogs { get; set; }

    // Notifications
    public DbSet<MemberNotification> MemberNotifications { get; set; }

    // Content
    public DbSet<ContentDocument> ContentDocuments { get; set; }

    // Audit
    public DbSet<AuditLog> AuditLogs { get; set; }

    // Users (for admin auth)
    public DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Members_Mbw>(entity =>
        {
            entity.ToTable("Members", "mbw");
            entity.HasKey(e => e.MemberId);
            entity.HasIndex(e => e.MemberCode).IsUnique();
            entity.HasIndex(e => e.Phone).HasFilter("[Phone] IS NOT NULL");
            entity.HasIndex(e => e.Email).HasFilter("[Email] IS NOT NULL");
            entity.HasIndex(e => e.Status);
            entity.Property(e => e.MemberCode).HasMaxLength(50).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(200);
            entity.Property(e => e.NamePrefix).HasMaxLength(50);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.Email).HasMaxLength(200);
            entity.Property(e => e.MemberType).HasMaxLength(50);
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.Gender).HasMaxLength(20);
            entity.Property(e => e.Address).HasMaxLength(500);
            entity.Property(e => e.Subdistrict).HasMaxLength(100);
            entity.Property(e => e.District).HasMaxLength(100);
            entity.Property(e => e.Province).HasMaxLength(100);
            entity.Property(e => e.ZipCode).HasMaxLength(20);
            entity.Property(e => e.MembershipTier).HasMaxLength(100);
            entity.Property(e => e.Tags).HasMaxLength(1000);
            entity.Property(e => e.Branch).HasMaxLength(100);
            entity.Property(e => e.PointsForTier).HasPrecision(18, 2);
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("Active");
            entity.Property(e => e.PreferredLanguage).HasMaxLength(10);
            entity.Property(e => e.PhoneCountryCode).HasMaxLength(10);
            entity.Property(e => e.HowYouKnowMe).HasMaxLength(1000);
            entity.Property(e => e.RegisteredAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasOne(e => e.CurrentTier).WithMany().HasForeignKey(e => e.CurrentTierId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.RegisterChannel).WithMany().HasForeignKey(e => e.RegisterChannelId).OnDelete(DeleteBehavior.SetNull);
            entity.HasMany(e => e.Identities).WithOne(i => i.Member).HasForeignKey(i => i.MemberId);
            entity.HasMany(e => e.PlatformAccounts).WithOne(pa => pa.Member).HasForeignKey(pa => pa.MemberId);
            entity.HasMany(e => e.MappingRequests).WithOne(r => r.Member).HasForeignKey(r => r.MemberId);
            entity.HasMany(e => e.RegistrationAnswers).WithOne().HasForeignKey(a => a.MemberId);
        });

        modelBuilder.Entity<MemberIdentity>(entity =>
        {
            entity.ToTable("MemberIdentities", "mbw");
            entity.HasKey(e => e.MemberIdentityId);
            entity.HasIndex(e => new { e.ProviderType, e.ProviderUserKey }).IsUnique();
            entity.HasIndex(e => e.MemberId);
            entity.Property(e => e.ProviderType).HasMaxLength(30).IsRequired();
            entity.Property(e => e.ProviderUserKey).HasMaxLength(200).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(200);
            entity.Property(e => e.PictureUrl).HasMaxLength(500);
            entity.Property(e => e.LinkedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<MemberPlatformAccount>(entity =>
        {
            entity.ToTable("MemberPlatformAccounts", "mbw");
            entity.HasKey(e => e.MemberPlatformAccountId);
            entity.HasIndex(e => new { e.PlatformType, e.ShopId, e.PlatformAccountKey }).IsUnique();
            entity.HasIndex(e => e.MemberId);
            entity.Property(e => e.PlatformType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.PlatformAccountKey).HasMaxLength(200).IsRequired();
            entity.Property(e => e.PlatformAccountName).HasMaxLength(200);
            entity.Property(e => e.VerifiedStatus).HasMaxLength(20).HasDefaultValue("Pending");
            entity.Property(e => e.VerifiedBy).HasMaxLength(100);
            entity.Property(e => e.LinkMethod).HasMaxLength(20).HasDefaultValue("MANUAL");
            entity.Property(e => e.ConfidenceScore).HasPrecision(5, 2);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<MemberMappingRequest>(entity =>
        {
            entity.ToTable("MemberMappingRequests", "mbw");
            entity.HasKey(e => e.RequestId);
            entity.HasIndex(e => e.MemberId);
            entity.HasIndex(e => new { e.RequestStatus, e.CreatedAt });
            entity.Property(e => e.PlatformType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.PlatformAccountKey).HasMaxLength(200).IsRequired();
            entity.Property(e => e.PlatformAccountName).HasMaxLength(200);
            entity.Property(e => e.SourceType).HasMaxLength(30).HasDefaultValue("ADMIN");
            entity.Property(e => e.RequestStatus).HasMaxLength(20).HasDefaultValue("Pending");
            entity.Property(e => e.ConfidenceScore).HasPrecision(5, 2);
            entity.Property(e => e.ReviewedBy).HasMaxLength(100);
            entity.Property(e => e.ReviewNote).HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasMany(e => e.Evidences).WithOne(e => e.Request).HasForeignKey(e => e.RequestId);
        });

        modelBuilder.Entity<MemberMappingEvidence>(entity =>
        {
            entity.ToTable("MemberMappingEvidence", "mbw");
            entity.HasKey(e => e.EvidenceId);
            entity.HasIndex(e => e.RequestId);
            entity.Property(e => e.EvidenceType).HasMaxLength(30).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<OrderMemberLink>(entity =>
        {
            entity.ToTable("OrderMemberLinks", "mbw");
            entity.HasKey(e => e.OrderMemberLinkId);
            entity.HasIndex(e => e.UnifiedOrderId);
            entity.HasIndex(e => e.MemberId);
            entity.Property(e => e.LinkMethod).HasMaxLength(30).IsRequired();
            entity.Property(e => e.LinkedBy).HasMaxLength(100);
            entity.Property(e => e.LinkedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<PointAccount>(entity =>
        {
            entity.ToTable("PointAccounts", "mbw");
            entity.HasKey(e => e.PointAccountId);
            entity.HasIndex(e => e.MemberId).IsUnique();
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasOne(e => e.Member)
                .WithOne(e => e.PointAccount)
                .HasForeignKey<PointAccount>(e => e.MemberId);
        });

        modelBuilder.Entity<PointLedgerEntry>(entity =>
        {
            entity.ToTable("PointLedger", "mbw");
            entity.HasKey(e => e.LedgerId);
            entity.HasIndex(e => new { e.MemberId, e.OccurredAt });
            entity.HasIndex(e => new { e.TxnType, e.OccurredAt });
            entity.HasIndex(e => new { e.RefType, e.RefId });
            entity.HasIndex(e => e.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
            entity.Property(e => e.TxnType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.RefType).HasMaxLength(30);
            entity.Property(e => e.RefId).HasMaxLength(100);
            entity.Property(e => e.CreatedBy).HasMaxLength(100);
            entity.Property(e => e.IdempotencyKey).HasMaxLength(200);
            entity.Property(e => e.OccurredAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<PointExpiration>(entity =>
        {
            entity.ToTable("PointExpirations", "mbw");
            entity.HasKey(e => e.ExpirationId);
            entity.HasIndex(e => new { e.MemberId, e.ExpiresAt });
            entity.HasIndex(e => new { e.Status, e.ExpiresAt });
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("Active");
        });

        modelBuilder.Entity<PointPolicy>(entity =>
        {
            entity.ToTable("PointPolicies", "mbw");
            entity.HasKey(e => e.PolicyId);
            entity.HasIndex(e => new { e.IsActive, e.EffectiveFrom, e.EffectiveTo });
            entity.Property(e => e.PolicyName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.PlatformType).HasMaxLength(20).HasDefaultValue("ALL");
            entity.Property(e => e.EarnFormula).HasMaxLength(50).HasDefaultValue("AMOUNT_DIV_100");
            entity.Property(e => e.EarnRate).HasPrecision(10, 4).HasDefaultValue(1.0m);
            entity.Property(e => e.MinOrderAmount).HasPrecision(12, 2);
            entity.Property(e => e.EligibleStatuses).HasMaxLength(500);
            entity.Property(e => e.CreatedBy).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<PointAdjustment>(entity =>
        {
            entity.ToTable("PointAdjustments", "mbw");
            entity.HasKey(e => e.AdjustmentId);
            entity.HasIndex(e => new { e.MemberId, e.CreatedAt });
            entity.Property(e => e.AdjustType).HasMaxLength(10).IsRequired();
            entity.Property(e => e.Reason).HasMaxLength(500).IsRequired();
            entity.Property(e => e.CreatedBy).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ApprovedBy).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<RewardCatalog>(entity =>
        {
            entity.ToTable("RewardCatalog", "mbw");
            entity.HasKey(e => e.RewardId);
            entity.HasIndex(e => new { e.IsActive, e.ValidFrom, e.ValidTo });
            entity.Property(e => e.RewardName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.PlatformType).HasMaxLength(20);
            entity.Property(e => e.RewardType).HasMaxLength(30).HasDefaultValue("DISCOUNT_CODE");
            entity.Property(e => e.ImageUrl).HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<RewardCode>(entity =>
        {
            entity.ToTable("RewardCodes", "mbw");
            entity.HasKey(e => e.RewardCodeId);
            entity.HasIndex(e => new { e.RewardId, e.Status });
            entity.HasIndex(e => e.Code);
            entity.Property(e => e.Code).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("Available");
            entity.HasOne(e => e.Reward)
                .WithMany(e => e.Codes)
                .HasForeignKey(e => e.RewardId);
        });

        modelBuilder.Entity<MemberRedemption>(entity =>
        {
            entity.ToTable("RewardRedemptions", "mbw");
            entity.HasKey(e => e.RedemptionId);
            entity.HasIndex(e => new { e.MemberId, e.CreatedAt });
            entity.HasIndex(e => new { e.Status, e.CreatedAt });
            entity.HasIndex(e => e.RedemptionCode).IsUnique();
            entity.Property(e => e.RedemptionCode).HasMaxLength(50).IsRequired();
            entity.Property(e => e.RewardNameSnapshot).HasMaxLength(200).IsRequired();
            entity.Property(e => e.RewardTypeSnapshot).HasMaxLength(30);
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("Reserved");
            entity.Property(e => e.CouponCode).HasMaxLength(200);
            entity.Property(e => e.ReservedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasOne(e => e.Reward)
                .WithMany(e => e.Redemptions)
                .HasForeignKey(e => e.RewardId);
        });

        modelBuilder.Entity<RewardFulfillment>(entity =>
        {
            entity.ToTable("RewardFulfillments", "mbw");
            entity.HasKey(e => e.FulfillmentId);
            entity.HasIndex(e => e.RedemptionId).IsUnique();
            entity.Property(e => e.FulfillmentType).HasMaxLength(30).IsRequired();
            entity.Property(e => e.FulfillmentStatus).HasMaxLength(30).IsRequired();
            entity.Property(e => e.RecipientName).HasMaxLength(200);
            entity.Property(e => e.Phone).HasMaxLength(30);
            entity.Property(e => e.AddressSnapshot).HasMaxLength(1000);
            entity.Property(e => e.CarrierName).HasMaxLength(100);
            entity.Property(e => e.TrackingNo).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasOne(e => e.Redemption)
                .WithOne(e => e.Fulfillment)
                .HasForeignKey<RewardFulfillment>(e => e.RedemptionId);
        });

        modelBuilder.Entity<RewardRedemptionHistory>(entity =>
        {
            entity.ToTable("RewardRedemptionHistories", "mbw");
            entity.HasKey(e => e.RedemptionHistoryId);
            entity.HasIndex(e => new { e.RedemptionId, e.ChangedAt });
            entity.Property(e => e.OldStatus).HasMaxLength(30);
            entity.Property(e => e.NewStatus).HasMaxLength(30).IsRequired();
            entity.Property(e => e.ChangedBy).HasMaxLength(100);
            entity.Property(e => e.Remark).HasMaxLength(500);
        });

        modelBuilder.Entity<LineOaConfig>(entity =>
        {
            entity.ToTable("LineOaConfigs", "mbw");
            entity.HasKey(e => e.LineOaConfigId);
            entity.HasIndex(e => e.CompanysId);
            entity.Property(e => e.LineOaName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.LoginChannelId).HasMaxLength(50);
            entity.Property(e => e.LoginChannelSecret).HasMaxLength(100);
            entity.Property(e => e.LoginCallbackUrl).HasMaxLength(500);
            entity.Property(e => e.MsgChannelSecret).HasMaxLength(100);
            entity.Property(e => e.MsgChannelToken).HasMaxLength(500).IsRequired();
            entity.Property(e => e.LiffId).HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<LineMessageInboxEntry>(entity =>
        {
            entity.ToTable("LineMessageInbox", "mbw");
            entity.HasKey(e => e.MessageEventId);
            entity.HasIndex(e => new { e.LineUserId, e.CreatedAt });
            entity.Property(e => e.LineUserId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.MessageType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ProcessStatus).HasMaxLength(20).HasDefaultValue("New");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasOne(e => e.Member)
                .WithMany()
                .HasForeignKey(e => e.MemberId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TierMaster>(entity =>
        {
            entity.ToTable("TierMasters", "mbw");
            entity.HasKey(e => e.TierId);
            entity.HasIndex(e => e.TierCode).IsUnique();
            entity.Property(e => e.TierCode).HasMaxLength(50).IsRequired();
            entity.Property(e => e.TierName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.MinPoints).HasPrecision(18, 2);
            entity.Property(e => e.MaxPoints).HasPrecision(18, 2);
            entity.Property(e => e.MinSpendAmount).HasPrecision(18, 2);
            entity.Property(e => e.MaxSpendAmount).HasPrecision(18, 2);
            entity.Property(e => e.TierColor).HasMaxLength(20);
            entity.Property(e => e.IconUrl).HasMaxLength(500);
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<MemberTierHistory>(entity =>
        {
            entity.ToTable("MemberTierHistories", "mbw");
            entity.HasKey(e => e.MemberTierHistoryId);
            entity.HasIndex(e => new { e.MemberId, e.CalculatedAt });
            entity.Property(e => e.TierPoints).HasPrecision(18, 2);
            entity.Property(e => e.SpendAmount).HasPrecision(18, 2);
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<MemberChannel>(entity =>
        {
            entity.ToTable("MemberChannels", "mbw");
            entity.HasKey(e => e.ChannelId);
            entity.HasIndex(e => e.ChannelCode).IsUnique();
            entity.Property(e => e.ChannelCode).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ChannelName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<RegistrationProductOption>(entity =>
        {
            entity.ToTable("RegistrationProductOptions", "mbw");
            entity.HasKey(e => e.OptionId);
            entity.HasIndex(e => e.OptionCode).IsUnique();
            entity.Property(e => e.OptionCode).HasMaxLength(50).IsRequired();
            entity.Property(e => e.OptionName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<MemberRegistrationAnswer>(entity =>
        {
            entity.ToTable("MemberRegistrationAnswers", "mbw");
            entity.HasKey(e => e.AnswerId);
            entity.HasIndex(e => new { e.MemberId, e.OptionId }).IsUnique();
            entity.Property(e => e.OtherText).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<MemberConsentLog>(entity =>
        {
            entity.ToTable("MemberConsentLogs", "mbw");
            entity.HasKey(e => e.ConsentLogId);
            entity.HasIndex(e => new { e.MemberId, e.AcceptedAt });
            entity.Property(e => e.AcceptedFromChannel).HasMaxLength(30).IsRequired();
            entity.Property(e => e.AcceptedIp).HasMaxLength(50);
            entity.Property(e => e.AcceptedUserAgent).HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<MemberNotification>(entity =>
        {
            entity.ToTable("MemberNotifications", "mbw");
            entity.HasKey(e => e.NotificationId);
            entity.HasIndex(e => new { e.MemberId, e.CreatedAt });
            entity.Property(e => e.NotificationType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Message).HasMaxLength(1000).IsRequired();
            entity.Property(e => e.RefType).HasMaxLength(50);
            entity.Property(e => e.RefId).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // User configuration (for admin auth)
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.Username).HasMaxLength(50).IsRequired();
            entity.Property(e => e.PasswordHash).HasMaxLength(200).IsRequired();
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.HasIndex(e => e.Username).IsUnique();
        });

        modelBuilder.Entity<ContentDocument>(entity =>
        {
            entity.ToTable("ContentDocuments", "mbw");
            entity.HasKey(e => e.DocumentId);
            entity.HasIndex(e => new { e.DocumentType, e.LanguageCode, e.IsActive });
            entity.Property(e => e.DocumentType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.VersionNo).HasMaxLength(30).IsRequired();
            entity.Property(e => e.LanguageCode).HasMaxLength(10).IsRequired();
            entity.Property(e => e.Title).HasMaxLength(300).IsRequired();
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // AuditLog configuration
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs", "mbw");
            entity.HasKey(e => e.AuditId);
            entity.Property(e => e.Action).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.PerformedBy).HasMaxLength(100);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
        });
    }
}
