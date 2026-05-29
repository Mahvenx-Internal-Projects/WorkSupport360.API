using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WorkSupport360.API.Data;
using WorkSupport360.API.DTOs;

namespace WorkSupport360.API.Controllers;

[ApiController, Route("api/notifications"), Authorize]
public class NotificationsController(AppDbContext db) : ControllerBase
{
    private Guid Me => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool? unreadOnly = null, [FromQuery] string? priority = null)
    {
        var query = db.Notifications.Where(n => n.UserId == Me);
        if (unreadOnly == true) query = query.Where(n => !n.IsRead);
        if (!string.IsNullOrWhiteSpace(priority)) query = query.Where(n => n.Priority == priority);
        var items = await query.OrderByDescending(n => n.CreatedAt).Take(100)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Message, n.IsRead, n.Priority, n.ActionUrl, n.CreatedAt))
            .ToListAsync();
        return Ok(items);
    }

    [HttpGet("count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        var count = await db.Notifications.CountAsync(n => n.UserId == Me && !n.IsRead);
        return Ok(new { count });
    }

    [HttpPatch("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == Me);
        if (n is null) return NotFound();
        n.IsRead = true;
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("mark-all-read")]
    public async Task<IActionResult> MarkAllRead()
    {
        await db.Notifications.Where(n => n.UserId == Me && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        return Ok(new { message = "All marked as read" });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == Me);
        if (n is null) return NotFound();
        db.Notifications.Remove(n);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
