namespace MDWAPI.DTOs;

// ─── Member Registration ──────────────────────────
public class MemberRegisterRequest
{
    public string? DisplayName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTime? BirthDate { get; set; }
    public int? Age { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool ConsentAccepted { get; set; }
    public int? RegisterChannelId { get; set; }
    public List<int>? ProductOptionIds { get; set; }
    public string? OtherProductText { get; set; }
    public string? PreferredLanguage { get; set; }
    public string? PhoneCountryCode { get; set; }

    // LINE identity (ถ้าสมัครผ่าน LINE)
    public string? LineProviderType { get; set; }   // LINE_LOGIN / LINE_OA
    public string? LineUserId { get; set; }
    public string? LinePictureUrl { get; set; }
    public string? LiffId { get; set; }

    // Company association (resolved from LIFF ID → LineOaConfig)
    public int? CompanysId { get; set; }
}

public class MemberImportDto
{
    public string? LineUserId { get; set; }
    public string? MemberType { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateTime? BirthDate { get; set; }
    public int? Age { get; set; }
    public string? Gender { get; set; }
    public string? Address { get; set; }
    public string? Subdistrict { get; set; }
    public string? District { get; set; }
    public string? Province { get; set; }
    public string? ZipCode { get; set; }
    public string? MembershipTier { get; set; }
    public string? Tags { get; set; }
    public string? Branch { get; set; }
    public decimal CurrentPoints { get; set; }
    public decimal TotalPoints { get; set; }
    public decimal PointsForTier { get; set; }
    public int UsageCount { get; set; }
    public DateTime? LastActiveAt { get; set; }
    public int? LastActiveDays { get; set; }
    public string? Status { get; set; }
    public DateTime? RegisteredAt { get; set; }
    public string? HowYouKnowMe { get; set; }

    // Legacy support for basic Excel
    public string? PictureUrl { get; set; }
    public string? StatusMessage { get; set; }
    public string? Language { get; set; }
    public DateTime? AddedAt { get; set; }
}

public class BulkImportResultDto
{
    public int Total { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class ImportValidationRowDto
{
    public int RowNumber { get; set; }
    public string? Status { get; set; } // Ready, Duplicate, Invalid
    public string? Message { get; set; }
    public MemberImportDto Data { get; set; } = default!;
}

public class BulkImportValidateResultDto
{
    public string FileName { get; set; } = "";
    public int TotalRows { get; set; }
    public int ReadyCount { get; set; }
    public int DuplicateCount { get; set; }
    public int ErrorCount { get; set; }
    public List<ImportValidationRowDto> Rows { get; set; } = new();
}

public class MemberProfileDto
{
    public long MemberId { get; set; }
    public string MemberCode { get; set; } = default!;
    public string? DisplayName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Gender { get; set; }
    public DateTime? BirthDate { get; set; }
    public int? Age { get; set; }
    
    public string? Address { get; set; }
    public string? Subdistrict { get; set; }
    public string? District { get; set; }
    public string? Province { get; set; }
    public string? ZipCode { get; set; }

    public string? MemberType { get; set; }
    public int? CurrentTierId { get; set; }
    public string? MembershipTier { get; set; }
    public string? TierColor { get; set; }
    public string? TierIconUrl { get; set; }
    public string? Tags { get; set; }
    public string? Branch { get; set; }
    public decimal PointsForTier { get; set; }
    public int UsageCount { get; set; }
    public DateTime? LastActiveAt { get; set; }
    public string? HowYouKnowMe { get; set; }
    public string? PreferredLanguage { get; set; }
    public string? PhoneCountryCode { get; set; }

    public string Status { get; set; } = default!;
    public DateTime RegisteredAt { get; set; }
    public List<MemberIdentityDto> Identities { get; set; } = new();
    public List<MemberPlatformAccountDto> PlatformAccounts { get; set; } = new();
    public PointBalanceDto? PointBalance { get; set; }
}

public class MemberIdentityDto
{
    public long MemberIdentityId { get; set; }
    public string ProviderType { get; set; } = default!;
    public string ProviderUserKey { get; set; } = default!;
    public string? DisplayName { get; set; }
    public string? PictureUrl { get; set; }
    public bool IsActive { get; set; }
}

public class MemberPlatformAccountDto
{
    public long MemberPlatformAccountId { get; set; }
    public string PlatformType { get; set; } = default!;
    public int? ShopId { get; set; }
    public string PlatformAccountKey { get; set; } = default!;
    public string? PlatformAccountName { get; set; }
    public string VerifiedStatus { get; set; } = default!;
    public string LinkMethod { get; set; } = default!;
    public bool IsPrimary { get; set; }
}

public class MemberSummaryDto
{
    public long MemberId { get; set; }
    public string MemberCode { get; set; } = default!;
    public string? DisplayName { get; set; }
    public string Status { get; set; } = default!;
    public int PlatformAccountCount { get; set; }
    public int AvailablePoints { get; set; }
    public DateTime RegisteredAt { get; set; }
}

public class MemberSummaryWithStatsDto
{
    public long MemberId { get; set; }
    public string MemberCode { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public string Status { get; set; } = "";
    public DateTime RegisteredAt { get; set; }
    public int PlatformAccountCount { get; set; }
    public string? PlatformTypes { get; set; }
    public int AvailablePoints { get; set; }
    public int TotalEarned { get; set; }
    public int TotalBurned { get; set; }
    public int ReservedPoints { get; set; }
    public int LinkedOrderCount { get; set; }
}

public class MemberUpdateProfileRequest
{
    public string? DisplayName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? Address { get; set; }
    public string? Subdistrict { get; set; }
    public string? District { get; set; }
    public string? Province { get; set; }
    public string? ZipCode { get; set; }
    public string? Remark { get; set; }
    public string? PreferredLanguage { get; set; }
}

public class MemberPlatformLinkRequest
{
    public string PlatformType { get; set; } = default!;   // SHOPEE / LAZADA / TIKTOK
    public string PlatformAccountKey { get; set; } = default!;  // username / buyer_id
    public string? PlatformAccountName { get; set; }  // ชื่อร้านหรือชื่อบัญชี
}

public class PlatformRequestStatusDto
{
    public long RequestId { get; set; }
    public string PlatformType { get; set; } = default!;
    public string PlatformAccountKey { get; set; } = default!;
    public string? PlatformAccountName { get; set; }
    public string RequestStatus { get; set; } = default!;
    public string? ReviewNote { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ─── Mapping ──────────────────────────────────────
public class MappingRequestCreateDto
{
    public long MemberId { get; set; }
    public string PlatformType { get; set; } = default!;
    public int? ShopId { get; set; }
    public string PlatformAccountKey { get; set; } = default!;
    public string? PlatformAccountName { get; set; }
    public string SourceType { get; set; } = "ADMIN";
    public List<MappingEvidenceDto>? Evidences { get; set; }
}

public class MappingEvidenceDto
{
    public string EvidenceType { get; set; } = default!;
    public string? EvidenceValue { get; set; }
}

public class MappingApprovalDto
{
    public string? ReviewNote { get; set; }
}

public class MappingRequestDto
{
    public long RequestId { get; set; }
    public long MemberId { get; set; }
    public string? MemberDisplayName { get; set; }
    public string PlatformType { get; set; } = default!;
    public int? ShopId { get; set; }
    public string PlatformAccountKey { get; set; } = default!;
    public string? PlatformAccountName { get; set; }
    public string SourceType { get; set; } = default!;
    public string RequestStatus { get; set; } = default!;
    public decimal? ConfidenceScore { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<MappingEvidenceDto> Evidences { get; set; } = new();
}

// ─── Points ───────────────────────────────────────
public class PointBalanceDto
{
    public int AvailablePoints { get; set; }
    public int PendingPoints { get; set; }
    public int TotalPoints => AvailablePoints + PendingPoints;
    public int ReservedPoints { get; set; }
    public int TotalEarned { get; set; }
    public int TotalBurned { get; set; }
    public int TotalExpired { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public int ExpiringPoints { get; set; }       // แต้มที่จะหมดอายุใน 30 วัน
    public DateTime? NextExpiryDate { get; set; }  // วันหมดอายุใกล้สุด
}

public class PointExpirationDto
{
    public long ExpirationId { get; set; }
    public int OriginalPoints { get; set; }
    public int RemainingPoints { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class PointHistoryDto
{
    public long LedgerId { get; set; }
    public string TxnType { get; set; } = default!;
    public int Points { get; set; }
    public int BalanceAfter { get; set; }
    public string? RefType { get; set; }
    public string? RefId { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? CreatedBy { get; set; }
}

public class PointAdjustRequest
{
    public long MemberId { get; set; }
    public string AdjustType { get; set; } = default!; // ADD / DEDUCT
    public int Points { get; set; }
    public string Reason { get; set; } = default!;
}

// ─── Rewards ──────────────────────────────────────
public class RewardListItemDto
{
    public int RewardId { get; set; }
    public string RewardName { get; set; } = default!;
    public string? Description { get; set; }
    public string? PlatformType { get; set; }
    public string RewardType { get; set; } = default!;
    public int PointsCost { get; set; }
    public int StockRemaining { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public bool IsActive { get; set; }
}

public class RedeemRequestDto
{
    public long MemberId { get; set; }
    public int RewardId { get; set; }
}

public class RedemptionResultDto
{
    public long RedemptionId { get; set; }
    public string RedemptionCode { get; set; } = default!;
    public string Status { get; set; } = default!;
    public int PointsSpent { get; set; }
    public string? Code { get; set; }
    public string? Message { get; set; }
    public RedemptionFulfillmentDto? Fulfillment { get; set; }
}

public class RedemptionFulfillmentDto
{
    public string FulfillmentStatus { get; set; } = default!;
    public string? CarrierName { get; set; }
    public string? TrackingNo { get; set; }
    public DateTime? ShippedAt { get; set; }
}

// ─── Masters & Content ────────────────────────────
public class MemberChannelDto
{
    public int ChannelId { get; set; }
    public string ChannelCode { get; set; } = default!;
    public string ChannelName { get; set; } = default!;
}

public class RegistrationProductOptionDto
{
    public int OptionId { get; set; }
    public string OptionCode { get; set; } = default!;
    public string OptionName { get; set; } = default!;
    public bool IsAllowOtherText { get; set; }
}

public class ContentDocumentDto
{
    public string DocumentType { get; set; } = default!;
    public string VersionNo { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string ContentHtml { get; set; } = default!;
}

public class TierMasterDto
{
    public int TierId { get; set; }
    public string TierCode { get; set; } = default!;
    public string TierName { get; set; } = default!;
    public decimal MinPoints { get; set; }
    public decimal? MaxPoints { get; set; }
    public decimal MinSpendAmount { get; set; }
    public decimal? MaxSpendAmount { get; set; }
    public string? TierColor { get; set; }
    public string? IconUrl { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
}

public class MemberNotificationDto
{
    public long NotificationId { get; set; }
    public string NotificationType { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string Message { get; set; } = default!;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
