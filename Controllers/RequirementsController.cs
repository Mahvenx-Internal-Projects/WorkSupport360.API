using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

using WorkSupport360.API.Data;
using WorkSupport360.API.DTOs;
using WorkSupport360.API.Models;
using WorkSupport360.API.Services;

namespace WorkSupport360.API.Controllers;

[ApiController, Route("api/requirements"), Authorize]
public class RequirementsController(AppDbContext db, IEmailService email) : ControllerBase
{
    private Guid Me => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Role => User.FindFirstValue(ClaimTypes.Role)!;

    // ── PUBLIC: Job board for freelancers ─────────────────────────────
    [HttpGet("public"), AllowAnonymous]
    public async Task<IActionResult> GetPublic(
        [FromQuery] string? skill, [FromQuery] string? type,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = db.Requirements
            .Where(r => r.Status == "open")
            .AsQueryable();

        if (!string.IsNullOrEmpty(skill))
            q = q.Where(r => r.SkillsRequired.Contains(skill));
        if (!string.IsNullOrEmpty(type))
            q = q.Where(r => r.WorkMode == type);

        var total = await q.CountAsync();
        var items = await q
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new {
                r.Id,
                r.Title,
                r.SkillsRequired,
                r.HoursPerEngagement,
                r.FreelancerCount,
                r.BudgetMin,
                r.BudgetMax,
                r.Currency,
                r.Duration,
                r.DurationType,
                r.WorkMode,
                r.Urgency,
                r.CreatedAt,
                r.Status,
                CompanyName = r.CompanyName ?? "Confidential"
            })
            .ToListAsync();

