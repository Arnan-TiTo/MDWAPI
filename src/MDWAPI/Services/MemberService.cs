using MDWAPI.Data;
using MDWAPI.DTOs;
using MDWAPI.Entities;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using System.Text;

namespace MDWAPI.Services;

public class MemberService
{
    private readonly AppDbContext _db;

    public MemberService(AppDbContext db) => _db = db;

    /// <summary>สมัครสมาชิกใหม่</summary>
    public async Task<MemberProfileDto> RegisterAsync(MemberRegisterRequest req)
    {
        // 1. generate code
        var seq = await _db.Members_Mbw.CountAsync() + 1;
        var code = $"MBW-{seq:D6}";

        // 2. create member
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
            CreatedAt = DateTime.UtcNow,
            CompanysId = req.CompanysId,
            RegisterChannelId = req.RegisterChannelId,
            PreferredLanguage = req.PreferredLanguage,
            PhoneCountryCode = req.PhoneCountryCode
        };

        _db.Members_Mbw.Add(member);
        await _db.SaveChangesAsync();

        // 3. handle registration answers
        if (req.ProductOptionIds != null && req.ProductOptionIds.Any())
        {
            foreach (var optId in req.ProductOptionIds)
            {
                _db.MemberRegistrationAnswers.Add(new MemberRegistrationAnswer
                {
                    MemberId = member.MemberId,
                    OptionId = optId,
                    OtherText = req.OtherProductText,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        // 4. handle consent logs
        if (req.ConsentAccepted)
        {
            // Find latest active TERMS and PRIVACY
            var docs = await _db.ContentDocuments
                .Where(d => d.IsActive && (d.DocumentType == "TERMS" || d.DocumentType == "PRIVACY"))
                .ToListAsync();

            foreach (var doc in docs)
            {
                _db.MemberConsentLogs.Add(new MemberConsentLog
                {
                    MemberId = member.MemberId,
                    DocumentId = doc.DocumentId,
                    AcceptedFlag = true,
                    AcceptedAt = DateTime.UtcNow,
                    AcceptedFromChannel = "LIFF",
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        // 5. สร้าง PointAccount
        _db.PointAccounts.Add(new PointAccount
        {
            MemberId = member.MemberId,
            UpdatedAt = DateTime.UtcNow
        });

        // 6. สร้าง LINE identity ถ้ามี
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
                IsActive = true,
                CompanysId = req.CompanysId
            });
        }

        await _db.SaveChangesAsync();

        return await GetProfileAsync(member.MemberId);
    }

    /// <summary>ถ้า Member/MemberIdentity ยังไม่มี CompanysId → set ให้</summary>
    public async Task EnsureCompanysIdAsync(string lineUserId, int? companysId)
    {
        if (companysId == null) return;

        var identity = await _db.MemberIdentities
            .Include(x => x.Member)
            .FirstOrDefaultAsync(x => x.ProviderUserKey == lineUserId && x.IsActive);

        if (identity == null) return;

        bool changed = false;

        if (identity.CompanysId == null)
        {
            identity.CompanysId = companysId;
            changed = true;
        }

        if (identity.Member.CompanysId == null)
        {
            identity.Member.CompanysId = companysId;
            changed = true;
        }

        if (changed)
            await _db.SaveChangesAsync();
    }

    /// <summary>ดู profile สมาชิก</summary>
    public async Task<MemberProfileDto> GetProfileAsync(long memberId)
    {
        var m = await _db.Members_Mbw
            .Include(x => x.Identities)
            .Include(x => x.PlatformAccounts)
            .Include(x => x.PointAccount)
            .Include(x => x.CurrentTier)
            .FirstOrDefaultAsync(x => x.MemberId == memberId)
            ?? throw new KeyNotFoundException($"Member {memberId} not found");

        var profile = MapToProfile(m);

        // Fetch point balance specifically to include expiring points etc.
        var bal = await _db.PointAccounts
            .Where(x => x.MemberId == memberId)
            .Select(x => new PointBalanceDto
            {
                AvailablePoints = x.AvailablePoints,
                PendingPoints = x.PendingPoints,
                ReservedPoints = x.ReservedPoints,
                TotalEarned = x.TotalEarned,
                TotalBurned = x.TotalBurned,
                TotalExpired = x.TotalExpired,
                LastActivityAt = x.LastActivityAt
            })
            .FirstOrDefaultAsync();
        
        if (bal != null)
        {
            // Calculate expiring in 30 days
            var next30 = DateTime.UtcNow.AddDays(30);
            bal.ExpiringPoints = await _db.PointExpirations
                .Where(x => x.MemberId == memberId && x.Status == "Active" && x.ExpiresAt <= next30)
                .SumAsync(x => x.RemainingPoints);
            
            bal.NextExpiryDate = await _db.PointExpirations
                .Where(x => x.MemberId == memberId && x.Status == "Active")
                .OrderBy(x => x.ExpiresAt)
                .Select(x => (DateTime?)x.ExpiresAt)
                .FirstOrDefaultAsync();

            profile.PointBalance = bal;
        }

        return profile;
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
        if (req.FirstName != null) member.FirstName = req.FirstName;
        if (req.LastName != null) member.LastName = req.LastName;
        if (req.Phone != null) member.Phone = req.Phone;
        if (req.Email != null) member.Email = req.Email;
        if (req.BirthDate != null) member.BirthDate = req.BirthDate;
        if (req.Address != null) member.Address = req.Address;
        if (req.Subdistrict != null) member.Subdistrict = req.Subdistrict;
        if (req.District != null) member.District = req.District;
        if (req.Province != null) member.Province = req.Province;
        if (req.ZipCode != null) member.ZipCode = req.ZipCode;
        if (req.Remark != null) member.HowYouKnowMe = req.Remark;
        if (req.PreferredLanguage != null) member.PreferredLanguage = req.PreferredLanguage;
        
        member.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return await GetProfileAsync(memberId);
    }

    /// <summary>Admin เปลี่ยนสถานะ member</summary>
    public async Task SetStatusAsync(long memberId, string status)
    {
        var member = await _db.Members_Mbw.FindAsync(memberId)
            ?? throw new KeyNotFoundException($"Member {memberId} not found");

        member.Status = status;
        member.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
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

    /// <summary>นำเข้าสมาชิกแบบกลุ่มจาก LINE OA</summary>
    public async Task<BulkImportResultDto> BulkImportAsync(List<MemberImportDto> dtos, int? companysId)
    {
        var result = new BulkImportResultDto { Total = dtos.Count };
        var now = DateTime.UtcNow;

        // ดึงลำดับสูงสุดปัจจุบันมาใช้รัน MemberCode (ใช้ Max MemberId แทน Count เพื่อเลี่ยงเลขซ้ำ)
        var maxId = await _db.Members_Mbw.OrderByDescending(x => x.MemberId).Select(x => (long?)x.MemberId).FirstOrDefaultAsync() ?? 0;
        var lastSeq = (int)maxId;

        foreach (var dto in dtos)
        {
            try
            {
                Member? member = null;
                MemberIdentity? identity = null;

                // 1. Try lookup by LineUserId (Identity)
                if (!string.IsNullOrEmpty(dto.LineUserId))
                {
                    identity = await _db.MemberIdentities
                        .Include(x => x.Member)
                        .FirstOrDefaultAsync(x => x.ProviderUserKey == dto.LineUserId && x.ProviderType == "LINE_OA");
                    if (identity != null) member = identity.Member;
                }

                // 2. Try lookup by Phone
                if (member == null && !string.IsNullOrEmpty(dto.Phone))
                {
                    member = await _db.Members_Mbw
                        .Include(x => x.Identities)
                        .FirstOrDefaultAsync(x => x.Phone == dto.Phone);
                }

                if (member != null)
                {
                    // Update existing
                    member.DisplayName = dto.DisplayName ?? member.DisplayName;
                    member.FirstName = dto.FirstName ?? member.FirstName;
                    member.LastName = dto.LastName ?? member.LastName;
                    member.BirthDate = dto.BirthDate ?? member.BirthDate;
                    member.Age = dto.Age ?? member.Age;
                    member.Gender = dto.Gender ?? member.Gender;
                    member.Address = dto.Address ?? member.Address;
                    member.Subdistrict = dto.Subdistrict ?? member.Subdistrict;
                    member.District = dto.District ?? member.District;
                    member.Province = dto.Province ?? member.Province;
                    member.ZipCode = dto.ZipCode ?? member.ZipCode;
                    member.MembershipTier = dto.MembershipTier ?? member.MembershipTier;
                    member.Tags = dto.Tags ?? member.Tags;
                    member.Branch = dto.Branch ?? member.Branch;
                    member.PointsForTier = dto.PointsForTier != 0 ? dto.PointsForTier : member.PointsForTier;
                    member.UsageCount = dto.UsageCount != 0 ? dto.UsageCount : member.UsageCount;
                    member.LastActiveAt = dto.LastActiveAt ?? member.LastActiveAt;
                    member.LastActiveDays = dto.LastActiveDays ?? member.LastActiveDays;
                    member.MemberType = dto.MemberType ?? member.MemberType;
                    member.HowYouKnowMe = dto.HowYouKnowMe ?? member.HowYouKnowMe;
                    if (!string.IsNullOrEmpty(dto.Email)) member.Email = dto.Email;
                    if (dto.RegisteredAt.HasValue) member.RegisteredAt = dto.RegisteredAt.Value;
                    if (!string.IsNullOrEmpty(dto.Status)) member.Status = dto.Status;

                    member.UpdatedAt = now;

                    // Update PointAccount if exists
                    var points = await _db.PointAccounts.FirstOrDefaultAsync(x => x.MemberId == member.MemberId);
                    if (points != null)
                    {
                        points.AvailablePoints = (int)dto.CurrentPoints;
                        points.TotalEarned = (int)dto.TotalPoints;
                        points.UpdatedAt = now;
                    }

                    // Update or Add Identity (Single LINE Account Enforcement)
                    if (!string.IsNullOrEmpty(dto.LineUserId))
                    {
                        // Fetch all existing LINE identities for this member
                        var allLineIds = await _db.MemberIdentities
                            .Where(x => x.MemberId == member.MemberId && x.ProviderType == "LINE_OA")
                            .ToListAsync();
                        
                        if (allLineIds.Any())
                        {
                            // Keep the first one and update it
                            var primaryId = allLineIds.First();
                            primaryId.ProviderUserKey = dto.LineUserId;
                            primaryId.DisplayName = dto.DisplayName ?? primaryId.DisplayName;
                            primaryId.PictureUrl = dto.PictureUrl ?? primaryId.PictureUrl;
                            primaryId.IsActive = true;

                            // Remove any extra duplicates
                            if (allLineIds.Count > 1)
                            {
                                var leftovers = allLineIds.Skip(1);
                                _db.MemberIdentities.RemoveRange(leftovers);
                            }
                        }
                        else
                        {
                            // Create new one only if they didn't have any LINE identity before
                            _db.MemberIdentities.Add(new MemberIdentity
                            {
                                MemberId = member.MemberId,
                                ProviderType = "LINE_OA",
                                ProviderUserKey = dto.LineUserId,
                                DisplayName = dto.DisplayName,
                                PictureUrl = dto.PictureUrl,
                                LinkedAt = now,
                                IsActive = true,
                                CompanysId = companysId
                            });
                        }
                    }
                    else if (identity != null)
                    {
                        // Update existing match if no new LineUserId provided in file
                        identity.DisplayName = dto.DisplayName ?? identity.DisplayName;
                        identity.PictureUrl = dto.PictureUrl ?? identity.PictureUrl;
                    }

                    await _db.SaveChangesAsync();
                    result.Updated++;
                }
                else
                {
                    // Create New
                    lastSeq++;
                    var code = $"MBW-{lastSeq:D6}";

                    member = new Member
                    {
                        MemberCode = code,
                        DisplayName = dto.DisplayName,
                        FirstName = dto.FirstName,
                        LastName = dto.LastName,
                        Phone = dto.Phone,
                        Email = dto.Email,
                        BirthDate = dto.BirthDate,
                        Age = dto.Age,
                        Gender = dto.Gender,
                        Address = dto.Address,
                        Subdistrict = dto.Subdistrict,
                        District = dto.District,
                        Province = dto.Province,
                        ZipCode = dto.ZipCode,
                        MembershipTier = dto.MembershipTier,
                        Tags = dto.Tags,
                        Branch = dto.Branch,
                        PointsForTier = dto.PointsForTier,
                        UsageCount = dto.UsageCount,
                        LastActiveAt = dto.LastActiveAt,
                        LastActiveDays = dto.LastActiveDays,
                        MemberType = dto.MemberType,
                        HowYouKnowMe = dto.HowYouKnowMe,
                        Status = dto.Status ?? "Active",
                        RegisteredAt = dto.RegisteredAt ?? now,
                        CompanysId = companysId,
                        CreatedAt = now
                    };
                    _db.Members_Mbw.Add(member);
                    await _db.SaveChangesAsync(); // เอา MemberId

                    // Initialize PointAccount
                    _db.PointAccounts.Add(new PointAccount 
                    { 
                        MemberId = member.MemberId, 
                        AvailablePoints = (int)dto.CurrentPoints,
                        TotalEarned = (int)dto.TotalPoints,
                        UpdatedAt = now 
                    });

                    // Add identity if LineUserId provided
                    if (!string.IsNullOrEmpty(dto.LineUserId))
                    {
                        _db.MemberIdentities.Add(new MemberIdentity
                        {
                            MemberId = member.MemberId,
                            ProviderType = "LINE_OA",
                            ProviderUserKey = dto.LineUserId,
                            DisplayName = dto.DisplayName,
                            PictureUrl = dto.PictureUrl,
                            LinkedAt = dto.RegisteredAt ?? now,
                            IsActive = true,
                            CompanysId = companysId
                        });
                    }

                    result.Created++;
                }
            }
            catch (Exception)
            {
                result.Failed++;
                // Important: Clear the errored entities from the context so they don't block the next row's SaveChangesAsync
                _db.ChangeTracker.Clear(); 
            }
        }

        return result;
    }

    /// <summary>อ่าน CSV (Custom format) แล้วนำเข้าสมาชิก</summary>
    public async Task<BulkImportResultDto> BulkImportFromCsvAsync(Stream stream, int? companysId)
    {
        var dtos = new List<MemberImportDto>();
        try
        {
            using var reader = new StreamReader(stream);
            var headerLine = await reader.ReadLineAsync(); // Skip header
            
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Simple CSV split (note: doesn't handle escaped commas in quotes if any, but let's assume standard for now)
                // For more robust parsing, use a library, but let's try a split first.
                var parts = line.Split(','); 
                if (parts.Length < 24) continue;

                var dto = new MemberImportDto
                {
                    MemberType = parts[0].Trim(),
                    FirstName = parts[1].Trim(),
                    LastName = parts[2].Trim(),
                    Phone = parts[3].Trim(),
                    BirthDate = ParseDate(parts[4].Trim(), "dd-MM-yyyy"),
                    Age = int.TryParse(parts[5].Trim(), out var age) ? age : null,
                    Email = parts[6].Trim() == "-" ? null : parts[6].Trim(),
                    Gender = parts[7].Trim() == "-" ? null : parts[7].Trim(),
                    Address = parts[8].Trim() == "-" ? null : parts[8].Trim(),
                    Subdistrict = parts[9].Trim() == "-" ? null : parts[9].Trim(),
                    District = parts[10].Trim() == "-" ? null : parts[10].Trim(),
                    Province = parts[11].Trim() == "-" ? null : parts[11].Trim(),
                    ZipCode = parts[12].Trim() == "-" ? null : parts[12].Trim(),
                    MembershipTier = parts[13].Trim() == "-" ? null : parts[13].Trim(),
                    Tags = parts[14].Trim().Replace("\"", ""), // Remove quotes from CSV
                    Branch = parts[15].Trim(),
                    CurrentPoints = decimal.TryParse(parts[16].Trim(), out var cp) ? cp : 0,
                    TotalPoints = decimal.TryParse(parts[17].Trim(), out var tp) ? tp : 0,
                    PointsForTier = decimal.TryParse(parts[18].Trim(), out var pft) ? pft : 0,
                    UsageCount = int.TryParse(parts[19].Trim(), out var usage) ? usage : 0,
                    LastActiveAt = ParseDate(parts[20].Trim(), "dd-MM-yyyy HH:mm"),
                    LastActiveDays = int.TryParse(parts[21].Trim(), out var lad) ? lad : null,
                    Status = parts[22].Trim(),
                    RegisteredAt = ParseDate(parts[23].Trim(), "dd-MM-yyyy HH:mm"),
                    HowYouKnowMe = parts.Length > 24 ? parts[24].Trim() : null,
                    LineUserId = parts.Length > 25 ? parts[25].Trim() : null,
                    DisplayName = $"{parts[1].Trim()} {parts[2].Trim()}".Trim()
                };

                dtos.Add(dto);
            }
        }
        catch (Exception ex)
        {
            return new BulkImportResultDto { Total = 0, Failed = 1, Errors = { $"Failed to parse CSV: {ex.Message}" } };
        }

        return await BulkImportAsync(dtos, companysId);
    }

    // Note: BulkImportFromExcelAsync and BulkImportFromCsvAsync are now legacy or can call the new flow if needed.
    // They are kept for backward compatibility but redirect to BulkImportAsync.

    /// <summary>วิเคราะห์ไฟล์ก่อนนำเข้า (Interactive Flow)</summary>
    public async Task<BulkImportValidateResultDto> AnalyzeImportAsync(Stream stream, string fileName, int? companysId)
    {
        var dtos = new List<MemberImportDto>();
        var isExcel = fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (isExcel) dtos = ParseExcelToDtos(stream);
            else dtos = await ParseCsvToDtos(stream);
        }
        catch (Exception)
        {
            return new BulkImportValidateResultDto { FileName = fileName, ErrorCount = 1, TotalRows = 0 };
        }

        var result = new BulkImportValidateResultDto
        {
            FileName = fileName,
            TotalRows = dtos.Count,
            Rows = new List<ImportValidationRowDto>()
        };

        int rowNum = 1;
        var seenIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var dto in dtos)
        {
            var row = await ValidateRowAsync(dto, rowNum++, seenIdentities);
            result.Rows.Add(row);
            
            if (row.Status == "Ready") result.ReadyCount++;
            else if (row.Status == "Duplicate") result.DuplicateCount++;
            else result.ErrorCount++;
        }

        return result;
    }

    private async Task<ImportValidationRowDto> ValidateRowAsync(MemberImportDto dto, int rowNum, HashSet<string> seenIdentities)
    {
        var row = new ImportValidationRowDto { RowNumber = rowNum, Data = dto, Status = "Ready" };

        // 1. Check completeness (First Name or Display Name + Phone or LineUserId)
        bool hasIdentity = !string.IsNullOrEmpty(dto.LineUserId) || !string.IsNullOrEmpty(dto.Phone);
        bool hasName = !string.IsNullOrEmpty(dto.FirstName) || !string.IsNullOrEmpty(dto.DisplayName);

        if (!hasIdentity)
        {
            row.Status = "Invalid";
            row.Message = "Missing Phone or Line User ID";
            return row;
        }

        if (!hasName)
        {
            row.Status = "Invalid";
            row.Message = "Missing Name";
            return row;
        }

        // 2. Check Duplicate IN FILE
        if (!string.IsNullOrEmpty(dto.Phone))
        {
            var phone = NormalizePhone(dto.Phone);
            if (seenIdentities.Contains(phone))
            {
                row.Status = "Duplicate"; // Change from Invalid to Duplicate to allow import
                row.Message = $"Duplicate phone '{dto.Phone}' found in this file (Will Upsert)";
                return row;
            }
            seenIdentities.Add(phone);
        }

        if (!string.IsNullOrEmpty(dto.LineUserId))
        {
            var lineId = dto.LineUserId.Trim();
            if (seenIdentities.Contains(lineId))
            {
                row.Status = "Duplicate"; // Change from Invalid to Duplicate to allow import
                row.Message = $"Duplicate LineUserId '{dto.LineUserId}' found in this file (Will Upsert)";
                return row;
            }
            seenIdentities.Add(lineId);
        }
    
        // 3. Check Duplicate in DB
        Member? existing = null;
        if (!string.IsNullOrEmpty(dto.LineUserId))
        {
            var identity = await _db.MemberIdentities.FirstOrDefaultAsync(x => x.ProviderUserKey == dto.LineUserId && x.IsActive);
            if (identity != null) existing = await _db.Members_Mbw.FindAsync(identity.MemberId);
        }

        if (existing == null && !string.IsNullOrEmpty(dto.Phone))
        {
            var phone = NormalizePhone(dto.Phone);
            existing = await _db.Members_Mbw.FirstOrDefaultAsync(x => x.Phone == phone);
        }

        if (existing != null)
        {
            row.Status = "Duplicate";
            row.Message = $"Existing Member found: {existing.MemberCode} ({existing.DisplayName})";
        }

        return row;
    }

    private List<MemberImportDto> ParseExcelToDtos(Stream stream)
    {
        var dtos = new List<MemberImportDto>();
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(1);
        var range = worksheet.RangeUsed();
        if (range == null) return dtos;

        var rows = range.RowsUsed();
        var headerRow = rows.First();
        var dataRows = rows.Skip(1);

        // Map headers to indices
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i <= range.ColumnCount(); i++)
        {
            var val = headerRow.Cell(i).GetValue<string>().Trim();
            if (!string.IsNullOrEmpty(val) && !headers.ContainsKey(val))
                headers[val] = i;
        }

        int GetIdx(params string[] names)
        {
            foreach (var n in names)
            {
                if (headers.TryGetValue(n, out var idx)) return idx;
                // Partially match
                var key = headers.Keys.FirstOrDefault(k => k.Contains(n, StringComparison.OrdinalIgnoreCase));
                if (key != null) return headers[key];
            }
            return -1;
        }

        var idxType = GetIdx("Member type");
        var idxFn = GetIdx("First name", "Name");
        var idxLn = GetIdx("Last name");
        var idxPhone = GetIdx("Phone", "Mobile", "เบอร์โทร");
        var idxBirth = GetIdx("Birth", "วันเกิด");
        var idxAge = GetIdx("Age", "อายุ");
        var idxGender = GetIdx("Gender", "เพศ");
        var idxAddr = GetIdx("Address", "ที่อยู่");
        var idxSub = GetIdx("Subdistrict", "แขวง", "ตำบล");
        var idxDist = GetIdx("District", "เขต", "อำเภอ");
        var idxProv = GetIdx("Province", "จังหวัด");
        var idxZip = GetIdx("Zip", "รหัสไปรษณีย์");
        var idxTier = GetIdx("Tier", "ระดับ");
        var idxTags = GetIdx("Tags", "แท็ก");
        var idxBranch = GetIdx("Branch", "สาขา");
        var idxCurPts = GetIdx("Current points", "แต้มปัจจุบัน");
        var idxTotalPts = GetIdx("Total points", "แต้มสะสม");
        var idxPtsForTier = GetIdx("Points for membership tier", "PointsForTier");
        var idxUsage = GetIdx("Usage", "จำนวนครั้งที่ใช้");
        var idxLastActive = GetIdx("Last active date", "ใช้งานล่าสุด");
        var idxLastDays = GetIdx("Last active days");
        var idxStatus = GetIdx("Status", "สถานะ");
        var idxRegAt = GetIdx("Registered", "วันที่สมัคร");
        var idxHow = GetIdx("How you know", "รู้จักเราได้อย่างไร");
        var idxEmail = GetIdx("Email", "อีเมล");
        var idxLineId = GetIdx("User Id", "LineUserId");

        foreach (var row in dataRows)
        {
            var dto = new MemberImportDto();
            if (idxType > 0) dto.MemberType = row.Cell(idxType).GetValue<string>().Trim();
            if (idxFn > 0) dto.FirstName = row.Cell(idxFn).GetValue<string>().Trim();
            if (idxLn > 0) dto.LastName = row.Cell(idxLn).GetValue<string>().Trim();
            if (idxPhone > 0) dto.Phone = NormalizePhone(row.Cell(idxPhone).GetValue<string>());
            if (idxBirth > 0) dto.BirthDate = ParseDate(row.Cell(idxBirth).GetValue<string>().Trim(), "dd-MM-yyyy");
            if (idxAge > 0) dto.Age = int.TryParse(row.Cell(idxAge).GetValue<string>().Trim(), out var age) ? age : null;
            if (idxGender > 0) dto.Gender = row.Cell(idxGender).GetValue<string>().Trim() == "-" ? null : row.Cell(idxGender).GetValue<string>().Trim();
            if (idxAddr > 0) dto.Address = row.Cell(idxAddr).GetValue<string>().Trim() == "-" ? null : row.Cell(idxAddr).GetValue<string>().Trim();
            if (idxSub > 0) dto.Subdistrict = row.Cell(idxSub).GetValue<string>().Trim() == "-" ? null : row.Cell(idxSub).GetValue<string>().Trim();
            if (idxDist > 0) dto.District = row.Cell(idxDist).GetValue<string>().Trim() == "-" ? null : row.Cell(idxDist).GetValue<string>().Trim();
            if (idxProv > 0) dto.Province = row.Cell(idxProv).GetValue<string>().Trim() == "-" ? null : row.Cell(idxProv).GetValue<string>().Trim();
            if (idxZip > 0) dto.ZipCode = row.Cell(idxZip).GetValue<string>().Trim() == "-" ? null : row.Cell(idxZip).GetValue<string>().Trim();
            if (idxTier > 0) dto.MembershipTier = row.Cell(idxTier).GetValue<string>().Trim() == "-" ? null : row.Cell(idxTier).GetValue<string>().Trim();
            if (idxTags > 0) dto.Tags = row.Cell(idxTags).GetValue<string>().Trim();
            if (idxBranch > 0) dto.Branch = row.Cell(idxBranch).GetValue<string>().Trim();
            
            if (idxCurPts > 0) dto.CurrentPoints = (decimal)row.Cell(idxCurPts).GetDouble();
            if (idxTotalPts > 0) dto.TotalPoints = (decimal)row.Cell(idxTotalPts).GetDouble();
            if (idxPtsForTier > 0) dto.PointsForTier = (decimal)row.Cell(idxPtsForTier).GetDouble();
            if (idxUsage > 0) dto.UsageCount = (int)row.Cell(idxUsage).GetDouble();
            
            if (idxLastActive > 0) dto.LastActiveAt = ParseDate(row.Cell(idxLastActive).GetValue<string>().Trim(), "dd-MM-yyyy HH:mm");
            if (idxLastDays > 0) dto.LastActiveDays = int.TryParse(row.Cell(idxLastDays).GetValue<string>().Trim(), out var lad) ? lad : null;
            if (idxStatus > 0) dto.Status = row.Cell(idxStatus).GetValue<string>().Trim();
            if (idxRegAt > 0) dto.RegisteredAt = ParseDate(row.Cell(idxRegAt).GetValue<string>().Trim(), "dd-MM-yyyy HH:mm");
            if (idxHow > 0) dto.HowYouKnowMe = row.Cell(idxHow).GetValue<string>().Trim();
            if (idxEmail > 0) dto.Email = row.Cell(idxEmail).GetValue<string>().Trim();
            if (idxLineId > 0) dto.LineUserId = row.Cell(idxLineId).GetValue<string>().Trim();
            
            dto.DisplayName = $"{dto.FirstName} {dto.LastName}".Trim();
            if (string.IsNullOrEmpty(dto.DisplayName) && idxLineId > 0) dto.DisplayName = dto.LineUserId;

            dtos.Add(dto);
        }
        return dtos;
    }

    private async Task<List<MemberImportDto>> ParseCsvToDtos(Stream stream)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var dtos = new List<MemberImportDto>();
        
        // Detect encoding or default to Windows-874 for Thai
        using var reader = new StreamReader(stream, Encoding.GetEncoding("windows-874"), true);
        var headerLine = await reader.ReadLineAsync();
        if (headerLine == null) return dtos;

        var headerParts = headerLine.Split(',');
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headerParts.Length; i++)
        {
            var val = headerParts[i].Trim().Trim('"');
            if (!string.IsNullOrEmpty(val) && !headers.ContainsKey(val))
                headers[val] = i;
        }

