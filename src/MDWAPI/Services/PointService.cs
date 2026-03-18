using MDWAPI.Data;
using MDWAPI.DTOs;
using MDWAPI.Entities;
using MDWAPI.Controllers;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Services;

/// <summary>คำนวณ earn points ตาม PointPolicy + EarnFormulas จาก DB</summary>
public class PointPolicyEngine
{
    private readonly AppDbContext _db;
    private Dictionary<string, EarnFormula>? _formulaCache;

    public PointPolicyEngine(AppDbContext db) => _db = db;

    /// <summary>คำนวณแต้มจากยอดซื้อ ตาม policy ที่ effective ในเวลานั้น</summary>
    public async Task<(int points, int? policyId)> CalculateEarnAsync(
        string platformType, decimal orderAmount, DateTime orderDate)
    {
        var orderDateOnly = orderDate.Date;
        var platformUpper = platformType.ToUpper();

        var policy = await _db.PointPolicies
            .Where(p => p.IsActive
                && (p.PlatformType == "ALL" || p.PlatformType == platformUpper)
                && p.EffectiveFrom.Date <= orderDateOnly
                && (p.EffectiveTo == null || p.EffectiveTo.Value.Date >= orderDateOnly))
            .OrderByDescending(p => p.PlatformType == platformUpper ? 1 : 0)
            .ThenByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync();

        if (policy == null) return (0, null);
        if (policy.MinOrderAmount.HasValue && orderAmount < policy.MinOrderAmount.Value) return (0, policy.PolicyId);

        // Lookup formula จาก EarnFormulas table
        var formula = await GetFormulaAsync(policy.EarnFormula);

        int points;
        if (formula != null)
        {
            // ใช้ expression จาก DB: เช่น "Amount / 100 * Rate"
            points = EvaluateExpression(formula.Expression, orderAmount, policy.EarnRate);
        }
        else
        {
            // Fallback: ถ้าไม่เจอ formula ใน DB ใช้ logic เดิม
            points = policy.EarnFormula switch
            {
                "FIXED" => (int)policy.EarnRate,
                _ => (int)Math.Floor(orderAmount / 100m * policy.EarnRate)
            };
        }

        return (points, policy.PolicyId);
    }

    /// <summary>ดึง formula จาก cache หรือ DB</summary>
    private async Task<EarnFormula?> GetFormulaAsync(string formulaCode)
    {
        // Load cache ครั้งแรก
        if (_formulaCache == null)
        {
            var all = await _db.EarnFormulas.ToListAsync();
            _formulaCache = all.ToDictionary(f => f.FormulaCode, StringComparer.OrdinalIgnoreCase);
        }
        return _formulaCache.GetValueOrDefault(formulaCode);
    }

