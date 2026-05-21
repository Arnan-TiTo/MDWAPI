using MDWAPI.Data;
using MDWAPI.DTOs;
using MDWAPI.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/jobs")]
public class JobController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<JobController> _logger;

    public JobController(AppDbContext db, ILogger<JobController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet("config")]
    public async Task<ActionResult<List<JobConfigDto>>> GetConfigs()
    {
        var jobs = await _db.Misc
            .Where(m => m.Type == "MarketJob" && m.IsActive)
            .OrderBy(m => m.Id)
            .Select(m => new JobConfigDto
            {
                Id = m.Id,
                Name = m.Name,
                Type = m.Type,
                Schedule = m.Value1,
                Url = m.Value2,
                Query = m.Value3,
                Behavior = m.Value4,
                Watermark = m.Value5,
                UpdatedAt = m.UpdatedAt
            })
            .ToListAsync();

        return Ok(jobs);
    }

    [HttpPost("logs")]
    public async Task<ActionResult> CreateLog([FromBody] JobLogDto dto)
    {
        try
        {
            _db.JobLogs.Add(new JobLog
            {
                RunId = dto.RunId,
                Category = dto.Category,
                Phase = dto.Phase,
                Step = dto.Step,
                Level = dto.Level,
                Message = dto.Message,
                JobId = dto.JobId,
                JobName = dto.JobName,
                HttpStatus = dto.HttpStatus,
                DurationMs = dto.DurationMs,
                MetaJson = dto.MetaJson
            });
            await _db.SaveChangesAsync();
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write job log");
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("{id}/state")]
    public async Task<ActionResult> UpdateState(long id, [FromBody] JobStateUpdateDto dto)
    {
        try
        {
            var job = await _db.Misc.FindAsync((int)id);
            if (job == null) return NotFound();

            job.Value5 = dto.Watermark;
            job.UpdatedAt = dto.UpdatedAt;
            
            await _db.SaveChangesAsync();
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update job state");
            return StatusCode(500, ex.Message);
        }
    }
}
