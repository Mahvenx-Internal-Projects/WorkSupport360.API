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

    // ── Express Interest — client shares budget ──────────────────
    [HttpPost("{id:guid}/express-interest"), Authorize(Roles = "client")]
    public async Task<IActionResult> ExpressInterest(Guid id, [FromBody] ExpressInterestDto dto)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var client = await db.Clients.FirstOrDefaultAsync(c => c.UserId == userId);
        if (client is null) return NotFound(new { message = "Client profile not found" });

        var req = await db.DemoRequests
            .Include(r => r.Freelancer).ThenInclude(f => f.User)
            .Include(r => r.Client)
            .FirstOrDefaultAsync(r => r.Id == id && r.ClientId == client.Id);
        if (req is null) return NotFound(new { message = "Request not found" });

        req.ClientInterested    = true;
        req.ClientOfferedBudget = dto.OfferedBudget;
        req.ClientBudgetType    = dto.BudgetType ?? "hourly";
        req.ClientMessage       = dto.Message;
        req.ClientInterestedAt  = DateTime.UtcNow;
        req.InterestStatus      = "client_interested";

        // Notify all admins via in-app notification
        var admins = await db.Users.Where(u => u.Role == "admin").ToListAsync();
        foreach (var admin in admins)
            db.Notifications.Add(new Notification {
                UserId    = admin.Id,
                Type      = "interest",
                Priority  = "urgent",
                Title     = $"🔥 {client.ContactName} is interested in {req.Freelancer.AliasName}!",
                Message   = $"Budget: {dto.BudgetType} {dto.OfferedBudget} {req.Currency}. {dto.Message?.Substring(0, Math.Min(dto.Message?.Length ?? 0, 80))}",
                ActionUrl = "/admin/requests",
            });

        // Email admin
        try
        {
            await email.SendRequestReceivedAsync(
                "admin@worksupport360.com", "Admin",
                req.Freelancer.AliasName, "interest",
                DateTime.UtcNow.AddDays(3));
        }
        catch { /* non-blocking */ }

        await db.SaveChangesAsync();
        return Ok(new {
            message        = "Interest submitted! Admin will contact you within 4 hours.",
            interestStatus = "client_interested"
        });
    }

    // ── Notify Freelancer — admin sends email to freelancer ───────
    [HttpPost("{id:guid}/notify-freelancer"), Authorize(Roles = "admin")]
    public async Task<IActionResult> NotifyFreelancer(Guid id, [FromBody] NotifyFreelancerDto dto)
    {
        var req = await db.DemoRequests
            .Include(r => r.Freelancer).ThenInclude(f => f.User)
            .Include(r => r.Client)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (req is null) return NotFound();

        req.AdminNotes     = dto.AdminNotes;
        req.InterestStatus = "freelancer_notified";

        // In-app notification to freelancer
        db.Notifications.Add(new Notification {
            UserId    = req.Freelancer.UserId,
            Type      = "interest",
            Priority  = "urgent",
            Title     = "🎯 A client is interested in working with you!",
            Message   = $"Budget offered: {req.ClientBudgetType} {req.ClientOfferedBudget} {req.Currency}. {dto.AdminNotes ?? "Check portal for details."}",
            ActionUrl = "/freelancer",
        });

        // Email to freelancer
        try
        {
            await email.SendFreelancerAvailabilityCheckAsync(
                req.Freelancer.User.Email,
                req.Freelancer.User.Name,
                req.Client?.CompanyName ?? "A client",
                DateTime.UtcNow.AddDays(7),
                "Zoom", "");
        }
        catch { /* non-blocking */ }

        await db.SaveChangesAsync();
        return Ok(new { message = "Freelancer notified via email + in-app notification" });
    }

    private static DemoRequestDto Map(DemoRequest r) => new(
        r.Id, r.ClientId, r.Client.CompanyName, r.Client.User?.MobileNumber ?? "",
        r.FreelancerId, r.Freelancer.AliasName, r.Freelancer.User?.MobileNumber ?? "",
        r.SessionType, r.PreferredDateTime, r.DurationMinutes,
        r.BudgetMin, r.BudgetMax, r.BudgetType, r.Currency,
        r.Description, r.Status, r.AdminNotes,
        r.FinalBudget, r.FinalBudgetType, r.CreatedAt,
        r.ClientInterested, r.ClientOfferedBudget, r.ClientBudgetType,
        r.ClientMessage, r.ClientInterestedAt, r.InterestStatus,
        r.Freelancer.AliasName,
        r.Client.User?.Email ?? "",
        r.Client.CompanyName,
        r.Freelancer.User?.Email ?? "");
}

public record ExpressInterestDto(
    decimal? OfferedBudget = null,
    string?  BudgetType   = "hourly",
    string?  Message      = null
);

public record NotifyFreelancerDto(
    string? AdminNotes = null
);