        int GetIdx(params string[] names)
        {
            foreach (var n in names)
            {
                if (headers.TryGetValue(n, out var idx)) return idx;
                var key = headers.Keys.FirstOrDefault(k => k.Contains(n, StringComparison.OrdinalIgnoreCase));
                if (key != null) return headers[key];
            }
            return -1;
        }

        var idxType = GetIdx("Member type");
        var idxFn = GetIdx("First name", "Name");
        var idxLn = GetIdx("Last name");
        var idxPhone = GetIdx("Phone", "Mobile", "เบอร์โทร");
        var idxBirth = GetIdx("Birth", "วันเกิด");
        var idxAge = GetIdx("Age", "อายุ");
        var idxGender = GetIdx("Gender", "เพศ");
        var idxAddr = GetIdx("Address", "ที่อยู่");
        var idxSub = GetIdx("Subdistrict", "แขวง", "ตำบล");
        var idxDist = GetIdx("District", "เขต", "อำเภอ");
        var idxProv = GetIdx("Province", "จังหวัด");
        var idxZip = GetIdx("Zip", "รหัสไปรษณีย์");
        var idxTier = GetIdx("Tier", "ระดับ");
        var idxTags = GetIdx("Tags", "แท็ก");
        var idxBranch = GetIdx("Branch", "สาขา");
        var idxCurPts = GetIdx("Current points", "แต้มปัจจุบัน");
        var idxTotalPts = GetIdx("Total points", "แต้มสะสม");
        var idxPtsForTier = GetIdx("Points for membership tier", "PointsForTier");
        var idxUsage = GetIdx("Usage", "จำนวนครั้งที่ใช้");
        var idxLastActive = GetIdx("Last active date", "ใช้งานล่าสุด");
        var idxLastDays = GetIdx("Last active days");
        var idxStatus = GetIdx("Status", "สถานะ");
        var idxRegAt = GetIdx("Registered", "วันที่สมัคร");
        var idxHow = GetIdx("How you know", "รู้จักเราได้อย่างไร");
        var idxEmail = GetIdx("Email", "อีเมล");
        var idxLineId = GetIdx("User Id", "LineUserId");

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(','); 

