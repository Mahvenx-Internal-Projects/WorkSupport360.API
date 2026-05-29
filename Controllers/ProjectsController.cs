using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WorkSupport360.API.Data;
using WorkSupport360.API.DTOs;
using WorkSupport360.API.Models;
using WorkSupport360.API.Services;

namespace WorkSupport360.API.Controllers;

[ApiController, Route("api/projects"), Authorize]
public class ProjectsController(AppDbContext db, IEmailService email) : ControllerBase
{
    private Guid Me   => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Role => User.FindFirstValue(ClaimTypes.Role)!;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status = null)
    {
        var query = db.Projects
            .Include(p => p.Skills).Include(p => p.Milestones)
            .Include(p => p.Client).Include(p => p.Freelancer)
            .Include(p => p.StatusLogs)
            .AsQueryable();

        if (Role == "client") { var c = await db.Clients.FirstOrDefaultAsync(x => x.UserId == Me); query = query.Where(p => p.ClientId == c!.Id); }
        else if (Role == "freelancer") { var f = await db.Freelancers.FirstOrDefaultAsync(x => x.UserId == Me); query = query.Where(p => p.FreelancerId == f!.Id); }
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(p => p.Status == status);

        return Ok(await query.OrderByDescending(p => p.CreatedAt).Select(p => Map(p)).ToListAsync());
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var p = await db.Projects
            .Include(x => x.Skills).Include(x => x.Milestones)
            .Include(x => x.Client).Include(x => x.Freelancer)
            .Include(x => x.StatusLogs)
            .FirstOrDefaultAsync(x => x.Id == id);
        return p is null ? NotFound() : Ok(Map(p));
    }

    /// <summary>Admin creates project — status starts as pending_payment</summary>
    [HttpPost, Authorize(Roles = "admin")]
    public async Task<IActionResult> Create([FromBody] CreateProjectDto dto)
    {
        var client     = await db.Clients.Include(c => c.User).FirstOrDefaultAsync(c => c.Id == dto.ClientId);
        var freelancer = await db.Freelancers.Include(f => f.User).FirstOrDefaultAsync(f => f.Id == dto.FreelancerId);
        if (client is null || freelancer is null) return NotFound();

        var project = new Project
        {
            Name = dto.Name, ClientId = dto.ClientId, FreelancerId = dto.FreelancerId,
            Description = dto.Description, HourlyRate = dto.HourlyRate,
            BudgetType = dto.BudgetType, Currency = dto.Currency,
            EstimatedHours = dto.EstimatedHours, StartDate = dto.StartDate, EndDate = dto.EndDate,
            TotalBudget = dto.TotalBudget, EscrowBalance = dto.TotalBudget,
            PendingAmount = dto.TotalBudget,
            ApplyGst = dto.ApplyGst, GstRate = dto.GstRate,
            Timezone = dto.Timezone, BufferDays = dto.BufferDays,
            Status = "pending_payment",  // starts as pending — activated after payment
            Skills = dto.Skills.Select(s => new ProjectSkill { Skill = s }).ToList(),
            Milestones = dto.Milestones?.Select(m => new Milestone
            {
                Title = m.Title, Description = m.Description, DueDate = m.DueDate, Amount = m.Amount,
            }).ToList() ?? [],
            StatusLogs = [new ProjectStatusLog { OldStatus = "", NewStatus = "pending_payment", ChangedBy = Me }],
        };
        db.Projects.Add(project);

        // Notify both parties
        void Notify(Guid userId, string title, string msg, string url) =>
            db.Notifications.Add(new Notification { UserId = userId, Type = "project", Priority = "high", Title = title, Message = msg, ActionUrl = url });

        Notify(client.UserId, "Project created — payment pending 💳",
            $"Project '{dto.Name}' has been created. Please complete payment to start. Amount: {dto.Currency} {dto.TotalBudget:N2}",
            "/client/invoices");
        Notify(freelancer.UserId, "New project assigned! 🚀",
            $"You've been assigned to '{dto.Name}'. Project starts after client payment.",
            "/freelancer/assignments");

        await db.SaveChangesAsync();

        // Email both
        await email.SendProjectStartNotificationAsync(freelancer.User.Email, freelancer.AliasName,
            dto.Name, "Expert", dto.StartDate, $"Project starts after client payment. Buffer: {dto.BufferDays ?? "0"} days.");
        await email.SendProjectStartNotificationAsync(client.User.Email, client.ContactName,
            dto.Name, "Client", dto.StartDate, $"Please complete payment of {dto.Currency} {dto.TotalBudget:N2} to activate the project.");

        return CreatedAtAction(nameof(Get), new { id = project.Id }, new { id = project.Id });
    }

    /// <summary>Admin updates project (change freelancer, budget, status, pause, stop)</summary>
    [HttpPatch("{id:guid}"), Authorize(Roles = "admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProjectDto dto)
    {
        var project = await db.Projects
            .Include(p => p.Client).ThenInclude(c => c.User)
            .Include(p => p.Freelancer).ThenInclude(f => f.User)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (project is null) return NotFound();

        var oldStatus = project.Status;

        if (dto.Name is not null)            project.Name = dto.Name;
        if (dto.Description is not null)     project.Description = dto.Description;
        if (dto.HourlyRate.HasValue)         project.HourlyRate = dto.HourlyRate.Value;
        if (dto.BudgetType is not null)      project.BudgetType = dto.BudgetType;
        if (dto.TotalBudget.HasValue)        project.TotalBudget = dto.TotalBudget.Value;
        if (dto.StartDate.HasValue)          project.StartDate = dto.StartDate.Value;
        if (dto.EndDate.HasValue)            project.EndDate = dto.EndDate.Value;
        if (dto.Progress.HasValue)           project.Progress = dto.Progress.Value;

        // Handle freelancer change
        if (dto.FreelancerId.HasValue && dto.FreelancerId != project.FreelancerId)
        {
            var newFl = await db.Freelancers.Include(f => f.User).FirstOrDefaultAsync(f => f.Id == dto.FreelancerId);
            if (newFl is not null)
            {
                // Notify old freelancer
                db.Notifications.Add(new Notification {
                    UserId = project.Freelancer.UserId, Type = "project",
                    Title = "Removed from project",
                    Message = $"You have been removed from project '{project.Name}'.",
                    ActionUrl = "/freelancer", Priority = "high",
                });
                // Notify new freelancer
                db.Notifications.Add(new Notification {
                    UserId = newFl.UserId, Type = "project",
                    Title = "New project assigned! 🚀",
                    Message = $"You have been assigned to '{project.Name}'.",
                    ActionUrl = "/freelancer/assignments", Priority = "high",
                });
                project.FreelancerId = dto.FreelancerId.Value;
            }
        }

        // Handle status change
        if (dto.Status is not null && dto.Status != oldStatus)
        {
            project.Status = dto.Status;
            project.StatusLogs.Add(new ProjectStatusLog
            {
                ProjectId = project.Id, OldStatus = oldStatus,
                NewStatus = dto.Status, Reason = dto.Reason, ChangedBy = Me,
            });

            if (dto.Status == "paused")  { project.PauseDate = DateTime.UtcNow; project.PauseReason = dto.Reason; }
            if (dto.Status == "completed") { project.ActualEndDate = DateTime.UtcNow; project.Progress = 100; }
            if (dto.Status == "cancelled") project.CancelReason = dto.Reason;
            if (dto.Status == "active" && oldStatus == "pending_payment")
                project.PendingAmount = 0; // payment received

            // Notify both parties
            void Notify(Guid userId, string title, string msg) =>
                db.Notifications.Add(new Notification { UserId = userId, Type = "project", Priority = "high", Title = title, Message = msg, ActionUrl = "/client/projects" });

            var msg = $"Project '{project.Name}' is now {dto.Status}.{(dto.Reason is not null ? " Reason: " + dto.Reason : "")}";
            Notify(project.Client.UserId,     $"Project {dto.Status}", msg);
            Notify(project.Freelancer.UserId, $"Project {dto.Status}", msg);

            await email.SendProjectStatusChangeAsync(project.Client.User.Email,     project.Client.ContactName,     project.Name, dto.Status, dto.Reason);
            await email.SendProjectStatusChangeAsync(project.Freelancer.User.Email, project.Freelancer.AliasName,  project.Name, dto.Status, dto.Reason);
        }

        await db.SaveChangesAsync();
        return Ok(new { message = "Project updated", status = project.Status });
    }

    [HttpPatch("{projectId:guid}/milestones/{milestoneId:guid}"), Authorize]
    public async Task<IActionResult> UpdateMilestone(Guid projectId, Guid milestoneId, [FromBody] string status)
    {
        var m = await db.Milestones.FirstOrDefaultAsync(x => x.Id == milestoneId && x.ProjectId == projectId);
        if (m is null) return NotFound();
        m.Status = status;
        await db.SaveChangesAsync();
        return Ok(new { message = "Milestone updated", status });
    }

    private static ProjectDto Map(Project p) => new(
        p.Id, p.Name, p.Client?.CompanyName ?? "", p.Freelancer?.RealName ?? "", p.Freelancer?.AliasName ?? "",
        p.Description, p.Skills.Select(s => s.Skill).ToList(),
        p.HourlyRate, p.BudgetType, p.Currency,
        p.EstimatedHours, p.LoggedHours,
        p.StartDate, p.EndDate, p.Status, p.Progress,
        p.TotalBudget, p.TotalPaid, p.PendingAmount, p.EscrowBalance,
        p.ApplyGst, p.GstRate, p.Timezone, p.BufferDays,
        p.Milestones.Select(m => new MilestoneDto(m.Id, m.Title, m.Description, m.DueDate, m.Amount, m.Status)).ToList(),
        p.StatusLogs.OrderBy(l => l.ChangedAt).Select(l => new ProjectStatusLogDto(l.OldStatus, l.NewStatus, l.Reason, l.ChangedAt)).ToList(),
        p.CreatedAt);
}
