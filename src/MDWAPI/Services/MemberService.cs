using MDWAPI.Data;
using MDWAPI.DTOs;
using MDWAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Services;

public class MemberService
{
    private readonly AppDbContext _db;

    public MemberService(AppDbContext db) => _db = db;

    /// <summary>สมัครสมาชิกใหม่</summary>
    public async Task<MemberProfileDto> RegisterAsync(MemberRegisterRequest req)
    {
        // generate code
        var seq = await _db.Members_Mbw.CountAsync() + 1;
        var code = $"MBW-{seq:D6}";

        var member = new Member
        {
            MemberCode = code,
            DisplayName = req.DisplayName,
            Phone = req.Phone,
            Email = req.Email,
            Status = "Active",
            ConsentAccepted = req.ConsentAccepted,
            ConsentedAt = req.ConsentAccepted ? DateTime.UtcNow : null,
            RegisteredAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _db.Members_Mbw.Add(member);
        await _db.SaveChangesAsync();

        // สร้าง PointAccount
        _db.PointAccounts.Add(new PointAccount
        {
            MemberId = member.MemberId,
            UpdatedAt = DateTime.UtcNow
        });

        // สร้าง LINE identity ถ้ามี
        if (!string.IsNullOrEmpty(req.LineUserId))
        {
            _db.MemberIdentities.Add(new MemberIdentity
            {
                MemberId = member.MemberId,
                ProviderType = req.LineProviderType ?? "LINE_OA",
                ProviderUserKey = req.LineUserId,
                DisplayName = req.DisplayName,
                PictureUrl = req.LinePictureUrl,
                LinkedAt = DateTime.UtcNow,
                IsActive = true
            });
        }

        await _db.SaveChangesAsync();

        return await GetProfileAsync(member.MemberId);
    }

    /// <summary>ดู profile สมาชิก</summary>
    public async Task<MemberProfileDto> GetProfileAsync(long memberId)
    {
        var m = await _db.Members_Mbw
            .Include(x => x.Identities)
            .Include(x => x.PlatformAccounts)
            .Include(x => x.PointAccount)
            .FirstOrDefaultAsync(x => x.MemberId == memberId)
            ?? throw new KeyNotFoundException($"Member {memberId} not found");

        return MapToProfile(m);
    }

    /// <summary>ค้นหา member จาก LINE userId</summary>
    public async Task<MemberProfileDto?> GetByLineUserIdAsync(string lineUserId)
    {
        var identity = await _db.MemberIdentities
            .Include(x => x.Member).ThenInclude(m => m.Identities)
            .Include(x => x.Member).ThenInclude(m => m.PlatformAccounts)
            .Include(x => x.Member).ThenInclude(m => m.PointAccount)
            .FirstOrDefaultAsync(x => x.ProviderUserKey == lineUserId && x.IsActive);

        return identity == null ? null : MapToProfile(identity.Member);
    }

    /// <summary>ค้นหา members สำหรับ admin</summary>
    public async Task<List<MemberSummaryDto>> SearchAsync(string? keyword, int page = 1, int pageSize = 20)
    {
        var q = _db.Members_Mbw.AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            q = q.Where(m =>
                m.MemberCode.Contains(keyword) ||
                (m.DisplayName != null && m.DisplayName.Contains(keyword)) ||
                (m.Phone != null && m.Phone.Contains(keyword)) ||
                (m.Email != null && m.Email.Contains(keyword)));
        }

        return await q
            .OrderByDescending(m => m.RegisteredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MemberSummaryDto
            {
                MemberId = m.MemberId,
                MemberCode = m.MemberCode,
                DisplayName = m.DisplayName,
                Status = m.Status,
                PlatformAccountCount = m.PlatformAccounts.Count,
                AvailablePoints = m.PointAccount != null ? m.PointAccount.AvailablePoints : 0,
                RegisteredAt = m.RegisteredAt
            })
            .ToListAsync();
    }

    /// <summary>สรุป member พร้อม earn stats (orders linked, total earned, total burned)</summary>
    public async Task<List<MemberSummaryWithStatsDto>> GetMemberSummaryWithStatsAsync(
        string? keyword = null, int page = 1, int pageSize = 50)
    {
        var q = _db.Members_Mbw.AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            q = q.Where(m =>
                m.MemberCode.Contains(keyword) ||
                (m.DisplayName != null && m.DisplayName.Contains(keyword)) ||
                (m.Phone != null && m.Phone.Contains(keyword)) ||
                (m.Email != null && m.Email.Contains(keyword)) ||
                m.PlatformAccounts.Any(pa => pa.PlatformAccountKey.Contains(keyword)));
        }