    /// <summary>ประมวลผล expression เช่น "Amount / 100 * Rate"</summary>
    private static int EvaluateExpression(string expression, decimal amount, decimal rate)
    {
        try
        {
            // Variables ที่รองรับ
            var vars = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["Amount"] = amount,
                ["Rate"] = rate,
                ["Quantity"] = 1m  // default
            };

            // Tokenize + evaluate (simple math expression parser)
            var result = SimpleExpressionEval(expression, vars);
            return (int)Math.Floor(result);
        }
        catch
        {
            // ถ้า parse ไม่ได้ fallback เป็น default
            return (int)Math.Floor(amount / 100m * rate);
        }
    }

    /// <summary>Simple math expression evaluator รองรับ +, -, *, / กับ variables</summary>
    private static decimal SimpleExpressionEval(string expr, Dictionary<string, decimal> vars)
    {
        // แทนค่าตัวแปรทั้งหมด
        foreach (var (name, value) in vars)
        {
            expr = System.Text.RegularExpressions.Regex.Replace(
                expr, @"\b" + name + @"\b", value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        // Parse expression: รองรับ +, -, *, / (ลำดับความสำคัญถูกต้อง)
        return ParseAddSub(expr.AsSpan().Trim());
    }

    private static decimal ParseAddSub(ReadOnlySpan<char> expr)
    {
        // หา + หรือ - ตัวสุดท้าย (ที่ไม่ได้อยู่ในวงเล็บ)
        int depth = 0;
        int lastOp = -1;
        for (int i = expr.Length - 1; i >= 0; i--)
        {
            if (expr[i] == ')') depth++;
            else if (expr[i] == '(') depth--;
            else if (depth == 0 && (expr[i] == '+' || expr[i] == '-') && i > 0)
            {
                lastOp = i;
                break;
            }
        }
        if (lastOp > 0)
        {
            var left = ParseAddSub(expr[..lastOp].Trim());
            var right = ParseMulDiv(expr[(lastOp + 1)..].Trim());
            return expr[lastOp] == '+' ? left + right : left - right;
        }
        return ParseMulDiv(expr);
    }

    private static decimal ParseMulDiv(ReadOnlySpan<char> expr)
    {
        int depth = 0;
        int lastOp = -1;
        for (int i = expr.Length - 1; i >= 0; i--)
        {
            if (expr[i] == ')') depth++;
            else if (expr[i] == '(') depth--;
            else if (depth == 0 && (expr[i] == '*' || expr[i] == '/'))
            {
                lastOp = i;
                break;
            }
        }
        if (lastOp >= 0)
        {
            var left = ParseMulDiv(expr[..lastOp].Trim());
            var right = ParseAtom(expr[(lastOp + 1)..].Trim());
            return expr[lastOp] == '*' ? left * right : (right != 0 ? left / right : 0);
        }
        return ParseAtom(expr);
    }

    private static decimal ParseAtom(ReadOnlySpan<char> expr)
    {
        // วงเล็บ
        if (expr.Length > 2 && expr[0] == '(' && expr[^1] == ')')
            return ParseAddSub(expr[1..^1].Trim());

        // ตัวเลข
        if (decimal.TryParse(expr, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var num))
            return num;

        return 0;
    }
}


/// <summary>จัดการ point transaction ทั้งหมด</summary>
public class PointService
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public PointService(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>ดูยอดคงเหลือ</summary>
    public async Task<PointBalanceDto> GetBalanceAsync(long memberId)
    {
        var pa = await _db.PointAccounts.FirstOrDefaultAsync(x => x.MemberId == memberId);
        if (pa == null)
            return new PointBalanceDto();

        // คำนวณแต้มที่จะหมดอายุใน 30 วัน
        var expiringSoon = await _db.PointExpirations
            .Where(e => e.MemberId == memberId && e.Status == "Active" && e.RemainingPoints > 0
                && e.ExpiresAt <= DateTime.UtcNow.AddDays(30))
            .SumAsync(e => e.RemainingPoints);

        var nextExpiry = await _db.PointExpirations
            .Where(e => e.MemberId == memberId && e.Status == "Active" && e.RemainingPoints > 0)
            .OrderBy(e => e.ExpiresAt)
            .Select(e => (DateTime?)e.ExpiresAt)
            .FirstOrDefaultAsync();

        return new PointBalanceDto
        {
            AvailablePoints = pa.AvailablePoints,
            ReservedPoints = pa.ReservedPoints,
            TotalEarned = pa.TotalEarned,
            TotalBurned = pa.TotalBurned,
            TotalExpired = pa.TotalExpired,
            LastActivityAt = pa.LastActivityAt,
            ExpiringPoints = expiringSoon,
            NextExpiryDate = nextExpiry
        };
    }

    /// <summary>ดูประวัติ transaction</summary>
    public async Task<List<PointHistoryDto>> GetHistoryAsync(long memberId, int page = 1, int pageSize = 20)
    {
        return await _db.PointLedger
            .Where(l => l.MemberId == memberId)
            .OrderByDescending(l => l.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new PointHistoryDto
            {
                LedgerId = l.LedgerId,
                TxnType = l.TxnType,
                Points = l.Points,
                BalanceAfter = l.BalanceAfter,
                RefType = l.RefType,
                RefId = l.RefId,
                OccurredAt = l.OccurredAt,
                CreatedBy = l.CreatedBy
            })
            .ToListAsync();
    }

    /// <summary>Earn points จาก order</summary>
    public async Task<PointLedgerEntry?> EarnAsync(long memberId, int points, int? policyId,
        string refId, string? createdBy = null)
    {
        if (points <= 0) return null;

        var idempotencyKey = $"EARN-ORDER-{refId}";
        if (await _db.PointLedger.AnyAsync(l => l.IdempotencyKey == idempotencyKey))
            return null; // ลงซ้ำแล้ว

        var account = await GetOrCreateAccountAsync(memberId);
        account.AvailablePoints += points;
        account.TotalEarned += points;
        account.LastActivityAt = DateTime.UtcNow;
        account.UpdatedAt = DateTime.UtcNow;

        var entry = new PointLedgerEntry
        {
            MemberId = memberId,
            TxnType = "EARN",
            Points = points,
            BalanceAfter = account.AvailablePoints,
            PolicyId = policyId,
            RefType = "ORDER",
            RefId = refId,
            OccurredAt = DateTime.UtcNow,
            CreatedBy = createdBy ?? "SYSTEM",
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTime.UtcNow
        };

        _db.PointLedger.Add(entry);
        await _db.SaveChangesAsync();

        // ─── Point Expiry: สร้าง PointExpiration ถ้า policy มี ExpiryDays ───
        if (policyId.HasValue)
        {
            var policy = await _db.PointPolicies.FindAsync(policyId.Value);
            if (policy?.ExpiryDays != null && policy.ExpiryDays > 0)
            {
                _db.PointExpirations.Add(new PointExpiration
                {
                    MemberId = memberId,
                    SourceLedgerId = entry.LedgerId,
                    OriginalPoints = points,
                    RemainingPoints = points,
                    ExpiresAt = DateTime.UtcNow.AddDays(policy.ExpiryDays.Value),
                    Status = "Active"
                });
                await _db.SaveChangesAsync();
            }
        }

        return entry;
    }

    /// <summary>หักแต้มคืนเมื่อ order ถูก Return/Refund</summary>
    public async Task<PointLedgerEntry?> EarnReversalAsync(long memberId, string refId, string? createdBy = null)
    {
        var reversalKey = $"EARN_REVERSAL-ORDER-{refId}";
        if (await _db.PointLedger.AnyAsync(l => l.IdempotencyKey == reversalKey))
            return null;

        var earnKey = $"EARN-ORDER-{refId}";
        var originalEarn = await _db.PointLedger
            .FirstOrDefaultAsync(l => l.IdempotencyKey == earnKey && l.TxnType == "EARN");

        if (originalEarn == null) return null;

        var pointsToReverse = originalEarn.Points;

        var account = await GetOrCreateAccountAsync(memberId);
        account.AvailablePoints -= pointsToReverse;
        account.TotalEarned -= pointsToReverse;
        account.LastActivityAt = DateTime.UtcNow;
        account.UpdatedAt = DateTime.UtcNow;

        var entry = new PointLedgerEntry
        {
            MemberId = memberId,
            TxnType = "EARN_REVERSAL",
            Points = -pointsToReverse,
            BalanceAfter = account.AvailablePoints,
            PolicyId = originalEarn.PolicyId,
            RefType = "ORDER",
            RefId = refId,
            OccurredAt = DateTime.UtcNow,
            CreatedBy = createdBy ?? "SYSTEM",
            IdempotencyKey = reversalKey,
            CreatedAt = DateTime.UtcNow
        };

        _db.PointLedger.Add(entry);

        // ─── Point Expiry: ลบ expiration ของ earn ที่ถูก reverse ───
        var expiration = await _db.PointExpirations
            .FirstOrDefaultAsync(e => e.SourceLedgerId == originalEarn.LedgerId && e.Status == "Active");
        if (expiration != null)
        {
            expiration.RemainingPoints = 0;
            expiration.Status = "Reversed";
        }

        await _db.SaveChangesAsync();
        return entry;
    }

    /// <summary>Reserve points (ก่อน burn) — ใช้แต้มที่ใกล้หมดอายุก่อน (FIFO)</summary>
    public async Task<PointLedgerEntry> ReserveAsync(long memberId, int points, string refId)
    {
        var account = await GetOrCreateAccountAsync(memberId);
        if (account.AvailablePoints < points)
            throw new InvalidOperationException($"Insufficient points. Available: {account.AvailablePoints}, Requested: {points}");

        account.AvailablePoints -= points;
        account.ReservedPoints += points;
        account.LastActivityAt = DateTime.UtcNow;
        account.UpdatedAt = DateTime.UtcNow;

        // ─── FIFO: ตัดแต้มที่ใกล้หมดอายุก่อน ───
        await ConsumeFifoAsync(memberId, points);

        var entry = new PointLedgerEntry
        {
            MemberId = memberId,
            TxnType = "RESERVE",
            Points = -points,
            BalanceAfter = account.AvailablePoints,
            RefType = "REDEMPTION",
            RefId = refId,
            OccurredAt = DateTime.UtcNow,
            CreatedBy = "SYSTEM",
            IdempotencyKey = $"RESERVE-{refId}",
            CreatedAt = DateTime.UtcNow
        };

        _db.PointLedger.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    /// <summary>Burn reserved points (หลังจ่าย code สำเร็จ)</summary>
    public async Task<PointLedgerEntry> BurnAsync(long memberId, int points, string refId)
    {
        var account = await GetOrCreateAccountAsync(memberId);
        account.ReservedPoints -= points;
        account.TotalBurned += points;
        account.LastActivityAt = DateTime.UtcNow;
        account.UpdatedAt = DateTime.UtcNow;

        var entry = new PointLedgerEntry
        {
            MemberId = memberId,
            TxnType = "BURN",
            Points = -points,
            BalanceAfter = account.AvailablePoints,
            RefType = "REDEMPTION",
            RefId = refId,
            OccurredAt = DateTime.UtcNow,
            CreatedBy = "SYSTEM",
            IdempotencyKey = $"BURN-{refId}",
            CreatedAt = DateTime.UtcNow
        };

        _db.PointLedger.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    /// <summary>Release reserved points (cancel) — คืนแต้มกลับเข้า expiration (reverse FIFO)</summary>
    public async Task ReleaseAsync(long memberId, int points, string refId)
    {
        var account = await GetOrCreateAccountAsync(memberId);
        account.ReservedPoints -= points;
        account.AvailablePoints += points;
        account.LastActivityAt = DateTime.UtcNow;
        account.UpdatedAt = DateTime.UtcNow;

        // คืนแต้มกลับเข้า expiration (newest first — reverse FIFO)
        await RestoreFifoAsync(memberId, points);

        _db.PointLedger.Add(new PointLedgerEntry
        {
            MemberId = memberId,
            TxnType = "RELEASE",
            Points = points,
            BalanceAfter = account.AvailablePoints,
            RefType = "REDEMPTION",
            RefId = refId,
            OccurredAt = DateTime.UtcNow,
            CreatedBy = "SYSTEM",
            IdempotencyKey = $"RELEASE-{refId}",
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
    }

    /// <summary>ปรับแต้มด้วยมือ (admin)</summary>
    public async Task<PointAdjustment> AdjustAsync(PointAdjustRequest req, int adminUserId, string adminUsername)
    {
        var account = await GetOrCreateAccountAsync(req.MemberId);
        int delta = req.AdjustType == "ADD" ? req.Points : -req.Points;
        account.AvailablePoints += delta;
        if (req.AdjustType == "ADD") account.TotalEarned += req.Points;
        else account.TotalBurned += req.Points;
        account.LastActivityAt = DateTime.UtcNow;
        account.UpdatedAt = DateTime.UtcNow;

        var entry = new PointLedgerEntry
        {
            MemberId = req.MemberId,
            TxnType = "ADJUST",
            Points = delta,
            BalanceAfter = account.AvailablePoints,
            RefType = "ADJUSTMENT",
            OccurredAt = DateTime.UtcNow,
            CreatedBy = adminUsername,
            CreatedAt = DateTime.UtcNow
        };
        _db.PointLedger.Add(entry);
        await _db.SaveChangesAsync();

        var adj = new PointAdjustment
        {
            MemberId = req.MemberId,
            AdjustType = req.AdjustType,
            Points = req.Points,
            Reason = req.Reason,
            ApprovedBy = adminUsername,
            ApprovedAt = DateTime.UtcNow,
            LedgerId = entry.LedgerId,
            CreatedBy = adminUsername,
            CreatedAt = DateTime.UtcNow
        };
        _db.PointAdjustments.Add(adj);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(adminUserId, "ADJUST_POINTS", "PointLedger", entry.LedgerId.ToString(),
            newValue: $"{req.AdjustType} {req.Points} pts: {req.Reason}");

        return adj;
    }

    private async Task<PointAccount> GetOrCreateAccountAsync(long memberId)
    {
        var account = await _db.PointAccounts.FirstOrDefaultAsync(x => x.MemberId == memberId);
        if (account == null)
        {
            account = new PointAccount { MemberId = memberId, UpdatedAt = DateTime.UtcNow };
            _db.PointAccounts.Add(account);
            await _db.SaveChangesAsync();
        }
        return account;
    }

    // ═══ FIFO Point Expiry helpers ═══════════════════════

    /// <summary>ตัดแต้มที่ใกล้หมดอายุก่อน (FIFO)</summary>
    private async Task ConsumeFifoAsync(long memberId, int points)
    {
        var expirations = await _db.PointExpirations
            .Where(e => e.MemberId == memberId && e.Status == "Active" && e.RemainingPoints > 0)
            .OrderBy(e => e.ExpiresAt)
            .ToListAsync();

        int remaining = points;
        foreach (var exp in expirations)
        {
            if (remaining <= 0) break;
            int consume = Math.Min(remaining, exp.RemainingPoints);
            exp.RemainingPoints -= consume;
            remaining -= consume;
        }
    }

    /// <summary>คืนแต้มกลับเข้า expiration (reverse FIFO — newest first)</summary>
    private async Task RestoreFifoAsync(long memberId, int points)
    {
        // คืนแต้มกลับ: เริ่มจาก batch ที่หมดอายุช้าสุด (เพราะถูกตัดไปหลังสุด)
        var expirations = await _db.PointExpirations
            .Where(e => e.MemberId == memberId && e.Status == "Active"
                && e.RemainingPoints < e.OriginalPoints) // ถูกตัดไปบ้างแล้ว
            .OrderByDescending(e => e.ExpiresAt)
            .ToListAsync();

        int remaining = points;
        foreach (var exp in expirations)
        {
            if (remaining <= 0) break;
            int canRestore = exp.OriginalPoints - exp.RemainingPoints;
            int restore = Math.Min(remaining, canRestore);
            exp.RemainingPoints += restore;
            remaining -= restore;
        }
    }

    /// <summary>ดูแต้มที่จะหมดอายุเร็วๆ นี้</summary>
    public async Task<List<PointExpirationDto>> GetExpiringPointsAsync(long memberId)
    {
        return await _db.PointExpirations
            .Where(e => e.MemberId == memberId && e.Status == "Active" && e.RemainingPoints > 0)
            .OrderBy(e => e.ExpiresAt)
            .Select(e => new PointExpirationDto
            {
                ExpirationId = e.ExpirationId,
                OriginalPoints = e.OriginalPoints,
                RemainingPoints = e.RemainingPoints,
                ExpiresAt = e.ExpiresAt
            })
            .ToListAsync();
    }

    // ─── Policy CRUD ──────────────────────────────
    public async Task<List<PointPolicyDto>> ListPoliciesAsync()
    {
        return await _db.PointPolicies
            .OrderByDescending(p => p.IsActive)
            .ThenByDescending(p => p.EffectiveFrom)
            .Select(p => MapPolicyDto(p))
            .ToListAsync();
    }

    public async Task<PointPolicyDto> CreatePolicyAsync(PointPolicyCreateDto dto, string createdBy)
    {
        var policy = new PointPolicy
        {
            PolicyName = dto.PolicyName,
            PlatformType = dto.PlatformType.ToUpper(),
            EarnFormula = dto.EarnFormula,
            EarnRate = dto.EarnRate,
            MinOrderAmount = dto.MinOrderAmount,
            EligibleStatuses = dto.EligibleStatuses,
            ExpiryDays = dto.ExpiryDays,
            EffectiveFrom = dto.EffectiveFrom,
            EffectiveTo = dto.EffectiveTo,
            IsActive = true,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };
        _db.PointPolicies.Add(policy);
        await _db.SaveChangesAsync();
        return MapPolicyDto(policy);
    }

    public async Task<PointPolicyDto> UpdatePolicyAsync(int policyId, PointPolicyCreateDto dto, string updatedBy)
    {
        var policy = await _db.PointPolicies.FindAsync(policyId)
            ?? throw new KeyNotFoundException($"Policy {policyId} not found");

        policy.PolicyName = dto.PolicyName;
        policy.PlatformType = dto.PlatformType.ToUpper();
        policy.EarnFormula = dto.EarnFormula;
        policy.EarnRate = dto.EarnRate;
        policy.MinOrderAmount = dto.MinOrderAmount;
        policy.EligibleStatuses = dto.EligibleStatuses;
        policy.ExpiryDays = dto.ExpiryDays;
        policy.EffectiveFrom = dto.EffectiveFrom;
        policy.EffectiveTo = dto.EffectiveTo;
        await _db.SaveChangesAsync();
        return MapPolicyDto(policy);
    }

    public async Task<PointPolicyDto> TogglePolicyAsync(int policyId)
    {
        var policy = await _db.PointPolicies.FindAsync(policyId)
            ?? throw new KeyNotFoundException($"Policy {policyId} not found");
        policy.IsActive = !policy.IsActive;
        await _db.SaveChangesAsync();
        return MapPolicyDto(policy);
    }

    private static PointPolicyDto MapPolicyDto(PointPolicy p) => new()
    {
        PolicyId = p.PolicyId,
        PolicyName = p.PolicyName,
        PlatformType = p.PlatformType,
        EarnFormula = p.EarnFormula,
        EarnRate = p.EarnRate,
        MinOrderAmount = p.MinOrderAmount,
        EligibleStatuses = p.EligibleStatuses,
        ExpiryDays = p.ExpiryDays,
        EffectiveFrom = p.EffectiveFrom,
        EffectiveTo = p.EffectiveTo,
        IsActive = p.IsActive,
        CreatedBy = p.CreatedBy,
        CreatedAt = p.CreatedAt
    };
}

public class PointPolicyDto
{
    public int PolicyId { get; set; }
    public string PolicyName { get; set; } = "";
    public string PlatformType { get; set; } = "ALL";
    public string EarnFormula { get; set; } = "";
    public decimal EarnRate { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public string? EligibleStatuses { get; set; }
    public int? ExpiryDays { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
