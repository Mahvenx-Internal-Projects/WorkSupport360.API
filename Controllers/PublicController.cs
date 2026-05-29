using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using WorkSupport360.API.Data;
using WorkSupport360.API.DTOs;
using WorkSupport360.API.Models;
using WorkSupport360.API.Services;

namespace WorkSupport360.API.Controllers;

[ApiController, Route("api/public")]
public class PublicController(AppDbContext db, IEmailService email) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> Stats() => Ok(new {
        totalFreelancers  = await db.Freelancers.CountAsync(f => f.IsVerified),
        availableNow      = await db.Freelancers.CountAsync(f => f.IsAvailable && f.IsVerified),
        totalClients      = await db.Clients.CountAsync(),
        completedProjects = await db.Projects.CountAsync(p => p.Status == "completed"),
        totalPaidOut      = Math.Round(await db.Payments.Where(p => p.Status == "paid").SumAsync(p => (decimal?)p.FreelancerAmount) ?? 0, 0),
        avgRating         = Math.Round(await db.Freelancers.Where(f => f.ReviewCount > 0).AverageAsync(f => (double?)f.Rating) ?? 4.8, 1),
        successRate       = 98,
        countriesServed   = 24,
    });

    [HttpGet("featured-freelancers")]
    public async Task<IActionResult> FeaturedFreelancers(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 8,
        [FromQuery] string? skill = null, [FromQuery] bool? availableOnly = null,
        [FromQuery] decimal? minRate = null, [FromQuery] decimal? maxRate = null,
        [FromQuery] int? minExp = null)
    {
        // ONLY verified freelancers show on homepage
        var query = db.Freelancers.Include(f => f.Skills).Include(f => f.Badges)
            .Where(f => f.IsVerified)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(skill))  query = query.Where(f => f.Skills.Any(s => s.Skill.Contains(skill)));
        if (availableOnly == true) query = query.Where(f => f.IsAvailable);
        if (minRate.HasValue)      query = query.Where(f => f.HourlyRate >= minRate);
        if (maxRate.HasValue)      query = query.Where(f => f.HourlyRate <= maxRate);
        if (minExp.HasValue)       query = query.Where(f => f.TotalExp >= minExp);

        var boosted = db.FeaturedBoosts.Where(fb => fb.IsActive && fb.StartsAt <= DateTime.UtcNow && fb.EndsAt >= DateTime.UtcNow).Select(fb => fb.FreelancerId);
        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(f => boosted.Contains(f.Id)).ThenByDescending(f => f.TrustScore).ThenByDescending(f => f.Rating)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(f => new {
                f.Id, f.AliasName, f.CurrentRole, f.TotalExp, f.FreelanceExp,
                f.HourlyRate, f.Currency, f.Rating, f.ReviewCount,
                f.TrustScore, f.Tier, f.IsAvailable, f.Country, f.Timezone,
                f.CompletedProjects, f.ResponseTimeMinutes, f.Bio,
                Skills = f.Skills.Select(s => s.Skill).Take(5).ToList(),
                Badges = f.Badges.Select(b => b.Badge).ToList(),
                IsFeatured = boosted.Contains(f.Id),
            }).ToListAsync();
        return Ok(new { items, total, page, pageSize, totalPages = (int)Math.Ceiling((double)total / pageSize) });
    }

    [HttpGet("faqs")]
    public async Task<IActionResult> GetFaqs([FromQuery] string? category = null)
    {
        var q = db.Faqs.Where(f => f.IsActive).AsQueryable();
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(f => f.Category == category);
        return Ok(await q.OrderBy(f => f.Category).ThenBy(f => f.SortOrder)
            .Select(f => new { f.Id, f.Category, f.Question, f.Answer, f.HelpfulCount }).ToListAsync());
    }

    [HttpPost("faqs/{id:guid}/helpful")]
    public async Task<IActionResult> MarkHelpful(Guid id)
    {
        await db.Faqs.Where(f => f.Id == id).ExecuteUpdateAsync(s => s.SetProperty(f => f.HelpfulCount, f => f.HelpfulCount + 1));
        return Ok();
    }

    [HttpGet("plans")]
    public async Task<IActionResult> GetPlans()
    {
        var plans = await db.SubscriptionPlans
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();

        var result = plans.Select(p => new
        {
            p.Id,
            p.PlanKey,
            p.Name,
            p.PriceMonthly,
            p.PriceYearly,
            p.HoursIncluded,
            p.OverageRatePerHr,
            p.CommissionRate,
            p.MaxProjects,
            p.HasPrioritySupport,
            p.HasDedicatedManager,

            Features = string.IsNullOrEmpty(p.FeaturesJson)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(p.FeaturesJson)
        });

        return Ok(result);
    }

    [HttpPost("contact")]
    public async Task<IActionResult> Contact([FromBody] ContactFormDto dto)
    {
        db.ContactSubmissions.Add(new ContactSubmission { Name = dto.Name, Email = dto.Email, Reason = dto.Reason, Message = dto.Message });
        await db.SaveChangesAsync();
        await email.SendContactUsEmailAsync(dto.Name, dto.Email, dto.Reason, dto.Message);
        return Ok(new { message = "Thank you! We'll respond within 24 hours." });
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings() =>
        Ok(await db.PlatformSettings.Where(s => s.Key.StartsWith("public."))
            .ToDictionaryAsync(s => s.Key.Replace("public.", ""), s => s.Value));

    [HttpGet("quick-support/available")]
    public async Task<IActionResult> QuickAvailable([FromQuery] string? skill = null)
    {
        var q = db.Freelancers.Include(f => f.Skills).Where(f => f.IsAvailable && f.IsVerified).AsQueryable();
        if (!string.IsNullOrWhiteSpace(skill)) q = q.Where(f => f.Skills.Any(s => s.Skill.Contains(skill)));
        return Ok(await q.OrderByDescending(f => f.TrustScore).Take(16)
            .Select(f => new { f.Id, f.AliasName, f.CurrentRole, f.HourlyRate, f.Currency, f.Rating, f.TrustScore, f.ResponseTimeMinutes, f.Bio,
                Skills = f.Skills.Select(s => s.Skill).Take(4).ToList() }).ToListAsync());
    }
}

