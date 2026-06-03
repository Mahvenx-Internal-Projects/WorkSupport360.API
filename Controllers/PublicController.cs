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
            .Select(p => new
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
                p.FeaturesJson
            })
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
            Features = string.IsNullOrWhiteSpace(p.FeaturesJson)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(p.FeaturesJson) ?? new List<string>()
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

// ══════════════════════════════════════════════════════════════
// SUPPORT — PUBLIC (no auth needed for initial bot flow)
// ══════════════════════════════════════════════════════════════
[ApiController, Route("api/support")]
public class SupportController(AppDbContext db, IEmailService email) : ControllerBase
{
    private Guid? MeOrNull => User.Identity?.IsAuthenticated == true
        ? Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)
        : null;

    // ── Create ticket (bot creates it, guest or logged-in) ────
    [HttpPost("tickets")]
    public async Task<IActionResult> Create([FromBody] CreateSupportTicketDto dto)
    {
        var userId = MeOrNull;

        // If guest — create a ghost user entry or use Guid.Empty
        if (userId is null)
        {
            // Guest ticket — we store contact info in ticket itself
            // For guests: use a special "guest" user or null foreign key
        // We make UserId nullable-safe by using a placeholder system user or admin
        var guestUserId = await db.Users
            .Where(u => u.Role == "admin")
            .Select(u => u.Id)
            .FirstOrDefaultAsync();

        var ticket = new SupportTicket
            {
                UserId       = guestUserId, // use admin as placeholder for guest tickets
                Subject      = dto.Subject ?? "Support Request",
                Category     = dto.Category ?? "general",
                Priority     = dto.Priority ?? "normal",
                Status       = "open",
                UserType     = dto.UserType ?? "visitor",
                BotSummary   = dto.BotSummary,
                ContactEmail = dto.ContactEmail,
                ContactPhone = string.IsNullOrWhiteSpace(dto.ContactPhone) ? null : dto.ContactPhone,
                LastMessageAt = DateTime.UtcNow,
            };
            db.SupportTickets.Add(ticket);
            if (!string.IsNullOrEmpty(dto.FirstMessage))
                db.SupportMessages.Add(new SupportMessage { TicketId = ticket.Id, SenderId = Guid.Empty, SenderRole = "user", Content = dto.FirstMessage });
            await db.SaveChangesAsync();

            // Notify all admins
            await NotifyAdmins(ticket.Id, dto.Subject, dto.ContactEmail ?? "Guest", dto.FirstMessage ?? "");
            return Ok(new { ticketId = ticket.Id, isGuest = true });
        }

        var authTicket = new SupportTicket
        {
            UserId       = userId.Value,
            Subject      = dto.Subject ?? "Support Request",
            Category     = dto.Category ?? "general",
            Priority     = dto.Priority ?? "normal",
            Status       = "open",
            UserType     = dto.UserType,
            BotSummary   = dto.BotSummary,
            ContactEmail = dto.ContactEmail,
            ContactPhone = dto.ContactPhone,
            LastMessageAt = DateTime.UtcNow,
        };
        db.SupportTickets.Add(authTicket);
        if (!string.IsNullOrEmpty(dto.FirstMessage))
            db.SupportMessages.Add(new SupportMessage { TicketId = authTicket.Id, SenderId = userId.Value, SenderRole = "user", Content = dto.FirstMessage });
        await db.SaveChangesAsync();

        await AutoAssign(authTicket.Id, db);
        await NotifyAdmins(authTicket.Id, dto.Subject ?? "Support Request", dto.ContactEmail ?? "", dto.FirstMessage ?? "");

        // Notify user about ticket + who's assigned
        var assigned = await db.SupportTickets.Where(t => t.Id == authTicket.Id).Select(t => t.AssignedAgentName).FirstOrDefaultAsync();
        db.Notifications.Add(new Notification {
            UserId = userId.Value, Type = "support", Priority = "normal",
            Title  = "Support ticket created ✅",
            Message = assigned != null
                ? $"Ticket #{authTicket.Id.ToString()[..8]} assigned to {assigned}. They will respond shortly."
                : $"Ticket #{authTicket.Id.ToString()[..8]} created. An agent will pick it up shortly.",
        });
        await db.SaveChangesAsync();
        return Ok(new { ticketId = authTicket.Id, isGuest = false });
    }

    // ── Get messages for a ticket (by ticketId, no auth — guest uses ticketId as key) ──
    [HttpGet("tickets/{id:guid}/messages")]
    public async Task<IActionResult> GetMessages(Guid id)
    {
        var msgs = await db.SupportMessages
            .Where(m => m.TicketId == id)
            .OrderBy(m => m.SentAt)
            .Select(m => new { m.Id, m.SenderRole, m.Content, m.IsAi, m.SentAt })
            .ToListAsync();
        return Ok(msgs);
    }

    // ── User sends a message ───────────────────────────────────
    [HttpPost("tickets/{id:guid}/messages")]
    public async Task<IActionResult> SendMessage(Guid id, [FromBody] string content)
    {
        var ticket = await db.SupportTickets.Include(t => t.Messages).FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound();

        var userId = MeOrNull ?? Guid.Empty;
        db.SupportMessages.Add(new SupportMessage { TicketId = id, SenderId = userId, SenderRole = "user", Content = content });
        ticket.LastMessageAt = DateTime.UtcNow;
        ticket.IsRead = false; // admin needs to re-read
        if (ticket.Status == "resolved") ticket.Status = "open"; // re-open if resolved
        await db.SaveChangesAsync();

        // Notify assigned agent
        if (ticket.AssignedAgentId.HasValue)
        {
            db.Notifications.Add(new Notification {
                UserId = ticket.AssignedAgentId.Value, Type = "support", Priority = "high",
                Title  = $"New message — Ticket #{id.ToString()[..8]}",
                Message = content.Length > 100 ? content[..100] + "..." : content,
                ActionUrl = "/admin/support",
            });
            await db.SaveChangesAsync();
        }

        return Ok();
    }

    // ── Get ticket status (for polling) ──────────────────────
    [HttpGet("tickets/{id:guid}/status")]
    public async Task<IActionResult> GetStatus(Guid id)
    {
        var t = await db.SupportTickets.FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return NotFound();
        return Ok(new {
            t.Status,
            t.AssignedAgentName,
            assignedAt  = t.AssignedAt,
            t.Priority,
            t.LastMessageAt,
            isAssigned  = t.AssignedAgentId.HasValue,
            // How long since ticket was created
            waitMinutes = (int)(DateTime.UtcNow - t.CreatedAt).TotalMinutes,
        });
    }

    // ── Get user's tickets ────────────────────────────────────
    [HttpGet("tickets"), Authorize]
    public async Task<IActionResult> GetMyTickets()
    {
        var userId = MeOrNull!.Value;
        return Ok(await db.SupportTickets
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.LastMessageAt)
            .Select(t => new { t.Id, t.Subject, t.Category, t.Status, t.Priority,
                t.AssignedAgentName, t.CreatedAt, t.LastMessageAt,
                MessageCount = t.Messages.Count })
            .ToListAsync());
    }

    // ═══ ADMIN ENDPOINTS ══════════════════════════════════════

    // ── Admin: list all tickets ───────────────────────────────
    [HttpGet("admin/tickets"), Authorize(Roles = "admin,agent")]
    public async Task<IActionResult> AdminGetAll([FromQuery] string? status = null)
    {
        var q = db.SupportTickets.Include(t => t.User).AsQueryable();
        if (!string.IsNullOrEmpty(status)) q = q.Where(t => t.Status == status);
        var items = await q.OrderByDescending(t => t.LastMessageAt)
            .Select(t => new {
                t.Id, t.Subject, t.Category, t.Status, t.Priority,
                t.AssignedAgentName, t.UserType, t.ContactEmail, t.ContactPhone,
                t.BotSummary, t.IsRead, t.CreatedAt, t.LastMessageAt,
                UserName = t.User != null ? t.User.Name : "Guest",
                UserEmail = t.User != null ? t.User.Email : t.ContactEmail,
                MessageCount = t.Messages.Count,
            }).ToListAsync();
        return Ok(items);
    }

    // ── Admin: assign ticket to self ──────────────────────────
    [HttpPatch("admin/tickets/{id:guid}/assign"), Authorize(Roles = "admin,agent")]
    public async Task<IActionResult> Assign(Guid id)
    {
        var agentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var agent   = await db.Users.FindAsync(agentId);
        var ticket  = await db.SupportTickets.FindAsync(id);
        if (ticket is null) return NotFound();

        ticket.AssignedAgentId   = agentId;
        ticket.AssignedAgentName = agent?.Name ?? "Admin";
        ticket.AssignedAt        = DateTime.UtcNow;
        ticket.Status            = "assigned";
        ticket.IsRead            = true;
        await db.SaveChangesAsync();

        // Notify user
        if (ticket.UserId != Guid.Empty)
            db.Notifications.Add(new Notification {
                UserId = ticket.UserId, Type = "support", Priority = "high",
                Title  = "Agent assigned to your ticket 👤",
                Message = $"{agent?.Name ?? "Admin"} has picked up your support ticket and will respond shortly.",
            });
        await db.SaveChangesAsync();
        return Ok(new { message = $"Ticket assigned to {agent?.Name}" });
    }

    // ── Admin: send reply to user ─────────────────────────────
    [HttpPost("admin/tickets/{id:guid}/reply"), Authorize(Roles = "admin,agent")]
    public async Task<IActionResult> AdminReply(Guid id, [FromBody] AdminReplyDto dto)
    {
        var agentId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var ticket  = await db.SupportTickets.Include(t => t.User).FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound();

        db.SupportMessages.Add(new SupportMessage {
            TicketId   = id,
            SenderId   = agentId,
            SenderRole = "agent",
            Content    = dto.Message,
        });
        ticket.LastMessageAt = DateTime.UtcNow;
        if (dto.Resolve) { ticket.Status = "resolved"; ticket.ResolvedAt = DateTime.UtcNow; }
        else ticket.Status = "assigned";

        await db.SaveChangesAsync();

        // Notify user
        if (ticket.UserId != Guid.Empty)
        {
            db.Notifications.Add(new Notification {
                UserId = ticket.UserId, Type = "support", Priority = "high",
                Title  = dto.Resolve ? "Ticket resolved ✅" : "Agent replied to your ticket 💬",
                Message = dto.Message.Length > 100 ? dto.Message[..100] + "..." : dto.Message,
                ActionUrl = "/support",
            });
            await db.SaveChangesAsync();
        }

        // Send email if we have contact
        var contactEmail = ticket.ContactEmail ?? ticket.User?.Email;
        if (!string.IsNullOrEmpty(contactEmail))
            await email.SendAsync(contactEmail, ticket.User?.Name ?? "User",
                $"[WS360 Support] Response to: {ticket.Subject}",
                SupportReplyEmailBody(ticket.Subject, dto.Message, ticket.AssignedAgentName ?? "Support Team", dto.Resolve));

        return Ok(new { message = "Reply sent" });
    }

    // ── Admin: resolve / close ticket ─────────────────────────
    [HttpPatch("admin/tickets/{id:guid}/status"), Authorize(Roles = "admin,agent")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] string status)
    {
        var t = await db.SupportTickets.FindAsync(id);
        if (t is null) return NotFound();
        t.Status = status;
        if (status == "resolved") t.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok();
    }

    // ── Admin: unread count ───────────────────────────────────
    [HttpGet("admin/tickets/unread-count"), Authorize(Roles = "admin,agent")]
    public async Task<IActionResult> UnreadCount() =>
        Ok(new { count = await db.SupportTickets.CountAsync(t => !t.IsRead && t.Status != "closed") });

    // ── Helper: auto-assign ticket to least-busy agent ──────
    private static async Task AutoAssign(Guid ticketId, AppDbContext db)
    {
        // Get all agents + admins
        var agents = await db.Users
            .Where(u => u.Role == "agent" || u.Role == "admin")
            .Select(u => new { u.Id, u.Name, u.Role })
            .ToListAsync();

        if (!agents.Any()) return;

        // Prefer agents over admins; round-robin by open ticket count
        var agentIds = agents.Select(a => a.Id).ToList();
        var ticketCounts = await db.SupportTickets
            .Where(t => agentIds.Contains(t.AssignedAgentId ?? Guid.Empty)
                        && (t.Status == "assigned" || t.Status == "open"))
            .GroupBy(t => t.AssignedAgentId)
            .Select(g => new { AgentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AgentId, x => x.Count);

        // Pick agent with fewest open tickets (prefer role=agent over admin)
        var pick = agents
            .OrderBy(a => a.Role == "admin" ? 1 : 0) // agents first
            .ThenBy(a => ticketCounts.GetValueOrDefault(a.Id, 0))
            .First();

        var ticket = await db.SupportTickets.FindAsync(ticketId);
        if (ticket == null) return;

        ticket.AssignedAgentId   = pick.Id;
        ticket.AssignedAgentName = pick.Name;
        ticket.AssignedAt        = DateTime.UtcNow;
        ticket.Status            = "assigned";

        // Notify the assigned agent
        db.Notifications.Add(new Notification {
            UserId  = pick.Id,
            Type    = "support",
            Priority = "urgent",
            Title   = $"🎫 New support ticket assigned to you",
            Message = $"Ticket #{ticketId.ToString()[..8].ToUpper()} — {ticket.Subject}. User type: {ticket.UserType ?? "unknown"}.",
            ActionUrl = "/agent",
        });

        await db.SaveChangesAsync();
    }

    // ── Notify admins ────────────────────────────────
    // ── Helper: notify admins ────────────────────────────────
    private async Task NotifyAdmins(Guid ticketId, string subject, string contact, string firstMsg)
    {
        var admins = await db.Users.Where(u => u.Role == "admin").ToListAsync();
        foreach (var admin in admins)
            db.Notifications.Add(new Notification {
                UserId = admin.Id, Type = "support", Priority = "high",
                Title  = $"🎫 New support ticket — {subject}",
                Message = $"From: {contact}. {(firstMsg.Length > 80 ? firstMsg[..80] + "..." : firstMsg)}",
                ActionUrl = "/admin/support",
            });
        if (admins.Any())
            await email.SendAsync("help@worksupport360.com", "Admin",
                $"[WS360] New Support Ticket: {subject}",
                $"<p><b>New ticket from:</b> {contact}</p><p><b>Message:</b> {firstMsg}</p><p>Ticket ID: {ticketId}</p>");
        await db.SaveChangesAsync();
    }

    private static string SupportReplyEmailBody(string subject, string reply, string agentName, bool resolved) => $"""
        <!DOCTYPE html><html><body style="font-family:Arial,sans-serif;background:#f8f9ff;padding:24px">
        <div style="background:#fff;border-radius:16px;padding:32px;max-width:560px;margin:0 auto;border:1px solid #e5e7eb">
          <div style="font-weight:900;font-size:20px;color:#1a1a2e;margin-bottom:24px">Work<span style="color:#f97316">Support</span>360</div>
          <h2 style="color:#1a1a2e;font-size:18px">{(resolved ? "Your ticket has been resolved ✅" : "Reply to your support ticket 💬")}</h2>
          <p style="color:#374151"><b>Regarding:</b> {subject}</p>
          <div style="background:#f8faff;border-left:4px solid #1a1a2e;padding:16px;border-radius:8px;margin:16px 0;color:#374151">{reply}</div>
          <p style="color:#6b7280;font-size:13px">— {agentName}, WorkSupport360 Support Team</p>
          <p style="color:#9ca3af;font-size:12px">Reply to this email or WhatsApp +91-9441363687</p>
        </div></body></html>
        """;
}

