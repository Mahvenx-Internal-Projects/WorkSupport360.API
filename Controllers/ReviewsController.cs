using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WorkSupport360.API.Data;
using WorkSupport360.API.DTOs;
using WorkSupport360.API.Models;

namespace WorkSupport360.API.Controllers;

[ApiController, Route("api/reviews"), Authorize]
public class ReviewsController(AppDbContext db) : ControllerBase
{
    private Guid Me => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost, Authorize(Roles = "client")]
    public async Task<IActionResult> Create([FromBody] CreateReviewDto dto)
    {
        if (dto.Rating is < 1 or > 5) return BadRequest(new { message = "Rating 1-5" });
        var client = await db.Clients.FirstOrDefaultAsync(c => c.UserId == Me);
        if (client is null) return Forbid();
        if (await db.Reviews.AnyAsync(r => r.ProjectId == dto.ProjectId && r.ClientId == client.Id))
            return Conflict(new { message = "Review already submitted for this project" });

        var review = new Review { ProjectId = dto.ProjectId, ClientId = client.Id, FreelancerId = dto.FreelancerId, Rating = dto.Rating, Comment = dto.Comment };
        db.Reviews.Add(review);

        var fl = await db.Freelancers.Include(f => f.ReviewsGiven).FirstOrDefaultAsync(f => f.Id == dto.FreelancerId);
        if (fl is not null)
        {
            fl.ReviewCount++;
            var all = fl.ReviewsGiven.Select(r => r.Rating).Append(dto.Rating).ToList();
            fl.Rating = (decimal)all.Average();

            db.Notifications.Add(new Notification {
                UserId = fl.UserId, Type = "review", Priority = "normal",
                Title = $"New {dto.Rating}★ review received!",
                Message = dto.Comment.Length > 80 ? dto.Comment[..80] + "..." : dto.Comment,
            });
        }
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Create), new { id = review.Id }, new { id = review.Id });
    }
}
