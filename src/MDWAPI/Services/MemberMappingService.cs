using MDWAPI.Data;
using MDWAPI.DTOs;
using MDWAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Services;

public class MemberMappingService
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public MemberMappingService(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>สร้างคำขอ mapping ใหม่</summary>
    public async Task<MappingRequestDto> CreateRequestAsync(MappingRequestCreateDto dto, string? createdBy = null)
    {
        var request = new MemberMappingRequest
        {
            MemberId = dto.MemberId,
            PlatformType = dto.PlatformType,
            ShopId = dto.ShopId,
            PlatformAccountKey = dto.PlatformAccountKey,
            PlatformAccountName = dto.PlatformAccountName,
            SourceType = dto.SourceType,
            RequestStatus = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        _db.MemberMappingRequests.Add(request);
        await _db.SaveChangesAsync();

        // เพิ่มหลักฐาน
        if (dto.Evidences?.Any() == true)
        {
            foreach (var ev in dto.Evidences)
            {
                _db.MemberMappingEvidence.Add(new MemberMappingEvidence
                {
                    RequestId = request.RequestId,
                    EvidenceType = ev.EvidenceType,
                    EvidenceValue = ev.EvidenceValue,
                    CreatedAt = DateTime.UtcNow
                });
            }
            await _db.SaveChangesAsync();
        }

        return await GetRequestAsync(request.RequestId);
    }

    /// <summary>อนุมัติ mapping → สร้าง MemberPlatformAccount</summary>
    public async Task<MappingRequestDto> ApproveAsync(long requestId, MappingApprovalDto dto, int adminUserId, string adminUsername)
    {
        var request = await _db.MemberMappingRequests
            .FirstOrDefaultAsync(r => r.RequestId == requestId)
            ?? throw new KeyNotFoundException($"Request {requestId} not found");

        if (request.RequestStatus != "Pending")
            throw new InvalidOperationException($"Request is already {request.RequestStatus}");

        request.RequestStatus = "Approved";
        request.ReviewedBy = adminUsername;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewNote = dto.ReviewNote;

        // สร้าง MemberPlatformAccount
        var account = new MemberPlatformAccount
        {
            MemberId = request.MemberId,
            PlatformType = request.PlatformType,
            ShopId = request.ShopId,
            PlatformAccountKey = request.PlatformAccountKey,
            PlatformAccountName = request.PlatformAccountName,
            VerifiedStatus = "Verified",
            VerifiedAt = DateTime.UtcNow,
            VerifiedBy = adminUsername,
            LinkMethod = request.SourceType == "ADMIN" ? "MANUAL" : "FORM",
            CreatedAt = DateTime.UtcNow
        };

        _db.MemberPlatformAccounts.Add(account);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(adminUserId, "APPROVE_MAPPING", "MemberMappingRequests", requestId.ToString());

        return await GetRequestAsync(requestId);
    }

    /// <summary>ปฏิเสธ mapping</summary>
    public async Task<MappingRequestDto> RejectAsync(long requestId, MappingApprovalDto dto, int adminUserId, string adminUsername)
    {
        var request = await _db.MemberMappingRequests
            .FirstOrDefaultAsync(r => r.RequestId == requestId)
            ?? throw new KeyNotFoundException($"Request {requestId} not found");

        if (request.RequestStatus != "Pending")
            throw new InvalidOperationException($"Request is already {request.RequestStatus}");

        request.RequestStatus = "Rejected";
        request.ReviewedBy = adminUsername;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewNote = dto.ReviewNote;

        await _db.SaveChangesAsync();
        await _audit.LogAsync(adminUserId, "REJECT_MAPPING", "MemberMappingRequests", requestId.ToString());

        return await GetRequestAsync(requestId);
    }

    /// <summary>ดู mapping requests (admin list - pending only)</summary>
    public async Task<List<MappingRequestDto>> ListPendingAsync(int page = 1, int pageSize = 20)
    {
        return await _db.MemberMappingRequests
            .Include(r => r.Member)
            .Include(r => r.Evidences)
            .Where(r => r.RequestStatus == "Pending")
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => MapToDto(r))
            .ToListAsync();
    }

    /// <summary>ดู mapping requests ทั้งหมด (filter by status optional)</summary>
    public async Task<List<MappingRequestDto>> ListAllAsync(string? status = null, int page = 1, int pageSize = 20)
    {
        var q = _db.MemberMappingRequests
            .Include(r => r.Member)
            .Include(r => r.Evidences)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(r => r.RequestStatus == status);

        return await q
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => MapToDto(r))
            .ToListAsync();
    }

    public async Task<MappingRequestDto> GetRequestAsync(long requestId)
    {
        var r = await _db.MemberMappingRequests
            .Include(x => x.Member)
            .Include(x => x.Evidences)
            .FirstOrDefaultAsync(x => x.RequestId == requestId)
            ?? throw new KeyNotFoundException($"Request {requestId} not found");

        return MapToDto(r);
    }

    /// <summary>Admin ผูก platform account โดยตรง (สร้าง request + auto-approve ในขั้นตอนเดียว)</summary>
    public async Task<MappingRequestDto> AdminDirectLinkAsync(
        long memberId, string platformType, string platformAccountKey,
        string? platformAccountName, long? shopId,
        int adminUserId, string adminUsername)
    {
        // ตรวจว่า member มีจริง
        var member = await _db.Members_Mbw.FindAsync(memberId)
            ?? throw new KeyNotFoundException($"Member {memberId} not found");

        // ตรวจว่าซ้ำหรือไม่
        var exists = await _db.MemberPlatformAccounts
            .AnyAsync(pa => pa.MemberId == memberId
                && pa.PlatformType == platformType.ToUpper()
                && pa.PlatformAccountKey == platformAccountKey);
        if (exists)
            throw new InvalidOperationException($"Platform account {platformType}/{platformAccountKey} is already linked to this member");

        // สร้าง mapping request
        var request = new MemberMappingRequest
        {
            MemberId = memberId,
            PlatformType = platformType.ToUpper(),
            ShopId = (int?)shopId,
            PlatformAccountKey = platformAccountKey,
            PlatformAccountName = platformAccountName,
            SourceType = "ADMIN",
            RequestStatus = "Approved",
            ReviewedBy = adminUsername,
            ReviewedAt = DateTime.UtcNow,
            ReviewNote = "Admin direct link",
            CreatedAt = DateTime.UtcNow
        };
        _db.MemberMappingRequests.Add(request);

        // สร้าง platform account ทันที
        var account = new MemberPlatformAccount
        {
            MemberId = memberId,
            PlatformType = platformType.ToUpper(),
            ShopId = (int?)shopId,
            PlatformAccountKey = platformAccountKey,
            PlatformAccountName = platformAccountName,
            VerifiedStatus = "Verified",
            VerifiedAt = DateTime.UtcNow,
            VerifiedBy = adminUsername,
            LinkMethod = "MANUAL",
            CreatedAt = DateTime.UtcNow
        };
        _db.MemberPlatformAccounts.Add(account);

        await _db.SaveChangesAsync();
        await _audit.LogAsync(adminUserId, "ADMIN_DIRECT_LINK", "MemberPlatformAccounts", account.MemberPlatformAccountId.ToString());

        return await GetRequestAsync(request.RequestId);
    }

    /// <summary>Admin ลบ platform link</summary>
    public async Task RemovePlatformLinkAsync(long memberId, long platformAccountId, int adminUserId, string adminUsername)
    {
        var account = await _db.MemberPlatformAccounts
            .FirstOrDefaultAsync(pa => pa.MemberPlatformAccountId == platformAccountId && pa.MemberId == memberId)
            ?? throw new KeyNotFoundException($"Platform account {platformAccountId} not found for member {memberId}");

        _db.MemberPlatformAccounts.Remove(account);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(adminUserId, "REMOVE_PLATFORM_LINK", "MemberPlatformAccounts", platformAccountId.ToString());
    }

    private static MappingRequestDto MapToDto(MemberMappingRequest r) => new()
    {
        RequestId = r.RequestId,
        MemberId = r.MemberId,
        MemberDisplayName = r.Member?.DisplayName,
        PlatformType = r.PlatformType,
        ShopId = r.ShopId,
        PlatformAccountKey = r.PlatformAccountKey,
        PlatformAccountName = r.PlatformAccountName,
        SourceType = r.SourceType,
        RequestStatus = r.RequestStatus,
        ConfidenceScore = r.ConfidenceScore,
        ReviewedBy = r.ReviewedBy,
        ReviewedAt = r.ReviewedAt,
        ReviewNote = r.ReviewNote,
        CreatedAt = r.CreatedAt,
        Evidences = r.Evidences?.Select(e => new MappingEvidenceDto
        {
            EvidenceType = e.EvidenceType,
            EvidenceValue = e.EvidenceValue
        }).ToList() ?? new()
    };
}
