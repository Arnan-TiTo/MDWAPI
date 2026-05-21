using CHMBAPI.Data;
using CHMBAPI.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CHMBAPI.Services;

public class AuditService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AuditService> _logger;

    public AuditService(AppDbContext db, ILogger<AuditService> logger)
    {
        _db = db;
        _logger = logger;
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

        try
        {
            _db.AuditLogs.Add(audit);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsMissingAuditTable(ex))
        {
            _db.Entry(audit).State = EntityState.Detached;
            _logger.LogWarning(ex, "Skipping audit log write because mbw.AuditLogs is missing.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _db.Entry(audit).State = EntityState.Detached;
            _logger.LogWarning(ex, "Skipping audit log write because audit logging failed.");
        }
    }

    private static bool IsMissingAuditTable(Exception ex)
        => ex.GetBaseException() is SqlException { Number: 208 };
}