public record CreateSupportTicketDto(
    string? Subject = "Support Request",
    string? Category = "general",
    string? Priority = "normal",
    string? UserType = "visitor",
    string? BotSummary = null,
    string? ContactEmail = null,
    string? ContactPhone = null,   // always optional, empty string is fine
    string? FirstMessage = null
);
public record AdminReplyDto(string Message, bool Resolve = false);


//[ApiController, Route("api/admin/revenue"), Authorize(Roles = "admin")]
//public class RevenueController(AppDbContext db) : ControllerBase
//{
//    [HttpGet("breakdown")]
//    public async Task<IActionResult> Breakdown()
//    {
//        var earnings = await db.PlatformEarnings.GroupBy(e => e.Source)
//            .Select(g => new { source = g.Key, total = g.Sum(e => e.Amount), count = g.Count() }).ToListAsync();
//        return Ok(new {
//            breakdown = earnings,
//            commissionRevenue   = await db.Payments.Where(p => p.Status == "paid").SumAsync(p => (decimal?)p.Commission) ?? 0,
//            subscriptionRevenue = await db.ClientSubscriptions.Where(s => s.Status == "active").SumAsync(s => (decimal?)s.AmountPaid) ?? 0,
//            quickSupportRevenue = await db.QuickSupportSessions.Where(q => q.Status == "completed").SumAsync(q => (decimal?)q.PlatformFee) ?? 0,
//            pendingPayouts      = await db.Payments.Where(p => p.PayoutStatus == "pending" && p.Status == "paid").SumAsync(p => (decimal?)p.FreelancerAmount) ?? 0,
//        });
//    }
//}

public static class StringExtensions
{
    public static string Truncate(this string s, int max) =>
        s == null ? "" : s.Length <= max ? s : s[..max] + "…";
}
