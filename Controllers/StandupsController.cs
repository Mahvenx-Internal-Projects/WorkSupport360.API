using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WorkSupport360.API.Data;
using WorkSupport360.API.DTOs;
using WorkSupport360.API.Models;

namespace WorkSupport360.API.Controllers;

[ApiController, Route("api/standups"), Authorize]
public class StandupsController(AppDbContext db) : ControllerBase
{
    private Guid Me => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("project/{projectId:guid}")]
    public async Task<IActionResult> GetByProject(Guid projectId) =>
        Ok(await db.DailyStandups.Include(s => s.Project).Include(s => s.Freelancer)
            .Where(s => s.ProjectId == projectId).OrderByDescending(s => s.Date)
            .Select(s => new StandupDto(s.Id, s.Project.Name, s.Date, s.YesterdayWork, s.TodayPlan, s.Blockers, s.HoursWorked, s.CreatedAt))
            .ToListAsync());

    [HttpPost, Authorize(Roles = "freelancer")]
    public async Task<IActionResult> Create([FromBody] CreateStandupDto dto)
    {
        var fl = await db.Freelancers.FirstOrDefaultAsync(f => f.UserId == Me);
        if (fl is null) return Forbid();

        // Log attendance
        db.AttendanceLogs.Add(new AttendanceLog { UserId = Me, Action = "standup",
            Note = $"Standup for project {dto.ProjectId}" });

        var standup = new DailyStandup
        {
            ProjectId = dto.ProjectId, FreelancerId = fl.Id,
            Date = dto.Date.Date, YesterdayWork = dto.YesterdayWork,
            TodayPlan = dto.TodayPlan, Blockers = dto.Blockers,
            HoursWorked = dto.HoursWorked,
        };
        db.DailyStandups.Add(standup);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetByProject), new { projectId = dto.ProjectId }, new { id = standup.Id });
    }
}