public record ContactFormDto(string Name, string Email, string Reason, string Message);

[ApiController, Route("api/subscriptions"), Authorize(Roles = "client")]
public class SubscriptionsController(AppDbContext db, IEmailService email) : ControllerBase
{
    private Guid Me => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("mine")]
    public async Task<IActionResult> GetMine()
    {
        var client = await db.Clients.FirstOrDefaultAsync(c => c.UserId == Me);
        if (client is null) return Forbid();
        var sub = await db.ClientSubscriptions.Include(s => s.Plan).Where(s => s.ClientId == client.Id && s.Status == "active").OrderByDescending(s => s.StartDate).FirstOrDefaultAsync();
        return Ok(new {
            planKey = sub?.Plan?.PlanKey ?? client.Plan,
            planName = sub?.Plan?.Name ?? client.Plan.ToUpper(),
            hoursIncluded = sub?.Plan?.HoursIncluded ?? client.HoursIncluded,
            hoursUsed = client.HoursUsed,
            hoursRemaining = Math.Max(0, (sub?.Plan?.HoursIncluded ?? client.HoursIncluded) - client.HoursUsed),
            commissionRate = sub?.Plan?.CommissionRate ?? 15,
            status = sub?.Status ?? "payg", endDate = sub?.EndDate,
        });
    }

    [HttpPost]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeDto dto)
    {
        var client = await db.Clients.Include(c => c.User).FirstOrDefaultAsync(c => c.UserId == Me);
        if (client is null) return Forbid();
        var plan = await db.SubscriptionPlans.FirstOrDefaultAsync(p => p.PlanKey == dto.PlanKey);
        if (plan is null) return NotFound(new { message = "Plan not found" });
        var price   = dto.BillingCycle == "yearly" ? plan.PriceYearly : plan.PriceMonthly;
        var endDate = dto.BillingCycle == "yearly" ? DateTime.UtcNow.AddYears(1) : DateTime.UtcNow.AddMonths(1);
        await db.ClientSubscriptions.Where(s => s.ClientId == client.Id && s.Status == "active").ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "cancelled").SetProperty(x => x.CancelledAt, DateTime.UtcNow));
        var sub = new ClientSubscription { ClientId = client.Id, PlanId = plan.Id, BillingCycle = dto.BillingCycle, AmountPaid = price, Currency = dto.Currency ?? "USD", Status = "active", StartDate = DateTime.UtcNow, EndDate = endDate, PaymentMethod = dto.PaymentMethod };
        db.ClientSubscriptions.Add(sub);
        client.Plan = plan.PlanKey; client.HoursIncluded = plan.HoursIncluded; client.HoursUsed = 0;
        db.PlatformEarnings.Add(new PlatformEarning { Source = "subscription", Amount = price, Currency = dto.Currency ?? "USD", RelatedEntityId = sub.Id, Description = $"{client.CompanyName} → {plan.Name}" });
        db.Notifications.Add(new Notification { UserId = client.UserId, Type = "system", Priority = "normal", Title = $"Welcome to {plan.Name}!", Message = $"{plan.HoursIncluded} hrs/month, {plan.CommissionRate}% commission." });
        await db.SaveChangesAsync();
        if (client.User?.Email is not null)
            await email.SendSubscriptionConfirmationAsync(client.User.Email, client.ContactName, plan.Name, price, dto.Currency ?? "USD", dto.BillingCycle, endDate);
        return Ok(new { message = "Subscription activated!", planName = plan.Name, endDate });
    }
}
public record SubscribeDto(string PlanKey, string BillingCycle, string? Currency, string? PaymentMethod);

