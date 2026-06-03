using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WorkSupport360.API.Data;
using WorkSupport360.API.Models;
using WorkSupport360.API.Services;

namespace WorkSupport360.API.Controllers;

[ApiController, Route("api/requirements")]
public class RequirementsController(AppDbContext db, IEmailService email) : ControllerBase
{
    // ── Client: Post a new requirement ───────────────────────
    [HttpPost, Authorize(Roles = "client")]
    public async Task<IActionResult> Create([FromBody] CreateRequirementDto dto)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var client = await db.Clients.Include(c => c.User)
            .FirstOrDefaultAsync(c => c.UserId == userId);
        if (client is null) return NotFound("Client profile not found");

        var req = new JobRequirement
        {
            ClientId       = client.Id,
            Title          = dto.Title,
            JobDescription = dto.JobDescription,
            RequiredSkills = dto.RequiredSkills,
            ExperienceMin  = dto.ExperienceMin,
            ExperienceMax  = dto.ExperienceMax,
            BudgetType     = dto.BudgetType ?? "hourly",
            BudgetMin      = dto.BudgetMin,
            BudgetMax      = dto.BudgetMax,
            Currency       = dto.Currency ?? "USD",
            WorkMode       = dto.WorkMode ?? "remote",
            HybridDaysPerWeek = dto.HybridDaysPerWeek,
            Location       = dto.Location,
            WorkTimings    = dto.WorkTimings,
            EngagementType = dto.EngagementType ?? "freelance",
            OpenPositions  = dto.OpenPositions > 0 ? dto.OpenPositions : 1,
            Notes          = dto.Notes,
            Status         = "open",
        };
        db.JobRequirements.Add(req);

        // Notify all admins
        var admins = await db.Users.Where(u => u.Role == "admin").ToListAsync();
        foreach (var admin in admins)
            db.Notifications.Add(new Notification {
                UserId    = admin.Id,
                Type      = "requirement",
                Priority  = "normal",
                Title     = $"📋 New requirement posted: {dto.Title}",
                Message   = $"{client.CompanyName} posted a new requirement. {dto.OpenPositions} position(s). Budget: {dto.Currency} {dto.BudgetMin}–{dto.BudgetMax} ({dto.BudgetType})",
                ActionUrl = "/admin/requirements",
            });

        // Notify client
        db.Notifications.Add(new Notification {
            UserId    = userId,
            Type      = "requirement",
            Priority  = "normal",
            Title     = $"✅ Requirement posted: {dto.Title}",
            Message   = "Your requirement has been posted. Admin will review and assign suitable freelancers shortly.",
            ActionUrl = "/client/requirements",
        });

        // Email admin
        try
        {
            await email.SendRequestReceivedAsync(
                "admin@worksupport360.com", "Admin",
                dto.Title, dto.EngagementType ?? "requirement",
                DateTime.UtcNow.AddDays(1));
        }
        catch { /* non-blocking */ }

