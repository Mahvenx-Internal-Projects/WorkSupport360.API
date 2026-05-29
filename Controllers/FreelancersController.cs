using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WorkSupport360.API.Data;
using WorkSupport360.API.DTOs;
using WorkSupport360.API.Models;

namespace WorkSupport360.API.Controllers;

[ApiController, Route("api/freelancers")]
[Produces("application/json")]
public class FreelancersController(AppDbContext db) : ControllerBase
{
    private Guid Me => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Search all verified freelancers (public)</summary>
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? keyword = null,
        [FromQuery] string? skill   = null,
        [FromQuery] bool?   isAvailable = null,
        [FromQuery] decimal? minRate = null,
        [FromQuery] decimal? maxRate = null,
        [FromQuery] string? currency = null,
        [FromQuery] string? country  = null,
        [FromQuery] int?   minExp    = null,
        [FromQuery] int?   minTrustScore = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var query = db.Freelancers.Include(f => f.Skills).Include(f => f.Badges)
            .Where(f => f.IsVerified) // only verified show in browse
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(f => f.AliasName.Contains(keyword) || f.CurrentRole.Contains(keyword) || f.Bio.Contains(keyword));
        if (!string.IsNullOrWhiteSpace(skill))
            query = query.Where(f => f.Skills.Any(s => s.Skill.Contains(skill)));
        if (isAvailable.HasValue) query = query.Where(f => f.IsAvailable == isAvailable);
        if (minRate.HasValue)     query = query.Where(f => f.HourlyRate >= minRate);
        if (maxRate.HasValue)     query = query.Where(f => f.HourlyRate <= maxRate);
        if (!string.IsNullOrWhiteSpace(country)) query = query.Where(f => f.Country == country);
        if (minExp.HasValue)       query = query.Where(f => f.TotalExp >= minExp);
        if (minTrustScore.HasValue) query = query.Where(f => f.TrustScore >= minTrustScore);

        var boosted = db.FeaturedBoosts
            .Where(fb => fb.IsActive && fb.StartsAt <= DateTime.UtcNow && fb.EndsAt >= DateTime.UtcNow)
            .Select(fb => fb.FreelancerId);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(f => boosted.Contains(f.Id))
            .ThenByDescending(f => f.TrustScore)
            .ThenByDescending(f => f.Rating)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(f => new FreelancerListDto(
                f.Id, f.AliasName, f.CurrentRole, f.TotalExp, f.FreelanceExp,
                f.Skills.Select(s => s.Skill).ToList(),
                f.HourlyRate, f.Currency, f.Rating, f.ReviewCount,
                f.TrustScore, f.Tier, f.IsAvailable, f.IsVerified,
                f.Country, f.Timezone, f.CompletedProjects, f.ProfileViews,
                f.ResponseTimeMinutes, f.Badges.Select(b => b.Badge).ToList(),
                boosted.Contains(f.Id)))
            .ToListAsync();

        return Ok(new PagedResult<FreelancerListDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var f = await db.Freelancers
            .Include(f => f.Skills).Include(f => f.Badges)
            .Include(f => f.Availability)
            .Include(f => f.ReviewsGiven).ThenInclude(r => r.Client)
            .FirstOrDefaultAsync(f => f.Id == id);
        if (f is null) return NotFound();
        f.ProfileViews++;
        await db.SaveChangesAsync();
        return Ok(new FreelancerDetailDto(
            f.Id, f.AliasName, f.CurrentRole, f.TotalExp, f.FreelanceExp,
            f.Skills.Select(s => s.Skill).ToList(),
            f.Badges.Select(b => b.Badge).ToList(),
            f.HourlyRate, f.Currency, f.Rating, f.ReviewCount,
            f.TrustScore, f.Tier, f.IsAvailable, f.IsVerified,
            f.Country, f.Timezone, f.Bio,
            f.Availability.Select(a => new AvailabilityDto(a.DayOfWeek, a.IsAvailable, a.StartTime, a.EndTime)).ToList(),
            f.CompletedProjects,
            f.ReviewsGiven.Where(r => r.IsPublic).OrderByDescending(r => r.CreatedAt)
                .Select(r => new ReviewDto(r.Id, r.Client.CompanyName, r.Rating, r.Comment, r.CreatedAt)).ToList()));
    }