        return await q
            .OrderByDescending(m => m.PointAccount != null ? m.PointAccount.TotalEarned : 0)
            .ThenByDescending(m => m.RegisteredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MemberSummaryWithStatsDto
            {
                MemberId = m.MemberId,
                MemberCode = m.MemberCode,
                DisplayName = m.DisplayName,
                Phone = m.Phone,
                Status = m.Status,
                RegisteredAt = m.RegisteredAt,
                PlatformAccountCount = m.PlatformAccounts.Count,
                PlatformTypes = string.Join(", ", m.PlatformAccounts.Select(pa => pa.PlatformType).Distinct()),
                AvailablePoints = m.PointAccount != null ? m.PointAccount.AvailablePoints : 0,
                TotalEarned = m.PointAccount != null ? m.PointAccount.TotalEarned : 0,
                TotalBurned = m.PointAccount != null ? m.PointAccount.TotalBurned : 0,
                ReservedPoints = m.PointAccount != null ? m.PointAccount.ReservedPoints : 0,
                LinkedOrderCount = _db.OrderMemberLinks.Count(l => l.MemberId == m.MemberId)
            })
            .ToListAsync();
    }

    /// <summary>อัปเดตข้อมูลส่วนตัวของสมาชิก</summary>
    public async Task<MemberProfileDto> UpdateProfileAsync(long memberId, MemberUpdateProfileRequest req)
    {
        var member = await _db.Members_Mbw.FindAsync(memberId)
            ?? throw new KeyNotFoundException($"Member {memberId} not found");

        if (req.DisplayName != null) member.DisplayName = req.DisplayName;
        if (req.Phone != null) member.Phone = req.Phone;
        if (req.Email != null) member.Email = req.Email;
        member.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return await GetProfileAsync(memberId);
    }

    /// <summary>member ส่ง request ผูก platform account</summary>
    public async Task<PlatformRequestStatusDto> SubmitPlatformLinkAsync(long memberId, MemberPlatformLinkRequest req)
    {
        // ตรวจสอบว่า member มีอยู่
        var member = await _db.Members_Mbw.FindAsync(memberId)
            ?? throw new KeyNotFoundException($"Member {memberId} not found");

        // ตรวจสอบว่ามี request ที่ pending อยู่แล้วหรือไม่
        var existing = await _db.MemberMappingRequests
            .AnyAsync(r => r.MemberId == memberId
                && r.PlatformType == req.PlatformType
                && r.PlatformAccountKey == req.PlatformAccountKey
                && r.RequestStatus == "Pending");

        if (existing)
            throw new InvalidOperationException("คุณมี request สำหรับบัญชีนี้ที่รอดำเนินการอยู่แล้ว");

        // ตรวจสอบว่าผูกไว้แล้วหรือไม่
        var alreadyLinked = await _db.MemberPlatformAccounts
            .AnyAsync(p => p.MemberId == memberId
                && p.PlatformType == req.PlatformType
                && p.PlatformAccountKey == req.PlatformAccountKey
                && p.VerifiedStatus != "Revoked");

        if (alreadyLinked)
            throw new InvalidOperationException("บัญชีนี้ผูกไว้แล้ว");

        var mappingReq = new MemberMappingRequest
        {
            MemberId = memberId,
            PlatformType = req.PlatformType,
            PlatformAccountKey = req.PlatformAccountKey,
            PlatformAccountName = req.PlatformAccountName,
            SourceType = "MEMBER_SELF",
            RequestStatus = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        _db.MemberMappingRequests.Add(mappingReq);
        await _db.SaveChangesAsync();

        return new PlatformRequestStatusDto
        {
            RequestId = mappingReq.RequestId,
            PlatformType = mappingReq.PlatformType,
            PlatformAccountKey = mappingReq.PlatformAccountKey,
            PlatformAccountName = mappingReq.PlatformAccountName,
            RequestStatus = mappingReq.RequestStatus,
            CreatedAt = mappingReq.CreatedAt
        };
    }

    /// <summary>ดู platform requests ของ member</summary>
    public async Task<List<PlatformRequestStatusDto>> GetPlatformRequestsAsync(long memberId)
    {
        return await _db.MemberMappingRequests
            .Where(r => r.MemberId == memberId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(20)
            .Select(r => new PlatformRequestStatusDto
            {
                RequestId = r.RequestId,
                PlatformType = r.PlatformType,
                PlatformAccountKey = r.PlatformAccountKey,
                PlatformAccountName = r.PlatformAccountName,
                RequestStatus = r.RequestStatus,
                ReviewNote = r.ReviewNote,
                CreatedAt = r.CreatedAt
            })
            .ToListAsync();
    }

    private static MemberProfileDto MapToProfile(Member m) => new()
    {
        MemberId = m.MemberId,
        MemberCode = m.MemberCode,
        DisplayName = m.DisplayName,
        Phone = m.Phone,
        Email = m.Email,
        Status = m.Status,
        RegisteredAt = m.RegisteredAt,
        Identities = m.Identities.Select(i => new MemberIdentityDto
        {
            MemberIdentityId = i.MemberIdentityId,
            ProviderType = i.ProviderType,
            ProviderUserKey = i.ProviderUserKey,
            DisplayName = i.DisplayName,
            PictureUrl = i.PictureUrl,
            IsActive = i.IsActive
        }).ToList(),
        PlatformAccounts = m.PlatformAccounts.Select(p => new MemberPlatformAccountDto
        {
            MemberPlatformAccountId = p.MemberPlatformAccountId,
            PlatformType = p.PlatformType,
            ShopId = p.ShopId,
            PlatformAccountKey = p.PlatformAccountKey,
            PlatformAccountName = p.PlatformAccountName,
            VerifiedStatus = p.VerifiedStatus,
            LinkMethod = p.LinkMethod,
            IsPrimary = p.IsPrimary
        }).ToList(),
        PointBalance = m.PointAccount == null ? null : new PointBalanceDto
        {
            AvailablePoints = m.PointAccount.AvailablePoints,
            ReservedPoints = m.PointAccount.ReservedPoints,
            TotalEarned = m.PointAccount.TotalEarned,
            TotalBurned = m.PointAccount.TotalBurned,
            TotalExpired = m.PointAccount.TotalExpired,
            LastActivityAt = m.PointAccount.LastActivityAt
        }
    };
}