            var dto = new MemberImportDto();
            string GetVal(int idx) => idx >= 0 && idx < parts.Length ? parts[idx].Trim().Trim('"') : "";

            if (idxType >= 0) dto.MemberType = GetVal(idxType);
            if (idxFn >= 0) dto.FirstName = GetVal(idxFn).Trim();
            if (idxLn >= 0) dto.LastName = GetVal(idxLn).Trim();
            if (idxPhone >= 0) dto.Phone = NormalizePhone(GetVal(idxPhone));
            if (idxBirth >= 0) dto.BirthDate = ParseDate(GetVal(idxBirth), "dd-MM-yyyy");
            if (idxAge >= 0) dto.Age = int.TryParse(GetVal(idxAge), out var age) ? age : null;
            if (idxGender >= 0) dto.Gender = GetVal(idxGender);
            if (idxAddr >= 0) dto.Address = GetVal(idxAddr);
            if (idxSub >= 0) dto.Subdistrict = GetVal(idxSub);
            if (idxDist >= 0) dto.District = GetVal(idxDist);
            if (idxProv >= 0) dto.Province = GetVal(idxProv);
            if (idxZip >= 0) dto.ZipCode = GetVal(idxZip);
            if (idxTier >= 0) dto.MembershipTier = GetVal(idxTier);
            if (idxTags >= 0) dto.Tags = GetVal(idxTags);
            if (idxBranch >= 0) dto.Branch = GetVal(idxBranch);
            
