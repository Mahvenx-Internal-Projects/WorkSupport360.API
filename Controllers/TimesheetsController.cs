using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WorkSupport360.API.Data;
using WorkSupport360.API.DTOs;
using WorkSupport360.API.Models;
using WorkSupport360.API.Services;

namespace WorkSupport360.API.Controllers;

[ApiController, Route("api/timesheets"), Authorize]
public class TimesheetsController(AppDbContext db, IEmailService email) : ControllerBase
{
    private Guid Me   => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Role => User.FindFirstValue(ClaimTypes.Role)!;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status = null, [FromQuery] Guid? projectId = null)
    {
        var query = db.Timesheets
            .Include(t => t.Project).Include(t => t.Freelancer).Include(t => t.Entries)
            .AsQueryable();

        if (Role == "freelancer") { var f = await db.Freelancers.FirstOrDefaultAsync(x => x.UserId == Me); query = query.Where(t => t.FreelancerId == f!.Id); }
        else if (Role == "client") { var c = await db.Clients.FirstOrDefaultAsync(x => x.UserId == Me); query = query.Where(t => t.Project.ClientId == c!.Id); }
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(t => t.Status == status);
        if (projectId.HasValue) query = query.Where(t => t.ProjectId == projectId);

        return Ok(await query.OrderByDescending(t => t.WeekStart).Select(t => Map(t)).ToListAsync());
    }

    [HttpPost, Authorize(Roles = "freelancer")]
    public async Task<IActionResult> Create([FromBody] CreateTimesheetDto dto)
    {
        var fl = await db.Freelancers.Include(f => f.User).FirstOrDefaultAsync(f => f.UserId == Me);
        if (fl is null) return Forbid();

        var project = await db.Projects.Include(p => p.Client).ThenInclude(c => c.User)
            .FirstOrDefaultAsync(p => p.Id == dto.ProjectId && p.FreelancerId == fl.Id);
        if (project is null) return NotFound(new { message = "Project not found or not assigned to you" });
        if (project.Status != "active") return BadRequest(new { message = "Project is not active" });

        var totalHours  = dto.Entries.Sum(e => e.Hours);
        var totalAmount = totalHours * project.HourlyRate;

        // Log attendance: call/task
        db.AttendanceLogs.Add(new AttendanceLog { UserId = Me, Action = "timesheet_submit",
            Note = $"Submitted {totalHours}h for {project.Name}" });

        var ts = new Timesheet
        {
            ProjectId = dto.ProjectId, FreelancerId = fl.Id,
            WeekStart = dto.WeekStart, WeekEnd = dto.WeekEnd,
            TotalHours = totalHours, TotalAmount = totalAmount,
            Status = "submitted", SubmittedAt = DateTime.UtcNow,
            Entries = dto.Entries.Select(e => new TimesheetEntry
            {
                ProjectId = dto.ProjectId, Date = e.Date,
                Hours = e.Hours, Description = e.Description, TaskType = e.TaskType,
            }).ToList(),
        };
        db.Timesheets.Add(ts);
        project.LoggedHours += (int)totalHours;
        fl.PendingAmount    += totalAmount;

        // Notify admins and client
        var admins = await db.Users.Where(u => u.Role == "admin").ToListAsync();
        foreach (var admin in admins)
            db.Notifications.Add(new Notification { UserId = admin.Id, Type = "timesheet", Priority = "normal",
                Title = "Timesheet submitted",
                Message = $"{fl.AliasName} submitted {totalHours}h for {project.Name}. Amount: {project.Currency} {totalAmount:N2}",
                ActionUrl = "/admin/timesheets" });

        db.Notifications.Add(new Notification { UserId = project.Client.UserId, Type = "timesheet", Priority = "normal",
            Title = "Timesheet ready for review ⏱",
            Message = $"{fl.AliasName} submitted {totalHours}h ({project.Currency} {totalAmount:N2}) for {project.Name}. Please review.",
            ActionUrl = "/client/timesheets" });

        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), new { id = ts.Id }, new { id = ts.Id });
    }

    [HttpPatch("{id:guid}/approve"), Authorize(Roles = "admin,client")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveTimesheetDto dto)
    {
        var ts = await db.Timesheets
            .Include(t => t.Freelancer).ThenInclude(f => f.User)
            .Include(t => t.Project).ThenInclude(p => p.Client)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (ts is null) return NotFound();

        ts.Status = dto.Approve ? "approved" : "rejected";
        if (dto.Approve) ts.ApprovedAt = DateTime.UtcNow;

        if (dto.Approve)
        {
            var commRate    = ts.Project.Status == "active" ? 15m : 15m; // use client plan later
            var commission  = ts.TotalAmount * commRate / 100;
            var gstAmount   = ts.Project.ApplyGst ? ts.TotalAmount * ts.Project.GstRate / 100 : 0;
            var total       = ts.TotalAmount + gstAmount;
            var invoiceNum  = $"INV-{DateTime.UtcNow:yyyy}-{(await db.Invoices.CountAsync()) + 1:D4}";

            var invoice = new Invoice
            {
                InvoiceNumber = invoiceNum, ProjectId = ts.ProjectId,
                ClientId = ts.Project.ClientId, FreelancerId = ts.FreelancerId,
                Subtotal = ts.TotalAmount, Commission = commission, CommissionRate = commRate,
                ApplyGst = ts.Project.ApplyGst, GstRate = ts.Project.GstRate, GstAmount = gstAmount,
                Total = total, FreelancerAmount = ts.TotalAmount - commission,
                Currency = ts.Project.Currency,
                DueAt = DateTime.UtcNow.AddDays(7),
                PaymentInstructions = "Please pay via bank transfer or UPI. Include invoice number as reference.",
                LineItems = ts.Entries.Select(e => new InvoiceLineItem
                {
                    Description = $"{e.Description} — {e.Date:MMM d}",
                    Hours = e.Hours, Rate = ts.Project.HourlyRate, Amount = e.Hours * ts.Project.HourlyRate,
                }).ToList(),
                InvoiceTimesheets = [new InvoiceTimesheet { TimesheetId = ts.Id }],
            };
            db.Invoices.Add(invoice);

            // Notify client about invoice
            db.Notifications.Add(new Notification {
                UserId = ts.Project.Client.UserId, Type = "invoice", Priority = "high",
                Title = $"Invoice generated — {invoiceNum} 🧾",
                Message = $"Invoice {invoiceNum} for {total:N2} {ts.Project.Currency} is ready. Due: {invoice.DueAt:MMM d}.",
                ActionUrl = "/client/invoices",
            });
        }
        else
        {
            ts.Freelancer.PendingAmount = Math.Max(0, ts.Freelancer.PendingAmount - ts.TotalAmount);
        }

        await email.SendTimesheetApprovalAsync(ts.Freelancer.User.Email, ts.Freelancer.AliasName,
            ts.Project.Name, ts.TotalHours, ts.TotalAmount, dto.Approve, dto.Reason);
        await db.SaveChangesAsync();
        return Ok(new { message = dto.Approve ? "Approved — invoice generated" : "Rejected" });
    }

    private static TimesheetDto Map(Timesheet t) => new(
        t.Id, t.ProjectId, t.Project?.Name ?? "", t.Freelancer?.AliasName ?? "",
        t.WeekStart, t.WeekEnd, t.TotalHours, t.TotalAmount, t.Status,
        t.Entries.Select(e => new TimesheetEntryDto(e.Id, e.Date, e.Hours, e.Description, e.TaskType)).ToList(),
        t.SubmittedAt, t.ApprovedAt);
}
