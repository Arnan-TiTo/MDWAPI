using MDWAPI.Data;
using MDWAPI.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Controllers;

/// <summary>API Admin สำหรับจัดการ Earn Formulas</summary>
[ApiController]
[Route("api/admin/earn-formulas")]
[Tags("Admin-EarnFormulas")]
[Authorize]
public class AdminEarnFormulaController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminEarnFormulaController(AppDbContext db) => _db = db;

    /// <summary>รายการสูตรคำนวณ</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await _db.EarnFormulas
            .OrderBy(f => f.FormulaCode)
            .ToListAsync();
        return Ok(list);
    }

    /// <summary>สร้างสูตรใหม่</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] EarnFormulaCreateDto dto)
    {
        if (await _db.EarnFormulas.AnyAsync(f => f.FormulaCode == dto.FormulaCode))
            return BadRequest(new { error = $"สูตร '{dto.FormulaCode}' มีอยู่แล้ว" });

        var entity = new EarnFormula
        {
            FormulaCode = dto.FormulaCode,
            FormulaName = dto.FormulaName,
            Expression = dto.Expression,
            Description = dto.Description,
            Variables = dto.Variables,
            IsSystem = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.EarnFormulas.Add(entity);
        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    /// <summary>แก้ไขสูตร</summary>
    [HttpPut("{formulaId}")]
    public async Task<IActionResult> Update(int formulaId, [FromBody] EarnFormulaCreateDto dto)
    {
        var entity = await _db.EarnFormulas.FindAsync(formulaId);
        if (entity == null) return NotFound();
        if (entity.IsSystem) return BadRequest(new { error = "ไม่สามารถแก้ไขสูตร System ได้" });

        entity.FormulaCode = dto.FormulaCode;
        entity.FormulaName = dto.FormulaName;
        entity.Expression = dto.Expression;
        entity.Description = dto.Description;
        entity.Variables = dto.Variables;
        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    /// <summary>ลบสูตร</summary>
    [HttpDelete("{formulaId}")]
    public async Task<IActionResult> Delete(int formulaId)
    {
        var entity = await _db.EarnFormulas.FindAsync(formulaId);
        if (entity == null) return NotFound();
        if (entity.IsSystem) return BadRequest(new { error = "ไม่สามารถลบสูตร System ได้" });

        _db.EarnFormulas.Remove(entity);
        await _db.SaveChangesAsync();
        return Ok();
    }
}

public class EarnFormulaCreateDto
{
    public string FormulaCode { get; set; } = "";
    public string FormulaName { get; set; } = "";
    public string Expression { get; set; } = "";
    public string? Description { get; set; }
    public string? Variables { get; set; }
}