            if (idxCurPts >= 0) dto.CurrentPoints = decimal.TryParse(GetVal(idxCurPts), out var cp) ? cp : 0;
            if (idxTotalPts >= 0) dto.TotalPoints = decimal.TryParse(GetVal(idxTotalPts), out var tp) ? tp : 0;
            if (idxPtsForTier >= 0) dto.PointsForTier = decimal.TryParse(GetVal(idxPtsForTier), out var pft) ? pft : 0;
            if (idxUsage >= 0) dto.UsageCount = int.TryParse(GetVal(idxUsage), out var usage) ? usage : 0;
            
            if (idxLastActive >= 0) dto.LastActiveAt = ParseDate(GetVal(idxLastActive), "dd-MM-yyyy HH:mm");
            if (idxLastDays >= 0) dto.LastActiveDays = int.TryParse(GetVal(idxLastDays), out var lad) ? lad : null;
            if (idxStatus >= 0) dto.Status = GetVal(idxStatus);
            if (idxRegAt >= 0) dto.RegisteredAt = ParseDate(GetVal(idxRegAt), "dd-MM-yyyy HH:mm");
            if (idxHow >= 0) dto.HowYouKnowMe = GetVal(idxHow);
            if (idxEmail >= 0) dto.Email = GetVal(idxEmail);
            if (idxLineId >= 0) dto.LineUserId = GetVal(idxLineId);
            
