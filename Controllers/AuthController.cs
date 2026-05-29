using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WorkSupport360.API.DTOs;
using WorkSupport360.API.Services;

namespace WorkSupport360.API.Controllers;

[ApiController, Route("api/auth")]
[Produces("application/json")]
public class AuthController(IAuthService auth, IConfiguration cfg) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var result = await auth.LoginAsync(req);
        return result is null ? Unauthorized(new { message = "Invalid email or password" }) : Ok(result);
    }

    [HttpPost("google")]
    public async Task<IActionResult> GoogleSignIn([FromBody] GoogleSignInRequest req)
    {
        try { return Ok(await auth.GoogleSignInAsync(req)); }
        catch (UnauthorizedAccessException) { return Unauthorized(new { message = "Invalid Google token" }); }
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        if (req.Role is not ("admin" or "freelancer" or "client"))
            return BadRequest(new { message = "Role must be admin, freelancer, or client" });
        var result = await auth.RegisterAsync(req);
        return result is null
            ? Conflict(new { message = "An account with this email already exists" })
            : CreatedAtAction(nameof(Login), result);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest req)
    {
        var result = await auth.RefreshAsync(req.RefreshToken);
        return result is null ? Unauthorized(new { message = "Invalid or expired refresh token" }) : Ok(result);
    }

    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest req)
    {
        await auth.RevokeTokenAsync(req.RefreshToken);
        return Ok(new { message = "Logged out" });
    }

    [HttpPost("logout-all"), Authorize]
    public async Task<IActionResult> LogoutAll()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await auth.LogoutAllDevicesAsync(userId);
        return Ok(new { message = "Logged out from all devices" });
    }

    [HttpGet("me"), Authorize]
    public IActionResult Me() => Ok(new
    {
        UserId  = User.FindFirstValue(ClaimTypes.NameIdentifier),
        Email   = User.FindFirstValue(ClaimTypes.Email),
        Role    = User.FindFirstValue(ClaimTypes.Role),
        Name    = User.FindFirstValue(ClaimTypes.Name),
        Picture = User.FindFirstValue("picture"),
    });

    [HttpGet("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromQuery] string token)
    {
        var user = await auth.VerifyEmailAsync(token);
        if (user is null) return BadRequest(new { message = "Invalid or expired verification link" });
        var frontendUrl = cfg["App:FrontendUrl"] ?? "http://localhost:3000";
        return Redirect($"{frontendUrl}/login?verified=true");
    }

    [HttpPost("complete-profile"), Authorize(Roles = "freelancer")]
    public async Task<IActionResult> CompleteProfile([FromBody] CompleteProfileRequest req)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await auth.CompleteFreelancerProfileAsync(userId, req);
        return Ok(new { message = "Profile saved! Admin will verify your identity shortly." });
    }
}
