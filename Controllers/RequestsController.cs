using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WorkSupport360.API.Data;
using WorkSupport360.API.DTOs;
using WorkSupport360.API.Models;
using WorkSupport360.API.Services;

namespace WorkSupport360.API.Controllers;

[ApiController, Route("api/requests"), Authorize]
public class RequestsController(AppDbContext db, IEmailService email) : ControllerBase
{
    private Guid Me   => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Role => User.FindFirstValue(ClaimTypes.Role)!;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status = null)
    {
        var query = db.DemoRequests
            .Include(r => r.Client).ThenInclude(c => c.User)
            .Include(r => r.Freelancer).ThenInclude(f => f.User)
            .AsQueryable();

        if (Role == "client") { var c = await db.Clients.FirstOrDefaultAsync(x => x.UserId == Me); query = query.Where(r => r.ClientId == c!.Id); }
        else if (Role == "freelancer") { var f = await db.Freelancers.FirstOrDefaultAsync(x => x.UserId == Me); query = query.Where(r => r.FreelancerId == f!.Id); }
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(r => r.Status == status);

        var items = await query.OrderByDescending(r => r.CreatedAt).Select(r => Map(r)).ToListAsync();
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var r = await db.DemoRequests
            .Include(x => x.Client).ThenInclude(c => c.User)
            .Include(x => x.Freelancer).ThenInclude(f => f.User)
            .FirstOrDefaultAsync(x => x.Id == id);
        return r is null ? NotFound() : Ok(Map(r));
    }

    [HttpPost, Authorize(Roles = "client")]
    public async Task<IActionResult> Create([FromBody] CreateDemoRequestDto dto)
    {
        var client = await db.Clients.Include(c => c.User).FirstOrDefaultAsync(c => c.UserId == Me);
        if (client is null) return Forbid();

        var freelancer = await db.Freelancers.Include(f => f.User).FirstOrDefaultAsync(f => f.Id == dto.FreelancerId);
        if (freelancer is null) return NotFound(new { message = "Freelancer not found" });

        var req = new DemoRequest
        {
            ClientId = client.Id, FreelancerId = dto.FreelancerId,
            SessionType = dto.SessionType, PreferredDateTime = dto.PreferredDateTime,
            DurationMinutes = dto.DurationMinutes, BudgetMin = dto.BudgetMin,
            BudgetMax = dto.BudgetMax, BudgetType = dto.BudgetType,
            Currency = dto.Currency, Description = dto.Description,
        };
        db.DemoRequests.Add(req);

        // Notify all admins
        var admins = await db.Users.Where(u => u.Role == "admin").ToListAsync();
        foreach (var admin in admins)
            db.Notifications.Add(new Notification {
                UserId = admin.Id, Type = "request", Priority = "high",
                Title = "New demo request",
                Message = $"{client.CompanyName} requested {dto.SessionType.Replace("_", " ")} with {freelancer.AliasName}.",
                ActionUrl = $"/admin/requests",
            });

        await db.SaveChangesAsync();
        await email.SendRequestReceivedAsync(client.User.Email, client.ContactName,
            freelancer.AliasName, dto.SessionType, dto.PreferredDateTime);

        return CreatedAtAction(nameof(Get), new { id = req.Id }, new { id = req.Id });
    }

    [HttpPatch("{id:guid}/status"), Authorize(Roles = "admin")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateRequestStatusDto dto)
    {
        var r = await db.DemoRequests.FindAsync(id);
        if (r is null) return NotFound();
        r.Status = dto.Status;
        r.AdminNotes = dto.AdminNotes;
        if (dto.FinalBudget.HasValue)
        {
            r.FinalBudget     = dto.FinalBudget;
            r.FinalBudgetType = dto.FinalBudgetType;
        }
        await db.SaveChangesAsync();
        return Ok(new { message = "Status updated", status = dto.Status });
    }

    private static DemoRequestDto Map(DemoRequest r) => new(
        r.Id, r.ClientId, r.Client.CompanyName, r.Client.User?.MobileNumber ?? "",
        r.FreelancerId, r.Freelancer.AliasName, r.Freelancer.User?.MobileNumber ?? "",
        r.SessionType, r.PreferredDateTime, r.DurationMinutes,
        r.BudgetMin, r.BudgetMax, r.BudgetType, r.Currency,
        r.Description, r.Status, r.AdminNotes,
        r.FinalBudget, r.FinalBudgetType, r.CreatedAt);
}
