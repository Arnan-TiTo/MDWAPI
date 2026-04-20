using CHMBAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace CHMBAPI.Controllers;

[ApiController]
[Route("api/content")]
[Tags("Content")]
public class ContentController : ControllerBase
{
    private readonly ContentService _contentService;

    public ContentController(ContentService contentService)
    {
        _contentService = contentService;
    }

    /// <summary>ดึงเอกสารล่าสุด (TERMS, PRIVACY, POLICY)</summary>
    [HttpGet("documents/{type}")]
    public async Task<IActionResult> GetDocument(string type, string lang = "th")
    {
        var result = await _contentService.GetLatestDocumentAsync(type, lang);
        if (result == null) return NotFound(new { error = "Document not found" });
        return Ok(result);
    }
}
