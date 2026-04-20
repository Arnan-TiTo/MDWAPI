using CHMBAPI.Data;
using CHMBAPI.Entities;

namespace CHMBAPI.Services;

public class AuditService
{
    private readonly AppDbContext _db;

    public AuditService(AppDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(string action, string description, string performedBy, long? memberId = null)
    {
        var audit = new AuditLog
        {
            Action = action,
            Description = description,
            PerformedBy = performedBy,
            MemberId = memberId,
            PerformedAt = DateTime.UtcNow,
            IpAddress = null, // Can be added later if needed
            UserAgent = null
        };

        _db.AuditLogs.Add(audit);
        await _db.SaveChangesAsync();
    }
}