        return Ok(new { items, total, page, pageSize });
    }


   

    // ── CLIENT: Get my own requirements ──────────────────────────────────
    [HttpGet("mine"), Authorize(Roles = "client,Client")]
    public async Task<IActionResult> GetMine()
    {
        var items = await db.Requirements
            .Where(r => r.ClientUserId == Me)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
        return Ok(new { items, total = items.Count });
    }

    // ── CLIENT: Post new requirement ──────────────────────────────────
    [HttpPost, Authorize(Roles = "client,admin,Client,Admin")]
    public async Task<IActionResult> Create([FromBody] CreateRequirementDto dto)
    {
        var user = await db.Users.FindAsync(Me);
        var client = await db.Clients.FirstOrDefaultAsync(c => c.UserId == Me);
        // Admin can post on behalf of clients — use their own user info

        var req = new Requirement
        {
            ClientUserId = Me,
            Title = dto.Title ?? $"{dto.SkillsRequired} Requirement",
            SkillsRequired = dto.SkillsRequired,
            HoursPerEngagement = dto.HoursPerEngagement,
            FreelancerCount = dto.FreelancerCount,
            BudgetMin = dto.BudgetMin,
            BudgetMax = dto.BudgetMax,
            Currency = dto.Currency ?? "INR",
            Duration = dto.Duration,
            DurationType = dto.DurationType,
            WorkMode = dto.WorkMode ?? "remote",
            Description = dto.Description,
            CompanyName = dto.CompanyName ?? client?.CompanyName,
            ContactName = dto.ContactName ?? user?.Name,
            Urgency = dto.Urgency ?? "normal",
            PreferredStartDate = dto.PreferredStartDate,
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
        };

        db.Requirements.Add(req);

        // In-app notification to admin
        var adminUser = await db.Users.FirstOrDefaultAsync(u => u.Role == "admin");
        if (adminUser != null)
            db.Notifications.Add(new Notification
            {
                UserId = adminUser.Id,
                Type = "requirement",
                Priority = req.Urgency == "urgent" ? "high" : "normal",
                Title = $"New requirement: {req.Title}",
                Message = $"{req.CompanyName} posted a {req.Urgency} requirement for {req.SkillsRequired}. Review and approve.",
                ActionUrl = "/admin/requirements",
            });

        await db.SaveChangesAsync();

        // Email to client
        if (user?.Email != null)
            await email.SendRequirementReceivedAsync(
                user.Email, user.Name ?? "Client", req.Title,
                req.SkillsRequired, req.Currency, req.BudgetMin, req.BudgetMax,
                req.HoursPerEngagement, req.WorkMode ?? "remote");

        // Email to admin
        await email.SendAdminRequirementAlertAsync(
            user?.Name ?? "Client", user?.Email ?? "",
            req.Title, req.SkillsRequired,
            req.Currency, req.BudgetMin, req.BudgetMax,
            req.HoursPerEngagement, req.FreelancerCount,
            req.Duration, req.DurationType,
            req.WorkMode ?? "remote", req.Urgency ?? "normal",
            req.Description ?? "");

        return Ok(new
        {
            success = true,
            requirementId = req.Id,
            message = "Requirement submitted. Confirmation email sent."
        });
    }

    // ── ADMIN: Get all requirements ───────────────────────────────────
    [HttpGet, Authorize(Roles = "admin")]
    public async Task<IActionResult> GetAll([FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = db.Requirements.AsQueryable();
        if (!string.IsNullOrEmpty(status)) q = q.Where(r => r.Status == status);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new { items, total });
    }

    // ── ADMIN: Approve / edit / reject ────────────────────────────────
    [HttpPatch("{id:guid}"), Authorize(Roles = "admin,Admin,client,Client")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRequirementDto dto)
    {
        var req = await db.Requirements.FindAsync(id);
        if (req is null) return NotFound();

        // Client can only edit their own pending requirements
        if (Role == "client" && req.ClientUserId != Me) return Forbid();
        if (Role == "client" && !new[] { "pending", "open" }.Contains(req.Status))
            return BadRequest(new { message = "Cannot edit a requirement that is already allocated or closed" });

        var prevStatus = req.Status;
        if (dto.Status != null) req.Status = dto.Status;
        if (dto.Title != null) req.Title = dto.Title;
        if (dto.AdminNotes != null) req.AdminNotes = dto.AdminNotes;
        if (dto.SkillsRequired != null) req.SkillsRequired = dto.SkillsRequired;
        if (dto.BudgetMin.HasValue) req.BudgetMin = dto.BudgetMin.Value;
        if (dto.BudgetMax.HasValue) req.BudgetMax = dto.BudgetMax.Value;
        req.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        // Email client when approved → now live
        if (prevStatus != "open" && dto.Status == "open")
        {
            var clientUser = await db.Users.FindAsync(req.ClientUserId);
            if (clientUser?.Email != null)
            {
                db.Notifications.Add(new Notification
                {
                    UserId = clientUser.Id,
                    Type = "requirement",
                    Priority = "high",
                    Title = "Your requirement is now LIVE! 🎉",
                    Message = $"'{req.Title}' has been approved and posted to the freelancer job board.",
                    ActionUrl = "/client",
                });
                await db.SaveChangesAsync();
                await email.SendRequirementApprovedAsync(
                    clientUser.Email, clientUser.Name ?? "Client", req.Title);
            }
        }

        return Ok(new { success = true, requirement = req });
    }

    // ── ADMIN: Delete ─────────────────────────────────────────────────
    [HttpDelete("{id:guid}"), Authorize(Roles = "admin,Admin,client,Client")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var req = await db.Requirements.FindAsync(id);
        if (req is null) return NotFound();
        if (Role == "client" && req.ClientUserId != Me) return Forbid();
        if (Role == "client" && req.Status == "allocated")
            return BadRequest(new { message = "Cannot delete — expert already assigned" });
        db.Requirements.Remove(req);
        await db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ── ADMIN: Allocate freelancer ────────────────────────────────────
    [HttpPatch("{id:guid}/allocate"), Authorize(Roles = "admin")]
    public async Task<IActionResult> Allocate(Guid id, [FromBody] AllocateRequirementDto dto)
    {
        var req = await db.Requirements.FindAsync(id);
        if (req is null) return NotFound();
        req.AllocatedFreelancerId = dto.FreelancerId;
        req.Status = "allocated";
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Notify client
        var clientUser = await db.Users.FindAsync(req.ClientUserId);
        if (clientUser?.Email != null)
        {
            db.Notifications.Add(new Notification
            {
                UserId = clientUser.Id,
                Type = "requirement",
                Priority = "high",
                Title = "Expert assigned to your requirement 🚀",
                Message = $"A verified expert has been assigned for '{req.Title}'. Admin will contact you to schedule.",
                ActionUrl = "/client",
            });
            await db.SaveChangesAsync();
            await email.SendRequirementAssignedAsync(
                clientUser.Email, clientUser.Name ?? "Client", req.Title);
        }

        return Ok(new { success = true });
    }

    // ── FREELANCER: Apply ─────────────────────────────────────────────
    [HttpPost("{id:guid}/apply"), Authorize(Roles = "freelancer,Freelancer")]
    public async Task<IActionResult> Apply(Guid id, [FromBody] ApplyRequirementDto dto)
    {
        var req = await db.Requirements.FindAsync(id);
        if (req is null) return NotFound();

        var exists = await db.RequirementApplications
            .AnyAsync(a => a.RequirementId == id && a.FreelancerUserId == Me);
        if (exists) return BadRequest(new { message = "Already applied" });

        var freelancer = await db.Freelancers.FirstOrDefaultAsync(f => f.UserId == Me);
        var user = await db.Users.FindAsync(Me);

        var app = new RequirementApplication
        {
            RequirementId = id,
            FreelancerUserId = Me,
            FreelancerId = freelancer?.Id,
            CoverNote = dto.CoverNote,
            ProposedRate = dto.ProposedRate,
            Status = "pending",
            AppliedAt = DateTime.UtcNow,
        };
        db.RequirementApplications.Add(app);

        // Notify admin
        var admin = await db.Users.FirstOrDefaultAsync(u => u.Role == "admin");
        if (admin != null)
            db.Notifications.Add(new Notification
            {
                UserId = admin.Id,
                Type = "application",
                Priority = "normal",
                Title = $"New application: {freelancer?.AliasName ?? user?.Name}",
                Message = $"{freelancer?.AliasName ?? user?.Name} applied for '{req.Title}'. Proposed: {dto.ProposedRate}",
                ActionUrl = "/admin/requirements",
            });

        await db.SaveChangesAsync();

        // Email admin
        await email.SendAdminApplicationAlertAsync(
            freelancer?.AliasName ?? user?.Name ?? "Freelancer",
            user?.Email ?? "", req.Title,
            dto.ProposedRate ?? "", dto.CoverNote ?? "");

        return Ok(new { success = true, applicationId = app.Id });
    }

    // ── FREELANCER: My applications ───────────────────────────────────
    [HttpGet("my-applications"), Authorize(Roles = "freelancer")]
    public async Task<IActionResult> MyApplications()
    {
        var apps = await db.RequirementApplications
            .Where(a => a.FreelancerUserId == Me)
            .OrderByDescending(a => a.AppliedAt)
            .ToListAsync();

        var reqIds = apps.Select(a => a.RequirementId).ToList();
        var reqs = await db.Requirements
            .Where(r => reqIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id);

        var result = apps.Select(a => new {
            a.Id,
            a.RequirementId,
            requirementTitle = reqs.TryGetValue(a.RequirementId, out var r) ? r.Title : "",
            company = reqs.TryGetValue(a.RequirementId, out var r2) ? (r2.CompanyName ?? "Confidential") : "",
            skills = reqs.TryGetValue(a.RequirementId, out var r3) ? r3.SkillsRequired : "",
            type = reqs.TryGetValue(a.RequirementId, out var r4) ? r4.WorkMode : "",
            rate = reqs.TryGetValue(a.RequirementId, out var r5) ? $"{r5.Currency}{r5.BudgetMin}–{r5.Currency}{r5.BudgetMax}/hr" : "",
            status = a.Status,
            a.AppliedAt,
            adminNote = a.AdminNote,
        });

        return Ok(result);
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────

    public record CreateRequirementDto(
        string? Title, string SkillsRequired, string HoursPerEngagement,
        int FreelancerCount, decimal BudgetMin, decimal BudgetMax,
        string? Currency, string? Duration, string? DurationType,
        string? WorkMode, string? Description, string? CompanyName,
        string? ContactName, string? Urgency, string? PreferredStartDate,
        int MinExperience = 0);

    public record UpdateRequirementDto(
        string? Status, string? Title, string? AdminNotes,
        string? SkillsRequired, decimal? BudgetMin, decimal? BudgetMax);

    public record AllocateRequirementDto(Guid FreelancerId);
    public record ApplyRequirementDto(string? CoverNote, string? ProposedRate);