[ApiController, Route("api/quick-support"), Authorize]
public class QuickSupportController(AppDbContext db, IEmailService email) : ControllerBase
{
    private Guid Me => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("book")]
    public async Task<IActionResult> Book([FromBody] BookQuickSupportDto dto)
    {
        var fl = await db.Freelancers.Include(f => f.User).FirstOrDefaultAsync(f => f.Id == dto.FreelancerId);
        if (fl is null) return NotFound();
        var client = await db.Clients.Include(c => c.User).FirstOrDefaultAsync(c => c.UserId == Me);

        var fee = fl.HourlyRate * 0.20m;
        var session = new QuickSupportSession
        {
            ClientId = client?.Id, FreelancerId = dto.FreelancerId,
            Topic = dto.Topic, Rate = fl.HourlyRate, Currency = fl.Currency,
            Platform = dto.Platform ?? "zoom", PlatformFee = fee,
            ClientContactEmail = dto.ClientEmail,
        };
        db.QuickSupportSessions.Add(session);
        db.PlatformEarnings.Add(new PlatformEarning { Source = "quick_support", Amount = fee, Currency = fl.Currency, RelatedEntityId = session.Id, Description = $"Quick support — {fl.AliasName}" });
        db.Notifications.Add(new Notification { UserId = fl.UserId, Type = "request", Priority = "urgent", Title = "⚡ Quick support request!", Message = $"Topic: {dto.Topic}. Client joining on {dto.Platform ?? "zoom"} shortly." });
        await db.SaveChangesAsync();
        if (!string.IsNullOrEmpty(dto.ClientEmail))
            await email.SendQuickSupportConfirmationAsync(dto.ClientEmail, dto.ClientName ?? "Client", fl.AliasName, fl.HourlyRate, fl.Currency, session.Id);
        return Ok(new { sessionId = session.Id, message = "Session booked! Expert connects in ~30 minutes." });
    }
}

[ApiController, Route("api/support"), Authorize]
public class SupportController(AppDbContext db) : ControllerBase
{
    private Guid Me => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("tickets")]
    public async Task<IActionResult> GetTickets() =>
        Ok(await db.SupportTickets.Include(t => t.Messages).Where(t => t.UserId == Me).OrderByDescending(t => t.CreatedAt)
            .Select(t => new { t.Id, t.Subject, t.Category, t.Status, t.Priority, t.CreatedAt, MessageCount = t.Messages.Count }).ToListAsync());

    [HttpPost("tickets")]
    public async Task<IActionResult> Create([FromBody] CreateTicketDto dto)
    {
        var ticket = new SupportTicket { UserId = Me, Subject = dto.Subject, Category = dto.Category, Priority = dto.Priority };
        db.SupportTickets.Add(ticket);
        db.SupportMessages.Add(new SupportMessage { TicketId = ticket.Id, SenderId = Me, SenderRole = "user", Content = dto.Message });
        var ai = GetAiReply(dto.Message);
        if (ai is not null) db.SupportMessages.Add(new SupportMessage { TicketId = ticket.Id, SenderId = Guid.Empty, SenderRole = "ai", Content = ai, IsAi = true });
        await db.SaveChangesAsync();
        return Ok(new { ticketId = ticket.Id });
    }

    [HttpGet("tickets/{id:guid}/messages")]
    public async Task<IActionResult> GetMessages(Guid id) =>
        Ok(await db.SupportMessages.Where(m => m.TicketId == id).OrderBy(m => m.SentAt)
            .Select(m => new { m.Id, m.SenderRole, m.Content, m.IsAi, m.SentAt }).ToListAsync());

    [HttpPost("tickets/{id:guid}/messages")]
    public async Task<IActionResult> SendMessage(Guid id, [FromBody] string content)
    {
        var ticket = await db.SupportTickets.FindAsync(id);
        if (ticket is null || ticket.UserId != Me) return NotFound();
        db.SupportMessages.Add(new SupportMessage { TicketId = id, SenderId = Me, SenderRole = "user", Content = content });
        var ai = GetAiReply(content);
        if (ai is not null) db.SupportMessages.Add(new SupportMessage { TicketId = id, SenderId = Guid.Empty, SenderRole = "ai", Content = ai, IsAi = true });
        await db.SaveChangesAsync();
        return Ok();
    }

    private static string? GetAiReply(string msg)
    {
        var l = msg.ToLower();
        if (l.Contains("payment") || l.Contains("invoice")) return "Payments are processed within 3 business days after timesheet approval. All invoices visible under Invoices section. Email help@worksupport360.com for specific queries.";
        if (l.Contains("cancel") || l.Contains("subscription")) return "Cancel anytime from Settings → Subscription. Access continues until billing period ends.";
        if (l.Contains("hire") || l.Contains("expert")) return "Browse experts → Click Request → Admin schedules demo in 4 hrs → Approve → Project starts!";
        if (l.Contains("password") || l.Contains("login")) return "Use 'Forgot password' on login page, or sign in with Google. Still stuck? Email help@worksupport360.com";
        if (l.Contains("gst") || l.Contains("tax")) return "GST (18%) applies for Indian clients on projects. GST number can be added to your profile. All invoices are GST-compliant.";
        if (l.Contains("bank") || l.Contains("payout")) return "Freelancers: Add bank details in My Profile → Bank Details. Payouts are processed within 3 days of client payment. WhatsApp +91-9441363687 for payout queries.";
        return null;
    }
}
public record CreateTicketDto(string Subject, string Category, string Priority, string Message);

