using MDWAPI.Data;
using MDWAPI.DTOs;
using MDWAPI.Entities;
using MDWAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace MDWAPI.Controllers;

/// <summary>API Admin สำหรับจัดการ Member, Mapping, Points, Rewards</summary>
[ApiController]
[Route("api/admin/member")]
[Tags("Admin-Member")]
[Authorize]
public class AdminMemberController : ControllerBase
{
    private readonly MemberService _memberService;
    private readonly MemberMappingService _mappingService;
    private readonly PointService _pointService;
    private readonly RewardService _rewardService;
    private readonly EarnProcessingService _earnProcessor;

    public AdminMemberController(
        MemberService memberService,
        MemberMappingService mappingService,
        PointService pointService,
        RewardService rewardService,
        EarnProcessingService earnProcessor)
    {
        _memberService = memberService;
        _mappingService = mappingService;
        _pointService = pointService;
        _rewardService = rewardService;
        _earnProcessor = earnProcessor;
    }

    private int GetUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    private string GetUsername() =>
        User.FindFirstValue(ClaimTypes.Name) ?? "admin";

    // ─── Members ──────────────────────────────────
    /// <summary>ค้นหาสมาชิก (paged)</summary>
    [HttpGet("search")]
    public async Task<IActionResult> SearchMembers(string? keyword, int page = 1, int pageSize = 20)
    {
        var result = await _memberService.SearchAsync(keyword, page, pageSize);
        return Ok(result);
    }

