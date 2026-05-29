using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkSupport360.API.Data;
using WorkSupport360.API.DTOs;
using WorkSupport360.API.Models;

namespace WorkSupport360.API.Controllers;

[ApiController, Route("api/admin"), Authorize(Roles = "admin")]
public class AdminController(AppDbContext db) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var lastMonth  = monthStart.AddMonths(-1);

        var revenueThis  = await db.Payments.Where(p => p.Status == "paid" && p.PaidAt >= monthStart).SumAsync(p => (decimal?)p.Amount) ?? 0;
        var revenueLast  = await db.Payments.Where(p => p.Status == "paid" && p.PaidAt >= lastMonth && p.PaidAt < monthStart).SumAsync(p => (decimal?)p.Amount) ?? 0;
        var commission   = await db.Payments.Where(p => p.Status == "paid" && p.PaidAt >= monthStart).SumAsync(p => (decimal?)p.Commission) ?? 0;
        var growth       = revenueLast == 0 ? 0 : Math.Round((revenueThis - revenueLast) / revenueLast * 100, 1);
        var pendingPayouts = await db.Payments.CountAsync(p => p.Status == "paid" && p.PayoutStatus == "pending");
        var pendingInvoiceAmount = await db.Invoices
    .Where(i => i.Status == "pending" || i.Status == "overdue")
    .SumAsync(i => (decimal?)i.Total) ?? 0;
        return Ok(new AdminStatsDto(
     revenueThis,
     await db.Projects.CountAsync(p => p.Status == "active"),
     await db.DemoRequests.CountAsync(r => r.Status == "pending"),
     commission,
     await db.Freelancers.CountAsync(),
     await db.Clients.CountAsync(),
     Convert.ToDecimal(
         Math.Round(
             await db.Freelancers
                 .Where(f => f.ReviewCount > 0)
                 .AverageAsync(f => (double?)f.Rating) ?? 0,
             2
         )
     ),
     growth,
     pendingPayouts,
     pendingInvoiceAmount
 ));
    }

    [HttpGet("attendance")]
    public async Task<IActionResult> GetAttendance([FromQuery] Guid? userId = null, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        var query = db.AttendanceLogs.Include(a => a.User).AsQueryable();
        if (userId.HasValue) query = query.Where(a => a.UserId == userId);
        if (from.HasValue)   query = query.Where(a => a.Timestamp >= from);
        if (to.HasValue)     query = query.Where(a => a.Timestamp <= to);
        var items = await query.OrderByDescending(a => a.Timestamp)
            .Select(a => new { a.Id, a.UserId, UserName = a.User.Name, UserRole = a.User.Role, a.Action, a.Timestamp, a.Note })
            .Take(500).ToListAsync();
        return Ok(items);
    }

    [HttpGet("leaderboard")]
    public async Task<IActionResult> Leaderboard()
    {
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var topEarners = await db.Payments
            .Where(p => p.Status == "paid" && p.PaidAt >= monthStart)
            .GroupBy(p => p.FreelancerId)
            .Select(g => new { FreelancerId = g.Key, Earnings = g.Sum(p => p.FreelancerAmount) })
            .OrderByDescending(x => x.Earnings).Take(10)
            .Join(db.Freelancers, x => x.FreelancerId, f => f.Id, (x, f) => new { x.Earnings, f.AliasName, f.Rating, f.CompletedProjects })
            .ToListAsync();

        return Ok(topEarners.Select((d, i) => new LeaderboardEntryDto(
            i + 1, d.AliasName, d.Earnings, d.Rating, d.CompletedProjects,
            i == 0 ? "Top Earner 🥇" : i == 1 ? "Rising Star 🥈" : i == 2 ? "5-Star Streak 🥉" : null)));
    }

    [HttpGet("reports/revenue")]
    public async Task<IActionResult> RevenueReport()
    {
        var months = Enumerable.Range(0, 12).Select(i => new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(-i))
            .OrderBy(d => d).ToList();
        var payments = await db.Payments.Where(p => p.Status == "paid" && p.PaidAt >= months[0]).ToListAsync();
        return Ok(months.Select(m => new
        {
            Month      = m.ToString("MMM yyyy"),
            Revenue    = payments.Where(p => p.PaidAt!.Value.Year == m.Year && p.PaidAt.Value.Month == m.Month).Sum(p => p.Amount),
            Commission = payments.Where(p => p.PaidAt!.Value.Year == m.Year && p.PaidAt.Value.Month == m.Month).Sum(p => p.Commission),
            GstAmount  = payments.Where(p => p.PaidAt!.Value.Year == m.Year && p.PaidAt.Value.Month == m.Month).Sum(p => p.GstAmount),
        }));
    }

    [HttpGet("revenue/breakdown")]
    public async Task<IActionResult> RevenueBreakdown()
    {
        var earnings = await db.PlatformEarnings.GroupBy(e => e.Source)
            .Select(g => new { Source = g.Key, Total = g.Sum(e => e.Amount), Count = g.Count() }).ToListAsync();
        var pendingPayouts = await db.Payments.Where(p => p.PayoutStatus == "pending" && p.Status == "paid")
            .SumAsync(p => (decimal?)p.FreelancerAmount) ?? 0;
        return Ok(new { breakdown = earnings, pendingPayouts, quickSessionsToday = await db.QuickSupportSessions.CountAsync(q => q.CreatedAt.Date == DateTime.UtcNow.Date) });
    }

    [HttpPatch("users/{userId:guid}/role")]
    public async Task<IActionResult> SetRole(Guid userId, [FromBody] string newRole)
    {
        if (newRole is not ("admin" or "freelancer" or "client")) return BadRequest(new { message = "Invalid role" });
        var user = await db.Users.FindAsync(userId);
        if (user is null) return NotFound();
        user.Role = newRole;
        await db.SaveChangesAsync();
        return Ok(new { message = $"Role updated to {newRole}" });
    }

    [HttpPatch("freelancers/{id:guid}/verify")]
    public async Task<IActionResult> VerifyFreelancer(Guid id, [FromBody] bool isVerified)
    {
        var fl = await db.Freelancers.FindAsync(id);
        if (fl is null) return NotFound();
        fl.IsVerified = isVerified;
        if (isVerified) fl.IsAvailable = true;  // make available after admin verification

        db.Notifications.Add(new Notification {
            UserId = fl.UserId, Type = "system", Priority = "high",
            Title = isVerified ? "Profile verified! ✅" : "Profile verification removed",
            Message = isVerified
                ? "Your profile is now verified and visible to clients. You can start receiving project requests!"
                : "Your profile verification has been updated. Contact admin for details.",
        });
        await db.SaveChangesAsync();
        return Ok(new { message = $"Freelancer {(isVerified ? "verified" : "unverified")}" });
    }

    [HttpGet("pending-payouts")]
    public async Task<IActionResult> PendingPayouts()
    {
        var items = await db.Payments
            .Include(p => p.Freelancer).ThenInclude(f => f.User)
            .Include(p => p.Invoice)
            .Where(p => p.Status == "paid" && p.PayoutStatus == "pending")
            .OrderBy(p => p.PaidAt)
            .Select(p => new {
                p.Id, InvoiceNumber = p.Invoice.InvoiceNumber,
                FreelancerName = p.Freelancer.AliasName, FreelancerEmail = p.Freelancer.User.Email,
                BankAccountName = p.Freelancer.BankAccountName, BankAccount = p.Freelancer.BankAccountNumber,
                IfscCode = p.Freelancer.BankIfscCode, UpiId = p.Freelancer.UpiId,
                p.FreelancerAmount, p.Currency, p.PaidAt,
            }).ToListAsync();
        return Ok(items);
    }
}
