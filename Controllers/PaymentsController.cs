using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WorkSupport360.API.Data;
using WorkSupport360.API.DTOs;
using WorkSupport360.API.Models;
using WorkSupport360.API.Services;

namespace WorkSupport360.API.Controllers;

[ApiController, Route("api/payments"), Authorize]
public class PaymentsController(AppDbContext db, IEmailService email) : ControllerBase
{
    private Guid Me   => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Role => User.FindFirstValue(ClaimTypes.Role)!;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var query = db.Payments.Include(p => p.Invoice).Include(p => p.Client).Include(p => p.Freelancer).AsQueryable();
        if (Role == "client") { var c = await db.Clients.FirstOrDefaultAsync(x => x.UserId == Me); query = query.Where(p => p.ClientId == c!.Id); }
        else if (Role == "freelancer") { var f = await db.Freelancers.FirstOrDefaultAsync(x => x.UserId == Me); query = query.Where(p => p.FreelancerId == f!.Id); }
        return Ok(await query.OrderByDescending(p => p.CreatedAt).Select(p => Map(p)).ToListAsync());
    }

    /// <summary>Admin records payout to freelancer (sends from WS360 to freelancer's bank)</summary>
    [HttpPost("{id:guid}/record-payout"), Authorize(Roles = "admin")]
    public async Task<IActionResult> RecordPayout(Guid id, [FromBody] RecordPayoutDto dto)
    {
        var payment = await db.Payments
            .Include(p => p.Freelancer).ThenInclude(f => f.User)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (payment is null) return NotFound();
        if (payment.PayoutStatus == "paid") return BadRequest(new { message = "Already paid out" });

        payment.PayoutStatus        = "paid";
        payment.PayoutDate          = DateTime.UtcNow;
        payment.PayoutTransactionId = dto.PayoutTransactionId;

        db.Notifications.Add(new Notification {
            UserId = payment.Freelancer.UserId, Type = "payment", Priority = "high",
            Title = "Payout processed! 💸",
            Message = $"{payment.Currency} {payment.FreelancerAmount:N2} has been transferred to your bank account.",
            ActionUrl = "/freelancer/earnings",
        });

        await db.SaveChangesAsync();

        await email.SendPayoutNotificationAsync(
            payment.Freelancer.User.Email, payment.Freelancer.AliasName,
            payment.FreelancerAmount, payment.Currency,
            payment.Invoice.InvoiceNumber, dto.PayoutTransactionId);

        return Ok(new { message = "Payout recorded — freelancer notified" });
    }

    private static PaymentDto Map(Payment p) => new(
        p.Id, p.Invoice?.InvoiceNumber ?? "", p.Amount, p.Commission,
        p.GstAmount, p.FreelancerAmount, p.Currency,
        p.Status, p.Method, p.TransactionId,
        p.PayoutStatus, p.PayoutDate, p.CreatedAt, p.PaidAt);
}
