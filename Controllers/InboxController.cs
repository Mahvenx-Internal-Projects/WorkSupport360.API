using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WorkSupport360.API.Data;
using WorkSupport360.API.Models;

namespace WorkSupport360.API.Controllers;

[ApiController, Route("api/inbox"), Authorize]
public class InboxController(AppDbContext db) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Role  => User.FindFirstValue(ClaimTypes.Role)!;

    // Get all threads for current user
    [HttpGet]
    public async Task<IActionResult> GetThreads()
    {
        var threads = await db.MessageThreads
            .Include(t => t.Messages.OrderByDescending(m => m.SentAt).Take(1))
            .Include(t => t.Participants).ThenInclude(p => p.User)
            .Where(t => t.Participants.Any(p => p.UserId == UserId))
            .OrderByDescending(t => t.LastMessageAt)
            .ToListAsync();

        return Ok(threads.Select(t => new {
            t.Id, t.Subject, t.LastMessageAt, t.IsAdminThread,
            lastMessage   = t.Messages.FirstOrDefault()?.Body?.Length > 80 ? t.Messages.FirstOrDefault()!.Body[..80] + "…" : t.Messages.FirstOrDefault()?.Body,
            unreadCount   = t.Messages.Count(m => !m.IsRead && m.RecipientId == UserId),
            participants  = t.Participants.Select(p => new { p.UserId, p.User?.Name, p.User?.Role }),
        }));
    }

    // Get thread messages
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetThread(Guid id)
    {
        var thread = await db.MessageThreads
            .Include(t => t.Messages.OrderBy(m => m.SentAt))
            .ThenInclude(m => m.Sender)
            .Include(t => t.Participants).ThenInclude(p => p.User)
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync(t => t.Id == id && t.Participants.Any(p => p.UserId == UserId));
        if (thread is null) return NotFound();

        // Mark messages as read
        var unread = thread.Messages.Where(m => !m.IsRead && m.RecipientId == UserId).ToList();
        foreach (var m in unread) m.IsRead = true;
        await db.SaveChangesAsync();

        return Ok(new {
            thread.Id, thread.Subject, thread.LastMessageAt, thread.IsAdminThread,
            messages = thread.Messages.Select(m => new {
                m.Id, m.Body, m.SentAt, m.IsRead,
                senderName = m.Sender?.Name, m.SenderId,
                isOwnMessage = m.SenderId == UserId,
            }),
            attachments = thread.Attachments.Select(a => new { a.Id, a.FileName, a.FileUrl, a.UploadedAt }),
            participants = thread.Participants.Select(p => new { p.UserId, p.User?.Name, p.User?.Role }),
        });
    }

    // Send message to admin (or start thread)
    [HttpPost]
    public async Task<IActionResult> Send([FromBody] SendMessageDto dto)
    {
        // Find admin to send to
        var admins = await db.Users.Where(u => u.Role == "admin").ToListAsync();
        if (!admins.Any()) return BadRequest("No admin found");
        var admin = admins.First();

        // Find or create thread
        MessageThread? thread = null;
        if (dto.ThreadId.HasValue)
        {
            thread = await db.MessageThreads
                .Include(t => t.Participants)
                .FirstOrDefaultAsync(t => t.Id == dto.ThreadId && t.Participants.Any(p => p.UserId == UserId));
        }

        if (thread is null)
        {
            thread = new MessageThread
            {
                Subject       = dto.Subject ?? "Message to Admin",
                IsAdminThread = true,
                LastMessageAt = DateTime.UtcNow,
            };
            db.MessageThreads.Add(thread);
            await db.SaveChangesAsync();

            // Add participants
            db.ThreadParticipants.AddRange([
                new ThreadParticipant { ThreadId = thread.Id, UserId = UserId },
                new ThreadParticipant { ThreadId = thread.Id, UserId = admin.Id },
            ]);
        }

        var msg = new InboxMessage
        {
            ThreadId    = thread.Id,
            SenderId    = UserId,
            RecipientId = admin.Id,
            Body        = dto.Body,
            IsRead      = false,
        };
        db.InboxMessages.Add(msg);
        thread.LastMessageAt = DateTime.UtcNow;

        // In-app notification to admin
        db.Notifications.Add(new Notification {
            UserId    = admin.Id,
            Type      = "message",
            Priority  = "normal",
            Title     = $"📬 New message: {thread.Subject}",
            Message   = dto.Body.Length > 80 ? dto.Body[..80] + "…" : dto.Body,
            ActionUrl = "/admin/inbox",
        });

        await db.SaveChangesAsync();
        return Ok(new { threadId = thread.Id, messageId = msg.Id });
    }

    // Reply in thread
    [HttpPost("{id:guid}/reply")]
    public async Task<IActionResult> Reply(Guid id, [FromBody] ReplyDto dto)
    {
        var thread = await db.MessageThreads
            .Include(t => t.Participants)
            .FirstOrDefaultAsync(t => t.Id == id && t.Participants.Any(p => p.UserId == UserId));
        if (thread is null) return NotFound();

        // Send to all other participants
        var others = thread.Participants.Where(p => p.UserId != UserId).ToList();
        foreach (var p in others)
        {
            db.InboxMessages.Add(new InboxMessage {
                ThreadId    = thread.Id,
                SenderId    = UserId,
                RecipientId = p.UserId,
                Body        = dto.Body,
            });
            db.Notifications.Add(new Notification {
                UserId    = p.UserId,
                Type      = "message",
                Priority  = "normal",
                Title     = $"📬 Reply in: {thread.Subject}",
                Message   = dto.Body.Length > 80 ? dto.Body[..80] + "…" : dto.Body,
                ActionUrl = $"/inbox/{thread.Id}",
            });
        }
        thread.LastMessageAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // Unread count
    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount() =>
        Ok(new { count = await db.InboxMessages.CountAsync(m => m.RecipientId == UserId && !m.IsRead) });
}

public record SendMessageDto(string Body, string? Subject = null, Guid? ThreadId = null);
public record ReplyDto(string Body);