    [HttpGet("me"), Authorize(Roles = "freelancer")]
    public async Task<IActionResult> GetMe()
    {
        var f = await db.Freelancers.FirstOrDefaultAsync(f => f.UserId == Me);
        if (f is null) return NotFound();
        return Ok(new FreelancerPrivateDto(f.Id, f.AliasName, f.RealName, f.CurrentCompany,
            f.CurrentRole, f.TotalEarned, f.PendingAmount, f.CompletedProjects,
            f.ProfileViews, f.TrustScore,
            f.BankAccountName, f.BankAccountNumber, f.BankIfscCode, f.BankName, f.UpiId));
    }

    [HttpGet("me/stats"), Authorize(Roles = "freelancer")]
    public async Task<IActionResult> GetStats()
    {
        var f = await db.Freelancers.FirstOrDefaultAsync(f => f.UserId == Me);
        if (f is null) return NotFound();
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var monthlyEarnings = await db.Payments
            .Where(p => p.FreelancerId == f.Id && p.Status == "paid" && p.PaidAt >= monthStart)
            .SumAsync(p => (decimal?)p.FreelancerAmount) ?? 0;
        var cleared = await db.Payments
            .Where(p => p.FreelancerId == f.Id && p.Status == "paid")
            .SumAsync(p => (decimal?)p.FreelancerAmount) ?? 0;
        var activeProjects = await db.Projects
            .CountAsync(p => p.FreelancerId == f.Id && p.Status == "active");
        return Ok(new FreelancerStatsDto(monthlyEarnings, cleared, f.PendingAmount,
            f.TotalEarned, f.CompletedProjects, f.TrustScore, f.ProfileViews, activeProjects));
    }

    [HttpPut("me"), Authorize(Roles = "freelancer")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateFreelancerRequest req)
    {
        var f = await db.Freelancers.Include(f => f.Skills).Include(f => f.Availability)
            .FirstOrDefaultAsync(f => f.UserId == Me);
        if (f is null) return NotFound();

        f.AliasName = req.AliasName; f.CurrentRole = req.CurrentRole;
        f.Bio = req.Bio; f.HourlyRate = req.HourlyRate;
        f.Currency = req.Currency; f.Country = req.Country; f.Timezone = req.Timezone;
        f.BankAccountName   = req.BankAccountName;
        f.BankAccountNumber = req.BankAccountNumber;
        f.BankIfscCode      = req.BankIfscCode;
        f.BankName          = req.BankName;
        f.UpiId             = req.UpiId;

        db.FreelancerSkills.RemoveRange(f.Skills);
        f.Skills = req.Skills.Select(s => new FreelancerSkill { FreelancerId = f.Id, Skill = s }).ToList();

        db.WeeklyAvailabilities.RemoveRange(f.Availability);
        f.Availability = req.Availability.Select(a => new WeeklyAvailability
        {
            FreelancerId = f.Id, DayOfWeek = a.DayOfWeek,
            IsAvailable  = a.IsAvailable, StartTime = a.StartTime, EndTime = a.EndTime
        }).ToList();
        f.IsAvailable = req.Availability.Any(a => a.IsAvailable);

        await db.SaveChangesAsync();
        return Ok(new { message = "Profile updated" });
    }

    [HttpPatch("me/availability"), Authorize(Roles = "freelancer")]
    public async Task<IActionResult> SetAvailable([FromBody] bool isAvailable)
    {
        var f = await db.Freelancers.FirstOrDefaultAsync(f => f.UserId == Me);
        if (f is null) return NotFound();
        f.IsAvailable = isAvailable;
        await db.SaveChangesAsync();
        return Ok(new { isAvailable });
    }
}
