using MDWAPI.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MasterDataController : ControllerBase
{
    private readonly AppDbContext _context;
    public MasterDataController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("provinces")]
    public async Task<IActionResult> GetProvinces()
    {
        var provinces = await _context.ThailandAddresses
            .Select(x => x.province)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();
        return Ok(provinces);
    }

    [HttpGet("districts")]
    public async Task<IActionResult> GetDistricts([FromQuery] string province)
    {
        if (string.IsNullOrEmpty(province)) return BadRequest("Province is required.");

        var districts = await _context.ThailandAddresses
            .Where(x => x.province == province)
            .Select(x => x.district)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();
        return Ok(districts);
    }

    [HttpGet("subdistricts")]
    public async Task<IActionResult> GetSubdistricts([FromQuery] string district)
    {
        if (string.IsNullOrEmpty(district)) return BadRequest("District is required.");

        var subdistricts = await _context.ThailandAddresses
            .Where(x => x.district == district)
            .Select(x => new { x.subDistrict, x.postcode })
            .Distinct()
            .OrderBy(x => x.subDistrict)
            .ToListAsync();
        return Ok(subdistricts);
    }
}
