using CHMBAPI.Data;
using CHMBAPI.DTOs;
using CHMBAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace CHMBAPI.Services;

public class ContentService
{
    private readonly AppDbContext _db;

    public ContentService(AppDbContext db) => _db = db;

    /// <summary>ดึงเอกสารล่าสุดตามประเภทและภาษา (TERMS, PRIVACY, POLICY)</summary>
    public async Task<ContentDocumentDto?> GetLatestDocumentAsync(string type, string lang = "th")
    {
        return await _db.ContentDocuments
            .Where(d => d.DocumentType == type && d.LanguageCode == lang && d.IsActive)
            .OrderByDescending(d => d.VersionNo)
            .Select(d => new ContentDocumentDto
            {
                DocumentType = d.DocumentType,
                VersionNo = d.VersionNo,
                Title = d.Title,
                ContentHtml = d.ContentHtml
            })
            .FirstOrDefaultAsync();
    }

    /// <summary>บันทึก log การยอมรับเงื่อนไข</summary>
    public async Task LogConsentAsync(long memberId, long documentId, string channel = "LIFF")
    {
        _db.MemberConsentLogs.Add(new MemberConsentLog
        {
            MemberId = memberId,
            DocumentId = documentId,
            AcceptedFlag = true,
            AcceptedAt = DateTime.UtcNow,
            AcceptedFromChannel = channel,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}