    /// <summary>ดู profile สมาชิก</summary>
    [HttpGet("{memberId:long}")]
    public async Task<IActionResult> GetProfile(long memberId)
    {
        try
        {
            var result = await _memberService.GetProfileAsync(memberId);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    /// <summary>สรุปข้อมูล member ทั้งหมด พร้อม earn stats (ค้นหาได้)</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> MemberSummary(string? keyword = null, int page = 1, int pageSize = 50)
    {
        var result = await _memberService.GetMemberSummaryWithStatsAsync(keyword, page, pageSize);
        return Ok(result);
    }

    /// <summary>Admin trigger คำนวณแต้มจากออเดอร์ทันที</summary>
    [HttpPost("trigger-earn")]
    public async Task<IActionResult> TriggerEarn()
    {
        var (linked, earned) = await _earnProcessor.ProcessPendingOrdersAsync();
        return Ok(new { linked, earned, message = $"จับคู่ {linked} ออเดอร์, คำนวณแต้ม {earned} รายการ" });
    }

    // ─── Mapping ──────────────────────────────────
    /// <summary>สร้างคำขอ mapping (admin สร้างให้)</summary>
    [HttpPost("mapping/request")]
    public async Task<IActionResult> CreateMappingRequest([FromBody] MappingRequestCreateDto dto)
    {
        try
        {
            dto.SourceType = "ADMIN";
            var result = await _mappingService.CreateRequestAsync(dto, GetUsername());
            return Ok(result);
        }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>ดู mapping requests ที่รออนุมัติ</summary>
    [HttpGet("mapping/pending")]
    public async Task<IActionResult> ListPendingMappings(int page = 1, int pageSize = 20)
    {
        var result = await _mappingService.ListPendingAsync(page, pageSize);
        return Ok(result);
    }

    /// <summary>ดู mapping requests ทั้งหมด (filter by status)</summary>
    [HttpGet("mapping/list")]
    public async Task<IActionResult> ListAllMappings(string? status = null, int page = 1, int pageSize = 20)
    {
        var result = await _mappingService.ListAllAsync(status, page, pageSize);
        return Ok(result);
    }

    /// <summary>ดูรายละเอียด mapping request</summary>
    [HttpGet("mapping/request/{requestId:long}")]
    public async Task<IActionResult> GetMappingRequest(long requestId)
    {
        try
        {
            var result = await _mappingService.GetRequestAsync(requestId);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    /// <summary>อนุมัติ mapping</summary>
    [HttpPost("mapping/request/{requestId:long}/approve")]
    public async Task<IActionResult> ApproveMappingRequest(long requestId, [FromBody] MappingApprovalDto dto)
    {
        try
        {
            var result = await _mappingService.ApproveAsync(requestId, dto, GetUserId(), GetUsername());
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>ปฏิเสธ mapping</summary>
    [HttpPost("mapping/request/{requestId:long}/reject")]
    public async Task<IActionResult> RejectMappingRequest(long requestId, [FromBody] MappingApprovalDto dto)
    {
        try
        {
            var result = await _mappingService.RejectAsync(requestId, dto, GetUserId(), GetUsername());
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>Bulk approve mapping requests</summary>
    [HttpPost("mapping/bulk-approve")]
    public async Task<IActionResult> BulkApprove([FromBody] BulkMappingActionDto dto)
    {
        var results = new List<object>();
        foreach (var id in dto.RequestIds)
        {
            try
            {
                await _mappingService.ApproveAsync(id, new MappingApprovalDto { ReviewNote = dto.ReviewNote }, GetUserId(), GetUsername());
                results.Add(new { RequestId = id, Success = true });
            }
            catch (Exception ex) { results.Add(new { RequestId = id, Success = false, Error = ex.Message }); }
        }
        return Ok(results);
    }

    /// <summary>Bulk reject mapping requests</summary>
    [HttpPost("mapping/bulk-reject")]
    public async Task<IActionResult> BulkReject([FromBody] BulkMappingActionDto dto)
    {
        var results = new List<object>();
        foreach (var id in dto.RequestIds)
        {
            try
            {
                await _mappingService.RejectAsync(id, new MappingApprovalDto { ReviewNote = dto.ReviewNote }, GetUserId(), GetUsername());
                results.Add(new { RequestId = id, Success = true });
            }
            catch (Exception ex) { results.Add(new { RequestId = id, Success = false, Error = ex.Message }); }
        }
        return Ok(results);
    }

    // ─── Points ───────────────────────────────────
    /// <summary>ดูยอดแต้มของ member</summary>
    [HttpGet("{memberId:long}/points")]
    public async Task<IActionResult> GetPointBalance(long memberId)
    {
        var result = await _pointService.GetBalanceAsync(memberId);
        return Ok(result);
    }

    /// <summary>ดูประวัติแต้ม</summary>
    [HttpGet("{memberId:long}/points/history")]
    public async Task<IActionResult> GetPointHistory(long memberId, int page = 1, int pageSize = 20)
    {
        var result = await _pointService.GetHistoryAsync(memberId, page, pageSize);
        return Ok(result);
    }

    /// <summary>ปรับแต้มด้วยมือ</summary>
    [HttpPost("points/adjust")]
    public async Task<IActionResult> AdjustPoints([FromBody] PointAdjustRequest req)
    {
        try
        {
            var result = await _pointService.AdjustAsync(req, GetUserId(), GetUsername());
            return Ok(new { adjustmentId = result.AdjustmentId, message = "Points adjusted successfully" });
        }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ─── Platform Direct Link ─────────────────────
    /// <summary>Admin ผูก platform account ให้ member โดยตรง (สร้าง + auto-approve)</summary>
    [HttpPost("{memberId:long}/platform-link")]
    public async Task<IActionResult> DirectPlatformLink(long memberId, [FromBody] AdminDirectLinkDto dto)
    {
        try
        {
            var result = await _mappingService.AdminDirectLinkAsync(
                memberId, dto.PlatformType, dto.PlatformAccountKey,
                dto.PlatformAccountName, dto.ShopId,
                GetUserId(), GetUsername());
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>Admin ลบ platform account ของ member</summary>
    [HttpDelete("{memberId:long}/platform-link/{platformAccountId:long}")]
    public async Task<IActionResult> RemovePlatformLink(long memberId, long platformAccountId)
    {
        try
        {
            await _mappingService.RemovePlatformLinkAsync(memberId, platformAccountId, GetUserId(), GetUsername());
            return Ok(new { message = "Platform link removed" });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    // ─── Redemptions ──────────────────────────────
    /// <summary>ดูรายการแลกรางวัล</summary>
    [HttpGet("redemptions")]
    public async Task<IActionResult> ListRedemptions(string? status = null, int page = 1, int pageSize = 20)
    {
        var result = await _rewardService.ListRedemptionsAsync(status, page, pageSize);
        return Ok(result);
    }

    /// <summary>ยกเลิกการแลกรางวัล</summary>
    [HttpPost("redemptions/{redemptionId:long}/cancel")]
    public async Task<IActionResult> CancelRedemption(long redemptionId, [FromBody] CancelRedemptionDto? dto = null)
    {
        try
        {
            await _rewardService.CancelRedemptionAsync(redemptionId, GetUsername(), dto?.Reason);
            return Ok(new { message = "Redemption cancelled" });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ─── Point Policies ──────────────────────────
    /// <summary>ดู point policies ทั้งหมด</summary>
    [HttpGet("~/api/admin/point-policies")]
    public async Task<IActionResult> ListPolicies()
    {
        var policies = await _pointService.ListPoliciesAsync();
        return Ok(policies);
    }

    /// <summary>สร้าง point policy ใหม่</summary>
    [HttpPost("~/api/admin/point-policies")]
    public async Task<IActionResult> CreatePolicy([FromBody] PointPolicyCreateDto dto)
    {
        try
        {
            var result = await _pointService.CreatePolicyAsync(dto, GetUsername());
            return Ok(result);
        }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>แก้ไข point policy</summary>
    [HttpPut("~/api/admin/point-policies/{policyId:int}")]
    public async Task<IActionResult> UpdatePolicy(int policyId, [FromBody] PointPolicyCreateDto dto)
    {
        try
        {
            var result = await _pointService.UpdatePolicyAsync(policyId, dto, GetUsername());
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>เปิด/ปิด policy</summary>
    [HttpPatch("~/api/admin/point-policies/{policyId:int}/toggle")]
    public async Task<IActionResult> TogglePolicy(int policyId)
    {
        try
        {
            var result = await _pointService.TogglePolicyAsync(policyId);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }
}

// ─── New DTOs ──
public class BulkMappingActionDto
{
    public List<long> RequestIds { get; set; } = new();
    public string? ReviewNote { get; set; }
}

public class CancelRedemptionDto
{
    public string? Reason { get; set; }
}

public class AdminDirectLinkDto
{
    public string PlatformType { get; set; } = "";
    public string PlatformAccountKey { get; set; } = "";
    public string? PlatformAccountName { get; set; }
    public long? ShopId { get; set; }
}

public class PointPolicyCreateDto
{
    public string PolicyName { get; set; } = "";
    public string PlatformType { get; set; } = "ALL";
    public string EarnFormula { get; set; } = "AMOUNT_DIV_100";
    public decimal EarnRate { get; set; } = 1.0m;
    public decimal? MinOrderAmount { get; set; }
    public string? EligibleStatuses { get; set; }
    public int? ExpiryDays { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
}