            dto.DisplayName = $"{dto.FirstName} {dto.LastName}".Trim();
            if (string.IsNullOrEmpty(dto.DisplayName) && idxLineId >= 0) dto.DisplayName = dto.LineUserId;

            dtos.Add(dto);
        }
        return dtos;
    }

    private static DateTime? ParseDate(string? value, string format)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "-") return null;
        if (DateTime.TryParseExact(value, format, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
            return dt;
        if (DateTime.TryParse(value, out var dt2))
            return dt2;
        return null;
    }

    private static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrEmpty(phone)) return "";
        // Remove non-digit characters (e.g. -, space, +, etc.)
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        
        // Handle Excel removing leading zero (e.g. 812345678 -> 0812345678)
        if (digits.Length == 9 && !digits.StartsWith("0"))
            digits = "0" + digits;
            
        return digits;
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
        FirstName = m.FirstName,
        LastName = m.LastName,
        Gender = m.Gender,
        BirthDate = m.BirthDate,
        Age = m.Age,
        Address = m.Address,
        Subdistrict = m.Subdistrict,
        District = m.District,
        Province = m.Province,
        ZipCode = m.ZipCode,
        MemberType = m.MemberType,
        CurrentTierId = m.CurrentTierId,
        MembershipTier = m.CurrentTier?.TierName ?? m.MembershipTier,
        TierColor = m.CurrentTier?.TierColor,
        TierIconUrl = m.CurrentTier?.IconUrl,
        Tags = m.Tags,
        Branch = m.Branch,
        PointsForTier = m.PointsForTier,
        UsageCount = m.UsageCount,
        LastActiveAt = m.LastActiveAt,
        HowYouKnowMe = m.HowYouKnowMe,
        PreferredLanguage = m.PreferredLanguage,
        PhoneCountryCode = m.PhoneCountryCode,
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
        }).ToList()
    };

    /// <summary>ดึงรายการช่องทางการสมัคร (Masters)</summary>
    public async Task<List<MemberChannelDto>> GetChannelsAsync()
    {
        return await _db.MemberChannels
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => new MemberChannelDto
            {
                ChannelId = c.ChannelId,
                ChannelCode = c.ChannelCode,
                ChannelName = c.ChannelName
            })
            .ToListAsync();
    }

    /// <summary>ดึงตัวเลือก "คุณรู้จักเราผ่านที่ไหน" (Masters)</summary>
    public async Task<List<RegistrationProductOptionDto>> GetRegistrationOptionsAsync()
    {
        return await _db.RegistrationProductOptions
            .Where(o => o.IsActive)
            .OrderBy(o => o.SortOrder)
            .Select(o => new RegistrationProductOptionDto
            {
                OptionId = o.OptionId,
                OptionCode = o.OptionCode,
                OptionName = o.OptionName,
                IsAllowOtherText = o.IsAllowOtherText
            })
            .ToListAsync();
    }
}
