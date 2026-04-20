using CHMBAPI.Data;
using CHMBAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace CHMBAPI.Services;

public class PointPolicyEngine
{
    private readonly AppDbContext _db;

    public PointPolicyEngine(AppDbContext db)
    {
        _db = db;
    }

    public async Task<int> CalculatePointsAsync(string platformType, decimal orderAmount, string orderStatus)
    {
        // Get active policies for this platform
        var policies = await _db.PointPolicies
            .Where(p => p.IsActive &&
                       (p.PlatformType == platformType || p.PlatformType == "ALL") &&
                       p.EffectiveFrom <= DateTime.UtcNow &&
                       (p.EffectiveTo == null || p.EffectiveTo >= DateTime.UtcNow))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        foreach (var policy in policies)
        {
            // Check minimum order amount
            if (policy.MinOrderAmount.HasValue && orderAmount < policy.MinOrderAmount.Value)
                continue;

            // Check eligible statuses
            if (!string.IsNullOrWhiteSpace(policy.EligibleStatuses))
            {
                var eligibleStatuses = policy.EligibleStatuses.Split(',');
                if (!eligibleStatuses.Contains(orderStatus))
                    continue;
            }

            // Calculate points based on formula
            var points = CalculatePointsByFormula(policy.EarnFormula, orderAmount, policy.EarnRate);

            if (points > 0)
                return points;
        }

        return 0; // No points if no policy matches
    }

    private int CalculatePointsByFormula(string formula, decimal amount, decimal rate)
    {
        switch (formula.ToUpper())
        {
            case "AMOUNT_DIV_100":
                return (int)(amount / 100 * rate);

            case "AMOUNT_DIV_1000":
                return (int)(amount / 1000 * rate);

            case "FIXED_RATE":
                return (int)rate;

            case "PERCENTAGE":
                return (int)(amount * rate / 100);

            default:
                // Default to amount divided by 100
                return (int)(amount / 100 * rate);
        }
    }
}
