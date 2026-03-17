namespace MDWAPI.DTOs;

// ─── Member Registration ──────────────────────────
public class MemberRegisterRequest
{
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool ConsentAccepted { get; set; }

    // LINE identity (ถ้าสมัครผ่าน LINE)
    public string? LineProviderType { get; set; }   // LINE_LOGIN / LINE_OA
    public string? LineUserId { get; set; }
    public string? LinePictureUrl { get; set; }
}

public class MemberProfileDto
{
    public long MemberId { get; set; }
    public string MemberCode { get; set; } = default!;
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
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
    public string? Phone { get; set; }
    public string? Email { get; set; }
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
    public int ReservedPoints { get; set; }
    public int TotalEarned { get; set; }
    public int TotalBurned { get; set; }
    public int TotalExpired { get; set; }
    public DateTime? LastActivityAt { get; set; }
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
    public string Status { get; set; } = default!;
    public int PointsSpent { get; set; }
    public string? Code { get; set; }
    public string? Message { get; set; }
}
