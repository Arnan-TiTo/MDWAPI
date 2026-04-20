using CHMBAPI.Data;
using CHMBAPI.DTOs;
using CHMBAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace CHMBAPI.Services;

public class MemberMappingService
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public MemberMappingService(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

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

        if (dto.Evidences?.Any() == true)
        {
            foreach (var ev in dto.Evidences)
            {
                _db.MemberMappingEvidences.Add(new MemberMappingEvidence
                {
                    RequestId = request.RequestId,
                    EvidenceType = ev.EvidenceType,
                    EvidenceValue = ev.EvidenceValue,
                    CreatedAt = DateTime.UtcNow
                });
            }
            await _db.SaveChangesAsync();
        }

        if (!string.IsNullOrWhiteSpace(createdBy))
        {
            await _audit.LogAsync(
                "CREATE_MAPPING_REQUEST",
                $"Created mapping request {request.RequestId} for member {dto.MemberId}",
                createdBy,
                dto.MemberId);
        }

        return await GetRequestAsync(request.RequestId);
    }

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

        _db.MemberPlatformAccounts.Add(new MemberPlatformAccount
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
        });

        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            "APPROVE_MAPPING",
            $"Approved mapping request {requestId}",
            adminUsername,
            request.MemberId);

        return await GetRequestAsync(requestId);
    }

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

        await _audit.LogAsync(
            "REJECT_MAPPING",
            $"Rejected mapping request {requestId}",
            adminUsername,
            request.MemberId);

        return await GetRequestAsync(requestId);
    }

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
        var request = await _db.MemberMappingRequests
            .Include(x => x.Member)
            .Include(x => x.Evidences)
            .FirstOrDefaultAsync(x => x.RequestId == requestId)
            ?? throw new KeyNotFoundException($"Request {requestId} not found");

        return MapToDto(request);
    }

    public async Task<MappingRequestDto> AdminDirectLinkAsync(
        long memberId,
        string platformType,
        string platformAccountKey,
        string? platformAccountName,
        int? shopId,
        int adminUserId,
        string adminUsername)
    {
        _ = await _db.Members_Mbw.FindAsync(memberId)
            ?? throw new KeyNotFoundException($"Member {memberId} not found");

        var exists = await _db.MemberPlatformAccounts
            .AnyAsync(pa => pa.MemberId == memberId
                && pa.PlatformType == platformType.ToUpper()
                && pa.PlatformAccountKey == platformAccountKey);

        if (exists)
            throw new InvalidOperationException(
                $"Platform account {platformType}/{platformAccountKey} is already linked to this member");

        var request = new MemberMappingRequest
        {
            MemberId = memberId,
            PlatformType = platformType.ToUpper(),
            ShopId = shopId,
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

        _db.MemberPlatformAccounts.Add(new MemberPlatformAccount
        {
            MemberId = memberId,
            PlatformType = platformType.ToUpper(),
            ShopId = shopId,
            PlatformAccountKey = platformAccountKey,
            PlatformAccountName = platformAccountName,
            VerifiedStatus = "Verified",
            VerifiedAt = DateTime.UtcNow,
            VerifiedBy = adminUsername,
            LinkMethod = "MANUAL",
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            "ADMIN_DIRECT_LINK",
            $"Linked {platformType}/{platformAccountKey} directly for member {memberId}",
            adminUsername,
            memberId);

        return await GetRequestAsync(request.RequestId);
    }

    public async Task RemovePlatformLinkAsync(long memberId, long platformAccountId, int adminUserId, string adminUsername)
    {
        var account = await _db.MemberPlatformAccounts
            .FirstOrDefaultAsync(pa => pa.MemberPlatformAccountId == platformAccountId && pa.MemberId == memberId)
            ?? throw new KeyNotFoundException($"Platform account {platformAccountId} not found for member {memberId}");

        _db.MemberPlatformAccounts.Remove(account);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            "REMOVE_PLATFORM_LINK",
            $"Removed platform link {platformAccountId} for member {memberId}",
            adminUsername,
            memberId);
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
