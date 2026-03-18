using MDWAPI.DTOs;
using MDWAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace MDWAPI.Controllers;

/// <summary>API สำหรับ Member (LINE Mini App / Frontend)</summary>
[ApiController]
[Route("api/member")]
[Tags("Member")]
public class MemberController : ControllerBase
{
    private readonly MemberService _memberService;
    private readonly PointService _pointService;
    private readonly RewardService _rewardService;

    public MemberController(MemberService memberService, PointService pointService, RewardService rewardService)
    {
        _memberService = memberService;
        _pointService = pointService;
        _rewardService = rewardService;
    }

    /// <summary>สมัครสมาชิก</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] MemberRegisterRequest req)
    {
        try
        {
            var result = await _memberService.RegisterAsync(req);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>ดู profile จาก MemberId</summary>
    [HttpGet("{memberId:long}")]
    public async Task<IActionResult> GetProfile(long memberId)
    {
        try
        {
            var result = await _memberService.GetProfileAsync(memberId);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>แก้ไขข้อมูลส่วนตัว</summary>
    [HttpPut("{memberId:long}/profile")]
    public async Task<IActionResult> UpdateProfile(long memberId, [FromBody] MemberUpdateProfileRequest req)
    {
        try
        {
            var result = await _memberService.UpdateProfileAsync(memberId, req);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>ดู profile จาก LINE userId</summary>
    [HttpGet("by-line/{lineUserId}")]
    public async Task<IActionResult> GetByLineUserId(string lineUserId)
    {
        var result = await _memberService.GetByLineUserIdAsync(lineUserId);
        if (result == null) return NotFound(new { error = "Member not found for this LINE user" });
        return Ok(result);
    }

    /// <summary>ดูยอดแต้ม</summary>
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

    /// <summary>ดูแต้มที่จะหมดอายุ (batch list)</summary>
    [HttpGet("{memberId:long}/points/expiring")]
    public async Task<IActionResult> GetExpiringPoints(long memberId)
    {
        var result = await _pointService.GetExpiringPointsAsync(memberId);
        return Ok(result);
    }

    /// <summary>ดู rewards ที่แลกได้</summary>
    [HttpGet("rewards")]
    public async Task<IActionResult> ListRewards()
    {
        var result = await _rewardService.ListActiveAsync();
        return Ok(result);
    }

    /// <summary>แลก reward</summary>
    [HttpPost("rewards/redeem")]
    public async Task<IActionResult> RedeemReward([FromBody] RedeemRequestDto req)
    {
        try
        {
            var result = await _rewardService.RedeemAsync(req);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>ส่ง request ผูก platform account</summary>
    [HttpPost("{memberId:long}/platform-link")]
    public async Task<IActionResult> SubmitPlatformLink(long memberId, [FromBody] MemberPlatformLinkRequest req)
    {
        try
        {
            var result = await _memberService.SubmitPlatformLinkAsync(memberId, req);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>ดู platform requests ของตัวเอง</summary>
    [HttpGet("{memberId:long}/platform-requests")]
    public async Task<IActionResult> GetPlatformRequests(long memberId)
    {
        var result = await _memberService.GetPlatformRequestsAsync(memberId);
        return Ok(result);
    }

    /// <summary>ดูประวัติแลกรางวัล + โค้ดที่ได้</summary>
    [HttpGet("{memberId:long}/redemptions")]
    public async Task<IActionResult> GetRedemptions(long memberId, int page = 1, int pageSize = 20)
    {
        var result = await _rewardService.GetMemberRedemptionsAsync(memberId, page, pageSize);
        return Ok(result);
    }
}
