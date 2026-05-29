using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WorkSupport360.API.Data;
using WorkSupport360.API.DTOs;
using WorkSupport360.API.Models;
using WorkSupport360.API.Services;

namespace WorkSupport360.API.Controllers;

[ApiController, Route("api/invoices"), Authorize]
public class InvoicesController(AppDbContext db, IEmailService email, IConfiguration cfg) : ControllerBase
{
    private Guid Me   => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Role => User.FindFirstValue(ClaimTypes.Role)!;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status = null)
    {
        var query = db.Invoices.Include(i => i.Client).Include(i => i.Freelancer)
            .Include(i => i.Project).Include(i => i.LineItems).AsQueryable();
        if (Role == "client") { var c = await db.Clients.FirstOrDefaultAsync(x => x.UserId == Me); query = query.Where(i => i.ClientId == c!.Id); }
        else if (Role == "freelancer") { var f = await db.Freelancers.FirstOrDefaultAsync(x => x.UserId == Me); query = query.Where(i => i.FreelancerId == f!.Id); }
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(i => i.Status == status);
        return Ok(await query.OrderByDescending(i => i.IssuedAt).Select(i => Map(i)).ToListAsync());
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var i = await db.Invoices.Include(x => x.Client).Include(x => x.Freelancer)
            .Include(x => x.Project).Include(x => x.LineItems).FirstOrDefaultAsync(x => x.Id == id);
        return i is null ? NotFound() : Ok(Map(i));
    }

    /// <summary>Admin marks invoice paid — sends bank details to client via email</summary>
    [HttpPost("{id:guid}/send-payment-instructions"), Authorize(Roles = "admin")]
    public async Task<IActionResult> SendInstructions(Guid id)
    {
        var invoice = await db.Invoices.Include(i => i.Client).ThenInclude(c => c.User)
            .FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return NotFound();

        // Bank details from config/settings
        var bankDetails = await db.PlatformSettings
            .Where(s => s.Key.StartsWith("bank."))
            .ToDictionaryAsync(s => s.Key.Replace("bank.", ""), s => s.Value);

        var bankInfo = string.Join("\n", bankDetails.Select(kv => $"{kv.Key}: {kv.Value}"));
        if (string.IsNullOrEmpty(bankInfo))
            bankInfo = "Bank: HDFC Bank\nAccount Name: WorkSupport360 Pvt Ltd\nAccount Number: XXXXXXXXXX\nIFSC: HDFC0000001\nUPI: help@worksupport360.com";

        invoice.PaymentInstructions = bankInfo;
        invoice.Status = "pending";
        await db.SaveChangesAsync();

        await email.SendPaymentInstructionsAsync(
            invoice.Client.User.Email, invoice.Client.ContactName,
            invoice.InvoiceNumber, invoice.Total, invoice.Currency,
            invoice.ApplyGst, invoice.GstAmount, bankInfo);

        return Ok(new { message = "Payment instructions sent to client" });
    }

    /// <summary>Mark invoice as paid — creates payment, logs commission, sends confirmation</summary>
    [HttpPatch("{id:guid}/mark-paid"), Authorize(Roles = "admin")]
    public async Task<IActionResult> MarkPaid(Guid id, [FromBody] RecordPaymentDto dto)
    {
        var invoice = await db.Invoices
            .Include(i => i.Freelancer).ThenInclude(f => f.User)
            .Include(i => i.Client).ThenInclude(c => c.User)
            .FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return NotFound();
        if (invoice.Status == "paid") return BadRequest(new { message = "Invoice already paid" });

        invoice.Status = "paid";
        invoice.PaidAt = DateTime.UtcNow;

        var payment = new Payment
        {
            InvoiceId = invoice.Id, ClientId = invoice.ClientId, FreelancerId = invoice.FreelancerId,
            Amount = invoice.Total, Commission = invoice.Commission,
            GstAmount = invoice.GstAmount, FreelancerAmount = invoice.FreelancerAmount,
            Currency = invoice.Currency, Status = "paid",
            Method = dto.Method, TransactionId = dto.TransactionId,
            PaymentNote = dto.PaymentNote, PaidAt = DateTime.UtcNow,
            PayoutStatus = "pending",
        };
        db.Payments.Add(payment);

        // Commission → PlatformEarnings
        db.PlatformEarnings.Add(new PlatformEarning
        {
            Source = "commission", Amount = invoice.Commission, Currency = invoice.Currency,
            RelatedEntityId = invoice.Id,
            Description = $"Commission {invoice.CommissionRate}% — {invoice.InvoiceNumber} — {invoice.Freelancer.AliasName}",
        });

        invoice.Freelancer.TotalEarned   += invoice.FreelancerAmount;
        invoice.Freelancer.PendingAmount  = Math.Max(0, invoice.Freelancer.PendingAmount - invoice.FreelancerAmount);
        invoice.Client.TotalSpent        += invoice.Total;

        // Update project
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == invoice.ProjectId);
        if (project is not null)
        {
            project.TotalPaid    += invoice.Total;
            project.PendingAmount = Math.Max(0, project.PendingAmount - invoice.Total);
            if (project.Status == "pending_payment")
            {
                project.Status = "active";
                project.StatusLogs.Add(new ProjectStatusLog
                {
                    OldStatus = "pending_payment", NewStatus = "active",
                    Reason = $"Payment received — {invoice.InvoiceNumber}", ChangedBy = Me,
                });
            }
        }

        // Notifications
        db.Notifications.Add(new Notification {
            UserId = invoice.Freelancer.UserId, Type = "payment", Priority = "high",
            Title = "Payment received 💰",
            Message = $"{invoice.Currency} {invoice.FreelancerAmount:N2} for {invoice.InvoiceNumber}. Payout processing.",
            ActionUrl = "/freelancer/earnings",
        });
        db.Notifications.Add(new Notification {
            UserId = invoice.Client.UserId, Type = "payment", Priority = "normal",
            Title = "Payment confirmed ✅",
            Message = $"Payment of {invoice.Currency} {invoice.Total:N2} for {invoice.InvoiceNumber} received.",
            ActionUrl = "/client/invoices",
        });

        await db.SaveChangesAsync();

        // Emails
        await email.SendPaymentConfirmationAsync(invoice.Client.User.Email, invoice.Client.ContactName,
            invoice.Total, invoice.Currency, invoice.InvoiceNumber);
        await email.SendPaymentConfirmationAsync(invoice.Freelancer.User.Email, invoice.Freelancer.AliasName,
            invoice.FreelancerAmount, invoice.Currency, invoice.InvoiceNumber);

        return Ok(new { message = "Invoice paid — commission logged", paymentId = payment.Id });
    }

    /// <summary>Send payment reminder to client</summary>
    [HttpPost("{id:guid}/send-reminder"), Authorize(Roles = "admin")]
    public async Task<IActionResult> SendReminder(Guid id)
    {
        var invoice = await db.Invoices.Include(i => i.Client).ThenInclude(c => c.User)
            .FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return NotFound();
        invoice.RemindersSent++;
        invoice.LastReminderAt = DateTime.UtcNow;

        db.Notifications.Add(new Notification {
            UserId = invoice.Client.UserId, Type = "payment", Priority = "urgent",
            Title = $"Payment reminder — {invoice.InvoiceNumber} ⚠️",
            Message = $"Invoice {invoice.InvoiceNumber} for {invoice.Currency} {invoice.Total:N2} is due. Please pay soon.",
            ActionUrl = "/client/invoices",
        });

        await db.SaveChangesAsync();
        await email.SendPaymentReminderAsync(invoice.Client.User.Email, invoice.Client.ContactName,
            invoice.InvoiceNumber, invoice.Total, invoice.Currency, invoice.DueAt, invoice.RemindersSent);

        return Ok(new { message = $"Reminder #{invoice.RemindersSent} sent" });
    }

    [HttpPost("mark-overdue"), Authorize(Roles = "admin")]
    public async Task<IActionResult> MarkOverdue()
    {
        var overdue = await db.Invoices.Include(i => i.Client).ThenInclude(c => c.User)
            .Where(i => i.Status == "pending" && i.DueAt < DateTime.UtcNow).ToListAsync();
        foreach (var inv in overdue)
        {
            inv.Status = "overdue";
            db.Notifications.Add(new Notification {
                UserId = inv.Client.UserId, Type = "payment", Priority = "urgent",
                Title = $"Invoice OVERDUE — {inv.InvoiceNumber} ⚠️",
                Message = $"Invoice {inv.InvoiceNumber} for {inv.Currency} {inv.Total:N2} is overdue!",
                ActionUrl = "/client/invoices",
            });
        }
        await db.SaveChangesAsync();
        return Ok(new { message = $"{overdue.Count} invoices marked overdue" });
    }

    private static InvoiceDto Map(Invoice i) => new(
        i.Id, i.InvoiceNumber, i.Client?.CompanyName ?? "", i.Freelancer?.AliasName ?? "",
        i.Project?.Name ?? "",
        i.LineItems.Select(li => new InvoiceLineItemDto(li.Description, li.Hours, li.Rate, li.Amount)).ToList(),
        i.Subtotal, i.Commission, i.CommissionRate,
        i.ApplyGst, i.GstRate, i.GstAmount,
        i.Total, i.FreelancerAmount, i.Currency, i.Status,
        i.PaymentInstructions, i.IssuedAt, i.DueAt, i.PaidAt, i.RemindersSent);
}
