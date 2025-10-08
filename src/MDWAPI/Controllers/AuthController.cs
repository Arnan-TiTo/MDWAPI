using MDWAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MDWAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly TokenService _tokens;
    public AuthController(TokenService tokens) => _tokens = tokens;

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var token = await _tokens.IssueAsync(req.Username, req.Password, TimeSpan.FromHours(999));
        return Ok(new { token, expires_in_hours = 999 });
    }

    [Authorize]
    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke()
    {
        var auth = Request.Headers["Authorization"].ToString();
        var token = auth.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(token)) return BadRequest();
        await _tokens.RevokeAsync(token);
        return Ok(new { revoked = true });
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me() => Ok(new { user = User.Identity?.Name, role = User.IsInRole("admin") ? "admin" : "operator" });
}

public record LoginRequest(string Username, string Password);