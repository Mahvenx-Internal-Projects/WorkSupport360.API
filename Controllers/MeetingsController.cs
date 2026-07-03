using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WorkSupport360.API.Data;
using WorkSupport360.API.DTOs;
using WorkSupport360.API.Models;
using WorkSupport360.API.Services;

namespace WorkSupport360.API.Controllers;


[ApiController, Route("api/meetings"), Authorize]
public class MeetingsController(AppDbContext db, IEmailService email) : ControllerBase
{
    private Guid Me   => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Role => User.FindFirstValue(ClaimTypes.Role)!;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var query = db.Meetings
            .Include(m => m.Client).ThenInclude(c => c.User)
            .Include(m => m.Freelancer).ThenInclude(f => f.User)
            .Include(m => m.Request)
            .AsQueryable();

        if (Role == "client") { var c = await db.Clients.FirstOrDefaultAsync(x => x.UserId == Me); query = query.Where(m => m.ClientId == c!.Id); }
        else if (Role == "freelancer") { var f = await db.Freelancers.FirstOrDefaultAsync(x => x.UserId == Me); query = query.Where(m => m.FreelancerId == f!.Id); }

        return Ok(await query.OrderByDescending(m => m.ScheduledAt).Select(m => Map(m)).ToListAsync());
    }

    /// <summary>Admin schedules meeting — first sends availability check to freelancer, then invite on confirm</summary>
    [HttpPost, Authorize(Roles = "admin")]
    public async Task<IActionResult> Schedule([FromBody] ScheduleMeetingDto dto)
    {
        var request = await db.DemoRequests
            .Include(r => r.Client).ThenInclude(c => c.User)
            .Include(r => r.Freelancer).ThenInclude(f => f.User)
            .FirstOrDefaultAsync(r => r.Id == dto.RequestId);

        if (request is null) return NotFound(new { message = "Request not found" });
        if (request.Meeting is not null) return Conflict(new { message = "Meeting already scheduled for this request" });

        var meeting = new Meeting
        {
            RequestId = dto.RequestId, ClientId = request.ClientId,
            FreelancerId = request.FreelancerId,
            ScheduledAt = dto.ScheduledAt, DurationMinutes = dto.DurationMinutes,
            Platform = dto.Platform, MeetingLink = dto.MeetingLink,
            AgreedRate = dto.AgreedRate, BudgetType = dto.BudgetType,
            Currency = dto.Currency, FreelancerConfirmed = false,
        };
        db.Meetings.Add(meeting);
        request.Status = "scheduled";

        // Notifications
        db.Notifications.Add(new Notification {
            UserId = request.Client.UserId, Type = "meeting", Priority = "high",
            Title = "Meeting scheduled ✅",
            Message = $"Your meeting with {request.Freelancer.AliasName} is confirmed for {dto.ScheduledAt:MMM d 'at' h:mm tt} UTC.",
            ActionUrl = "/client/meetings",
        });
        db.Notifications.Add(new Notification {
            UserId = request.Freelancer.UserId, Type = "meeting", Priority = "high",
            Title = "Meeting scheduled — confirm availability ⏰",
            Message = $"Meeting with {request.Client.CompanyName} on {dto.ScheduledAt:MMM d 'at' h:mm tt} UTC. Please confirm or decline.",
            ActionUrl = "/freelancer/meetings",
        });

        await db.SaveChangesAsync();

        // Send availability check to freelancer (admin calls them too)
        await email.SendFreelancerAvailabilityCheckAsync(
            request.Freelancer.User.Email, request.Freelancer.AliasName,
            request.Client.CompanyName, dto.ScheduledAt, dto.Platform,
            request.Freelancer.User.MobileNumber ?? "");

        // Send invite to client
        await email.SendMeetingInviteAsync(
            request.Client.User.Email, request.Client.ContactName,
            dto.MeetingLink, dto.ScheduledAt, dto.Platform,
            request.Freelancer.AliasName, dto.AgreedRate, dto.BudgetType, dto.Currency);

        return CreatedAtAction(nameof(GetAll), new { id = meeting.Id }, Map(meeting));
    }

    /// <summary>Freelancer confirms or declines availability</summary>
    [HttpPatch("{id:guid}/confirm"), Authorize(Roles = "freelancer")]
    public async Task<IActionResult> Confirm(Guid id, [FromBody] FreelancerConfirmDto dto)
    {
        var m = await db.Meetings
            .Include(x => x.Client).ThenInclude(c => c.User)
            .Include(x => x.Freelancer)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (m is null) return NotFound();

        m.FreelancerConfirmed       = dto.Confirmed;
        m.FreelancerDeclineReason   = dto.DeclineReason;
        if (!dto.Confirmed) m.Status = "cancelled";

        // Notify admins
        var admins = await db.Users.Where(u => u.Role == "admin").ToListAsync();
        foreach (var admin in admins)
            db.Notifications.Add(new Notification {
                UserId = admin.Id, Type = "meeting",
                Priority = dto.Confirmed ? "normal" : "urgent",
                Title = dto.Confirmed ? "Freelancer confirmed meeting ✅" : "Freelancer declined meeting ❌",
                Message = dto.Confirmed
                    ? $"{m.Freelancer.AliasName} confirmed the meeting."
                    : $"{m.Freelancer.AliasName} declined: {dto.DeclineReason}. Please reassign.",
                ActionUrl = "/admin/meetings",
            });

        // Notify client if declined — suggest picking another freelancer
        if (!dto.Confirmed)
            db.Notifications.Add(new Notification {
                UserId = m.Client.UserId, Type = "meeting", Priority = "urgent",
                Title = "Meeting cancelled — expert unavailable",
                Message = $"The expert is unavailable at the proposed time. Admin is working on an alternative. Please browse other experts.",
                ActionUrl = "/client/browse",
            });

        await db.SaveChangesAsync();
        return Ok(new { message = dto.Confirmed ? "Confirmed!" : "Declined" });
    }

    [HttpPatch("{id:guid}/outcome"), Authorize(Roles = "admin")]
    public async Task<IActionResult> UpdateOutcome(Guid id, [FromBody] UpdateMeetingOutcomeDto dto)
    {
        var m = await db.Meetings.Include(x => x.Request).FirstOrDefaultAsync(x => x.Id == id);
        if (m is null) return NotFound();
        m.Outcome = dto.Outcome; m.Status = "completed";
        m.Request.Status = dto.Outcome == "approved" ? "approved" : "completed";
        if (dto.FinalBudget.HasValue)
        {
            m.Request.FinalBudget     = dto.FinalBudget;
            m.Request.FinalBudgetType = dto.FinalBudgetType;
        }
        await db.SaveChangesAsync();
        return Ok(new { message = "Outcome saved", outcome = dto.Outcome });
    }

    [HttpPatch("{id:guid}/cancel"), Authorize(Roles = "admin")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var m = await db.Meetings.FindAsync(id);
        if (m is null) return NotFound();
        m.Status = "cancelled";
        await db.SaveChangesAsync();
        return Ok(new { message = "Meeting cancelled" });
    }

    private static MeetingDto Map(Meeting m) => new(
        m.Id, m.Client?.CompanyName ?? "", m.Freelancer?.AliasName ?? "",
        m.Request?.SessionType ?? "", m.ScheduledAt, m.DurationMinutes,
        m.Platform, m.MeetingLink, m.AgreedRate, m.BudgetType,
        m.Currency, m.Status, m.Outcome, m.FreelancerConfirmed, m.CreatedAt);
}