        await db.SaveChangesAsync();
        return Ok(new { id = req.Id, message = "Requirement posted! Admin will contact you shortly." });
    }

    // ── Public: List open requirements (for homepage) ─────────
    [HttpGet("public")]
    public async Task<IActionResult> GetPublic([FromQuery] int page = 1, [FromQuery] int pageSize = 12)
    {
        var q = db.JobRequirements
            .Include(r => r.Client)
            .Where(r => r.Status == "open")
            .OrderByDescending(r => r.CreatedAt);
        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new { total, items = items.Select(MapPublic) });
    }

    // ── Client: My requirements ────────────────────────────────
    [HttpGet("mine"), Authorize(Roles = "client")]
    public async Task<IActionResult> GetMine()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var client = await db.Clients.FirstOrDefaultAsync(c => c.UserId == userId);
        if (client is null) return NotFound();
        var items = await db.JobRequirements
            .Include(r => r.Assignments).ThenInclude(a => a.Freelancer).ThenInclude(f => f.User)
            .Where(r => r.ClientId == client.Id)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
        return Ok(items.Select(MapFull));
    }

    // ── Admin: All requirements ────────────────────────────────
    [HttpGet, Authorize(Roles = "admin")]
    public async Task<IActionResult> GetAll([FromQuery] string? status)
    {
        var q = db.JobRequirements
            .Include(r => r.Client).ThenInclude(c => c.User)
            .Include(r => r.Assignments).ThenInclude(a => a.Freelancer).ThenInclude(f => f.User)
            .AsQueryable();
        if (!string.IsNullOrEmpty(status)) q = q.Where(r => r.Status == status);
        var items = await q.OrderByDescending(r => r.CreatedAt).ToListAsync();
        return Ok(items.Select(MapFull));
    }

    // ── Admin: Assign freelancers ──────────────────────────────
    [HttpPost("{id:guid}/assign"), Authorize(Roles = "admin")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignFreelancersDto dto)
    {
        var req = await db.JobRequirements
            .Include(r => r.Client).ThenInclude(c => c.User)
            .Include(r => r.Assignments)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (req is null) return NotFound();

        var assigned = new List<string>();
        foreach (var fId in dto.FreelancerIds)
        {
            // Skip if already assigned
            if (req.Assignments.Any(a => a.FreelancerId == fId)) continue;

            var fl = await db.Freelancers.Include(f => f.User)
                .FirstOrDefaultAsync(f => f.Id == fId);
            if (fl is null) continue;

            db.RequirementAssignments.Add(new RequirementAssignment {
                RequirementId = id,
                FreelancerId  = fId,
                Status        = "notified",
                AdminNote     = dto.AdminNote,
            });

            // In-app notification to freelancer
            db.Notifications.Add(new Notification {
                UserId    = fl.UserId,
                Type      = "requirement",
                Priority  = "urgent",
                Title     = $"🎯 New opportunity: {req.Title}",
                Message   = $"A client has a requirement matching your skills! {req.BudgetType} budget: {req.Currency} {req.BudgetMin}–{req.BudgetMax}. Check your portal to view and respond.",
                ActionUrl = "/freelancer/requirements",
            });

            // Email freelancer
            try
            {
                await email.SendFreelancerAvailabilityCheckAsync(
                    fl.User.Email, fl.User.Name,
                    req.Client.CompanyName,
                    req.CreatedAt.AddDays(3), "Video call", "");
            }
            catch { /* non-blocking */ }

            assigned.Add(fl.AliasName);
        }

        if (req.Status == "open") req.Status = "in_progress";
        await db.SaveChangesAsync();

        return Ok(new {
            message  = $"Assigned to {assigned.Count} freelancer(s): {string.Join(", ", assigned)}",
            assigned = assigned.Count
        });
    }

    // ── Freelancer: Respond (interested / declined) ────────────
    [HttpPost("{id:guid}/respond"), Authorize(Roles = "freelancer")]
    public async Task<IActionResult> Respond(Guid id, [FromBody] RespondToRequirementDto dto)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var fl = await db.Freelancers.FirstOrDefaultAsync(f => f.UserId == userId);
        if (fl is null) return NotFound("Freelancer profile not found");

        var assignment = await db.RequirementAssignments
            .Include(a => a.Requirement).ThenInclude(r => r.Client).ThenInclude(c => c.User)
            .FirstOrDefaultAsync(a => a.RequirementId == id && a.FreelancerId == fl.Id);
        if (assignment is null) return NotFound("Assignment not found");

        assignment.Status               = dto.Interested ? "interested" : "declined";
        assignment.FreelancerNote       = dto.Note;
        assignment.FreelancerRespondedAt = DateTime.UtcNow;

        if (dto.Interested)
        {
            var req = assignment.Requirement;
            // Notify admin
            var admins = await db.Users.Where(u => u.Role == "admin").ToListAsync();
            foreach (var admin in admins)
                db.Notifications.Add(new Notification {
                    UserId    = admin.Id,
                    Type      = "requirement",
                    Priority  = "urgent",
                    Title     = $"🔥 Freelancer interested: {req.Title}",
                    Message   = $"{fl.AliasName} is interested in '{req.Title}'. Note: {dto.Note?.Substring(0, Math.Min(dto.Note?.Length ?? 0, 80))}",
                    ActionUrl = $"/admin/requirements/{id}",
                });

            // Notify client
            if (req.Client?.User != null)
                db.Notifications.Add(new Notification {
                    UserId    = req.Client.User.Id,
                    Type      = "requirement",
                    Priority  = "normal",
                    Title     = $"✅ Expert interested in your requirement!",
                    Message   = $"An expert is interested in '{req.Title}'. Admin will coordinate and set up a meeting.",
                    ActionUrl = "/client/requirements",
                });
        }

        await db.SaveChangesAsync();
        return Ok(new { message = dto.Interested ? "Great! Admin and the client have been notified. Expect a meeting invite soon." : "Response recorded." });
    }

    // ── Admin: Close requirement ───────────────────────────────
    [HttpPatch("{id:guid}/close"), Authorize(Roles = "admin")]
    public async Task<IActionResult> Close(Guid id, [FromBody] CloseRequirementDto dto)
    {
        var req = await db.JobRequirements.Include(r => r.Client).ThenInclude(c => c.User)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (req is null) return NotFound();

        req.Status   = dto.Status ?? "closed"; // closed | cancelled
        req.ClosedAt = DateTime.UtcNow;

        // Notify client
        if (req.Client?.User != null)
            db.Notifications.Add(new Notification {
                UserId  = req.Client.User.Id,
                Type    = "requirement",
                Priority = "normal",
                Title   = $"Requirement {req.Status}: {req.Title}",
                Message = dto.Notes ?? $"Your requirement '{req.Title}' has been {req.Status}.",
            });

        await db.SaveChangesAsync();
        return Ok(new { message = $"Requirement marked as {req.Status}" });
    }

    // ── Freelancer: My assigned requirements ───────────────────
    [HttpGet("assigned"), Authorize(Roles = "freelancer")]
    public async Task<IActionResult> GetAssigned()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var fl = await db.Freelancers.FirstOrDefaultAsync(f => f.UserId == userId);
        if (fl is null) return NotFound();

        var assignments = await db.RequirementAssignments
            .Include(a => a.Requirement).ThenInclude(r => r.Client)
            .Where(a => a.FreelancerId == fl.Id)
            .OrderByDescending(a => a.AssignedAt)
            .ToListAsync();

        return Ok(assignments.Select(a => new {
            requirementId  = a.RequirementId,
            assignmentId   = a.Id,
            status         = a.Status,
            adminNote      = a.AdminNote,
            assignedAt     = a.AssignedAt,
            respondedAt    = a.FreelancerRespondedAt,
            requirement    = MapPublic(a.Requirement),
        }));
    }

    private static object MapPublic(JobRequirement r) => new {
        id             = r.Id,
        title          = r.Title,
        description    = r.JobDescription,
        skills         = r.RequiredSkills,
        experienceMin  = r.ExperienceMin,
        experienceMax  = r.ExperienceMax,
        budgetType     = r.BudgetType,
        budgetMin      = r.BudgetMin,
        budgetMax      = r.BudgetMax,
        currency       = r.Currency,
        workMode       = r.WorkMode,
        hybridDays     = r.HybridDaysPerWeek,
        location       = r.Location,
        workTimings    = r.WorkTimings,
        engagementType = r.EngagementType,
        openPositions  = r.OpenPositions,
        notes          = r.Notes,
        status         = r.Status,
        createdAt      = r.CreatedAt,
        clientName     = r.Client?.CompanyName ?? "Company",
    };

    private static object MapFull(JobRequirement r) => new {
        id             = r.Id,
        title          = r.Title,
        description    = r.JobDescription,
        skills         = r.RequiredSkills,
        experienceMin  = r.ExperienceMin,
        experienceMax  = r.ExperienceMax,
        budgetType     = r.BudgetType,
        budgetMin      = r.BudgetMin,
        budgetMax      = r.BudgetMax,
        currency       = r.Currency,
        workMode       = r.WorkMode,
        hybridDays     = r.HybridDaysPerWeek,
        location       = r.Location,
        workTimings    = r.WorkTimings,
        engagementType = r.EngagementType,
        openPositions  = r.OpenPositions,
        notes          = r.Notes,
        status         = r.Status,
        createdAt      = r.CreatedAt,
        closedAt       = r.ClosedAt,
        clientName     = r.Client?.CompanyName ?? "Company",
        clientId       = r.ClientId,
        clientEmail    = r.Client?.User?.Email,
        assignmentCount = r.Assignments.Count,
        interestedCount = r.Assignments.Count(a => a.Status == "interested"),
        assignments    = r.Assignments.Select(a => new {
            a.Id, a.Status, a.AdminNote, a.FreelancerNote, a.AssignedAt, a.FreelancerRespondedAt,
            freelancerName  = a.Freelancer?.AliasName,
            freelancerId    = a.FreelancerId,
        }),
    };
}

public record CreateRequirementDto(
    string Title, string JobDescription,
    string? RequiredSkills = null,
    string? ExperienceMin = null, string? ExperienceMax = null,
    string? BudgetType = "hourly", decimal? BudgetMin = null, decimal? BudgetMax = null, string? Currency = "USD",
    string? WorkMode = "remote", int? HybridDaysPerWeek = null,
    string? Location = null, string? WorkTimings = null,
    string? EngagementType = "freelance", int OpenPositions = 1,
    string? Notes = null
);
public record AssignFreelancersDto(List<Guid> FreelancerIds, string? AdminNote = null);
public record RespondToRequirementDto(bool Interested, string? Note = null);
public record CloseRequirementDto(string? Status = "closed", string? Notes = null);
