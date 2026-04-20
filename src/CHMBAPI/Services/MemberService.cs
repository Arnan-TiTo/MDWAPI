using CHMBAPI.Data;
using CHMBAPI.DTOs;
using CHMBAPI.Entities;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Member = CHMBAPI.Entities.Members_Mbw;

namespace CHMBAPI.Services;

public class MemberService
{
    private readonly AppDbContext _db;
    private readonly PointService _pointService;
    private readonly TierService _tierService;

    public MemberService(AppDbContext db, PointService pointService, TierService tierService)
    {
        _db = db;
        _pointService = pointService;
        _tierService = tierService;
    }

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

    public async Task<List<TierMasterDto>> GetTiersAsync()
    {
        return await _db.TierMasters
            .Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder)
            .Select(t => new TierMasterDto
            {
                TierId = t.TierId,
                TierCode = t.TierCode,
                TierName = t.TierName,
                MinPoints = t.MinPoints,
                MaxPoints = t.MaxPoints,
                MinSpendAmount = t.MinSpendAmount,
                MaxSpendAmount = t.MaxSpendAmount,
                TierColor = t.TierColor,
                IconUrl = t.IconUrl,
                Description = t.Description,
                SortOrder = t.SortOrder
            })
            .ToListAsync();
    }

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

    public async Task<MemberProfileDto> RegisterAsync(MemberRegisterRequest req)
    {
        int? resolvedCompanysId = req.CompanysId;
        if (!string.IsNullOrEmpty(req.LiffId))
        {
            var oaConfig = await _db.LineOaConfigs.FirstOrDefaultAsync(c => c.LiffId == req.LiffId && c.IsActive);
            if (oaConfig != null)
            {
                resolvedCompanysId = oaConfig.CompanysId;
            }
        }

        if (!string.IsNullOrEmpty(req.LineUserId))
        {
            var isTaken = await _db.MemberIdentities
                .AnyAsync(x => x.ProviderUserKey == req.LineUserId);
            if (isTaken)
            {
                throw new InvalidOperationException("This LINE account is already linked to a member.");
            }
        }

        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var now = DateTime.UtcNow;
            var memberCode = $"MBW-{now:yyMMdd}-{now.Ticks % 1000000:D6}";
            var memberDisplayName = req.DisplayName ?? $"{req.FirstName} {req.LastName}".Trim();

            var member = new Member
            {
                MemberCode = memberCode,
                DisplayName = memberDisplayName,
                FirstName = req.FirstName,
                LastName = req.LastName,
                Phone = req.Phone,
                Email = req.Email,
                BirthDate = req.BirthDate,
                Age = req.Age,
                Status = "Active",
                ConsentAccepted = req.ConsentAccepted,
                ConsentedAt = DateTime.UtcNow,
                RegisteredAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                CompanysId = resolvedCompanysId,
                RegisterChannelId = req.RegisterChannelId,
                PreferredLanguage = req.PreferredLanguage,
                PhoneCountryCode = req.PhoneCountryCode,
                PointsForTier = 0
            };
            _db.Members_Mbw.Add(member);
            await _db.SaveChangesAsync();

            if (!string.IsNullOrEmpty(req.LineUserId))
            {
                _db.MemberIdentities.Add(new MemberIdentity
                {
                    MemberId = member.MemberId,
                    ProviderType = req.LineProviderType ?? "LINE_OA",
                    ProviderUserKey = req.LineUserId,
                    DisplayName = req.LineDisplayName ?? req.DisplayName ?? member.DisplayName,
                    PictureUrl = req.LinePictureUrl,
                    LinkedAt = DateTime.UtcNow,
                    IsActive = true,
                    CompanysId = resolvedCompanysId
                });
            }

            if (req.ProductOptionIds?.Any() == true)
            {
                var selectedOptionIds = req.ProductOptionIds.Distinct().ToList();
                var optionIdsAllowingOtherText = new HashSet<int>();

                if (!string.IsNullOrWhiteSpace(req.OtherProductText))
                {
                    optionIdsAllowingOtherText = await _db.RegistrationProductOptions
                        .Where(o => selectedOptionIds.Contains(o.OptionId) && o.IsAllowOtherText)
                        .Select(o => o.OptionId)
                        .ToHashSetAsync();
                }

                foreach (var optId in selectedOptionIds)
                {
                    _db.MemberRegistrationAnswers.Add(new MemberRegistrationAnswer
                    {
                        MemberId = member.MemberId,
                        OptionId = optId,
                        OtherText = optionIdsAllowingOtherText.Contains(optId) ? req.OtherProductText : null,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            await _db.SaveChangesAsync();

            await _pointService.AdjustAsync(new PointAdjustRequest
            {
                MemberId = member.MemberId,
                AdjustType = "ADD",
                Points = 100,
                Reason = "Welcome Points"
            }, 0, "SYSTEM");

            _db.MemberNotifications.Add(new MemberNotification
            {
                MemberId = member.MemberId,
                NotificationType = "SYSTEM",
                Title = "สมัครสมาชิกสำเร็จ",
                Message = "ยินดีต้อนรับ! คุณได้รับ 100 พอยท์สำหรับสมาชิกใหม่",
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            await _tierService.UpdateMemberTierAsync(member.MemberId, "Initial Registration Tier");

            await transaction.CommitAsync();
            return await GetProfileAsync(member.MemberId);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task EnsureCompanysIdAsync(string lineUserId, int? companysId)
    {
        if (companysId == null) return;

        var identity = await _db.MemberIdentities
            .Include(x => x.Member)
            .FirstOrDefaultAsync(x => x.ProviderUserKey == lineUserId && x.IsActive);

        if (identity == null) return;

        var changed = false;

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

    public async Task<MemberProfileDto> GetProfileAsync(long memberId)
    {
        var member = await _db.Members_Mbw
            .Include(x => x.Identities)
            .Include(x => x.PlatformAccounts)
            .Include(x => x.PointAccount)
            .Include(x => x.CurrentTier)
            .FirstOrDefaultAsync(x => x.MemberId == memberId)
            ?? throw new KeyNotFoundException($"Member {memberId} not found");

        var profile = MapToProfile(member);

        var balance = await _db.PointAccounts
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

        if (balance != null)
        {
            var next30 = DateTime.UtcNow.AddDays(30);
            balance.ExpiringPoints = await _db.PointExpirations
                .Where(x => x.MemberId == memberId && x.Status == "Active" && x.ExpiresAt <= next30)
                .SumAsync(x => x.RemainingPoints);

            balance.NextExpiryDate = await _db.PointExpirations
                .Where(x => x.MemberId == memberId && x.Status == "Active")
                .OrderBy(x => x.ExpiresAt)
                .Select(x => (DateTime?)x.ExpiresAt)
                .FirstOrDefaultAsync();

            profile.PointBalance = balance;
        }

        return profile;
    }

    public async Task<MemberProfileDto?> GetByLineUserIdAsync(string lineUserId)
    {
        var identity = await _db.MemberIdentities
            .FirstOrDefaultAsync(i => i.ProviderUserKey == lineUserId && i.IsActive);

        if (identity == null)
            return null;

        return await GetProfileAsync(identity.MemberId);
    }

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

    public async Task<List<MemberSummaryWithStatsDto>> GetMemberSummaryWithStatsAsync(string? keyword = null, int page = 1, int pageSize = 50)
    {
        var q = _db.Members_Mbw
            .Include(m => m.PlatformAccounts)
            .Include(m => m.PointAccount)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            q = q.Where(m =>
                m.MemberCode.Contains(keyword) ||
                (m.DisplayName != null && m.DisplayName.Contains(keyword)) ||
                (m.Phone != null && m.Phone.Contains(keyword)) ||
                (m.Email != null && m.Email.Contains(keyword)) ||
                m.PlatformAccounts.Any(pa => pa.PlatformAccountKey.Contains(keyword)));
        }

        var members = await q
            .OrderByDescending(m => m.PointAccount != null ? m.PointAccount.TotalEarned : 0)
            .ThenByDescending(m => m.RegisteredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return members.Select(m => new MemberSummaryWithStatsDto
        {
            MemberId = m.MemberId,
            MemberCode = m.MemberCode,
            DisplayName = m.DisplayName,
            Phone = m.Phone,
            Status = m.Status,
            RegisteredAt = m.RegisteredAt,
            PlatformAccountCount = m.PlatformAccounts.Count,
            PlatformTypes = string.Join(", ", m.PlatformAccounts.Select(pa => pa.PlatformType).Distinct()),
            AvailablePoints = m.PointAccount?.AvailablePoints ?? 0,
            TotalEarned = m.PointAccount?.TotalEarned ?? 0,
            TotalBurned = m.PointAccount?.TotalBurned ?? 0,
            ReservedPoints = m.PointAccount?.ReservedPoints ?? 0,
            LinkedOrderCount = _db.OrderMemberLinks.Count(l => l.MemberId == m.MemberId)
        }).ToList();
    }

    public async Task SetStatusAsync(long memberId, string status)
    {
        var member = await _db.Members_Mbw.FindAsync(memberId);
        if (member == null)
            throw new KeyNotFoundException("Member not found");

        member.Status = status;
        member.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<PlatformRequestStatusDto> SubmitPlatformLinkAsync(long memberId, MemberPlatformLinkRequest req)
    {
        _ = await _db.Members_Mbw.FindAsync(memberId)
            ?? throw new KeyNotFoundException($"Member {memberId} not found");

        var existing = await _db.MemberMappingRequests
            .AnyAsync(r => r.MemberId == memberId
                && r.PlatformType == req.PlatformType
                && r.PlatformAccountKey == req.PlatformAccountKey
                && r.RequestStatus == "Pending");

        if (existing)
            throw new InvalidOperationException("คุณมี request สำหรับบัญชีนี้ที่รอดำเนินการอยู่แล้ว");

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

    public async Task<List<MemberRedemption>> GetMemberRedemptionsAsync(long memberId, int page = 1, int pageSize = 20)
    {
        return await _db.MemberRedemptions
            .Where(r => r.MemberId == memberId)
            .Include(r => r.RewardCode)
            .Include(r => r.Fulfillment)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<BulkImportValidateResultDto> AnalyzeImportAsync(Stream stream, string fileName, int? companysId)
    {
        var dtos = new List<MemberImportDto>();
        var isExcel = fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (isExcel) dtos = ParseExcelToDtos(stream);
            else dtos = await ParseCsvToDtos(stream);
        }
        catch
        {
            return new BulkImportValidateResultDto { FileName = fileName, ErrorCount = 1, TotalRows = 0 };
        }

        var result = new BulkImportValidateResultDto
        {
            FileName = fileName,
            TotalRows = dtos.Count,
            Rows = new List<ImportValidationRowDto>()
        };

        var rowNum = 1;
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

    public async Task<BulkImportResultDto> BulkImportAsync(List<MemberImportDto> dtos, int? companysId)
    {
        var result = new BulkImportResultDto { Total = dtos.Count };
        var now = DateTime.UtcNow;
        var maxId = await _db.Members_Mbw.OrderByDescending(x => x.MemberId).Select(x => (long?)x.MemberId).FirstOrDefaultAsync() ?? 0;
        var lastSeq = (int)maxId;

        foreach (var dto in dtos)
        {
            try
            {
                Member? member = null;
                MemberIdentity? identity = null;

                if (!string.IsNullOrEmpty(dto.LineUserId))
                {
                    identity = await _db.MemberIdentities
                        .Include(x => x.Member)
                        .FirstOrDefaultAsync(x => x.ProviderUserKey == dto.LineUserId && x.ProviderType == "LINE_OA");
                    if (identity != null) member = identity.Member;
                }

                if (member == null && !string.IsNullOrEmpty(dto.Phone))
                {
                    member = await _db.Members_Mbw
                        .Include(x => x.Identities)
                        .FirstOrDefaultAsync(x => x.Phone == dto.Phone);
                }

                if (member != null)
                {
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

                    var points = await _db.PointAccounts.FirstOrDefaultAsync(x => x.MemberId == member.MemberId);
                    if (points != null)
                    {
                        points.AvailablePoints = (int)dto.CurrentPoints;
                        points.TotalEarned = (int)dto.TotalPoints;
                        points.UpdatedAt = now;
                    }

                    if (!string.IsNullOrEmpty(dto.LineUserId))
                    {
                        var allLineIds = await _db.MemberIdentities
                            .Where(x => x.MemberId == member.MemberId && x.ProviderType == "LINE_OA")
                            .ToListAsync();

                        if (allLineIds.Any())
                        {
                            var primaryId = allLineIds.First();
                            primaryId.ProviderUserKey = dto.LineUserId;
                            primaryId.DisplayName = dto.DisplayName ?? primaryId.DisplayName;
                            primaryId.PictureUrl = dto.PictureUrl ?? primaryId.PictureUrl;
                            primaryId.IsActive = true;

                            if (allLineIds.Count > 1)
                            {
                                var leftovers = allLineIds.Skip(1);
                                _db.MemberIdentities.RemoveRange(leftovers);
                            }
                        }
                        else
                        {
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
                        identity.DisplayName = dto.DisplayName ?? identity.DisplayName;
                        identity.PictureUrl = dto.PictureUrl ?? identity.PictureUrl;
                    }

                    await _db.SaveChangesAsync();
                    result.Updated++;
                }
                else
                {
                    lastSeq++;
                    var code = $"MBW-{lastSeq:D6}";

                    member = new Member
                    {
                        MemberCode = code,
                        DisplayName = dto.DisplayName,
                        FirstName = dto.FirstName,
                        LastName = dto.LastName,
                        Email = dto.Email,
                        Phone = dto.Phone,
                        BirthDate = dto.BirthDate,
                        Age = dto.Age,
                        Gender = dto.Gender,
                        Address = dto.Address,
                        Subdistrict = dto.Subdistrict,
                        District = dto.District,
                        Province = dto.Province,
                        ZipCode = dto.ZipCode,
                        MemberType = dto.MemberType,
                        Tags = dto.Tags,
                        Branch = dto.Branch,
                        PointsForTier = dto.PointsForTier,
                        UsageCount = dto.UsageCount,
                        LastActiveAt = dto.LastActiveAt,
                        LastActiveDays = dto.LastActiveDays,
                        HowYouKnowMe = dto.HowYouKnowMe,
                        Status = dto.Status ?? "Active",
                        RegisteredAt = dto.RegisteredAt ?? DateTime.UtcNow,
                        CompanysId = companysId,
                        CreatedAt = now
                    };
                    _db.Members_Mbw.Add(member);
                    await _db.SaveChangesAsync();

                    _db.PointAccounts.Add(new PointAccount
                    {
                        MemberId = member.MemberId,
                        AvailablePoints = (int)dto.CurrentPoints,
                        TotalEarned = (int)dto.TotalPoints,
                        UpdatedAt = now
                    });

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
            catch
            {
                result.Failed++;
                _db.ChangeTracker.Clear();
            }
        }

        return result;
    }

    private async Task<string> GenerateMemberCodeAsync()
    {
        var date = DateTime.UtcNow.ToString("yyyyMMdd");
        var count = await _db.Members_Mbw.CountAsync(m => m.MemberCode.StartsWith(date));
        return $"{date}{(count + 1):D4}";
    }

    private async Task<ImportValidationRowDto> ValidateRowAsync(MemberImportDto dto, int rowNum, HashSet<string> seenIdentities)
    {
        var row = new ImportValidationRowDto { RowNumber = rowNum, Data = dto, Status = "Ready" };

        var hasIdentity = !string.IsNullOrEmpty(dto.LineUserId) || !string.IsNullOrEmpty(dto.Phone);
        var hasName = !string.IsNullOrEmpty(dto.FirstName) || !string.IsNullOrEmpty(dto.DisplayName);

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

        if (!string.IsNullOrEmpty(dto.Phone))
        {
            var phone = NormalizePhone(dto.Phone);
            if (seenIdentities.Contains(phone))
            {
                row.Status = "Duplicate";
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
                row.Status = "Duplicate";
                row.Message = $"Duplicate LineUserId '{dto.LineUserId}' found in this file (Will Upsert)";
                return row;
            }
            seenIdentities.Add(lineId);
        }

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

        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i <= range.ColumnCount(); i++)
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

        using var reader = new StreamReader(stream, Encoding.GetEncoding("windows-874"), true);
        var headerLine = await reader.ReadLineAsync();
        if (headerLine == null) return dtos;

        var headerParts = headerLine.Split(',');
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headerParts.Length; i++)
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
        var digits = new string(phone.Where(char.IsDigit).ToArray());
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
}
