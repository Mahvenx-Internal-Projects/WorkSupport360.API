using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Google.Apis.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using WorkSupport360.API.Data;
using WorkSupport360.API.DTOs;
using WorkSupport360.API.Models;

namespace WorkSupport360.API.Services;

public interface IAuthService
{
    Task<AuthResponse?> LoginAsync(LoginRequest req);
    Task<AuthResponse>  GoogleSignInAsync(GoogleSignInRequest req);
    Task<AuthResponse?> RegisterAsync(RegisterRequest req);
    Task<AuthResponse?> RefreshAsync(string refreshToken);
    Task               RevokeTokenAsync(string refreshToken);
    Task               LogoutAllDevicesAsync(Guid userId);
    Task<AuthResponse?> ForceLoginAsync(LoginRequest req);
    Task<User?>        VerifyEmailAsync(string token);
    Task               CompleteFreelancerProfileAsync(Guid userId, CompleteProfileRequest req);
}

public class AuthService(AppDbContext db, IConfiguration cfg, IEmailService email, ILogger<AuthService> log) : IAuthService
{
    private readonly string _secret   = cfg["Jwt:Secret"]   ?? throw new Exception("Jwt:Secret missing");
    private readonly string _issuer   = cfg["Jwt:Issuer"]   ?? "WorkSupport360";
    private readonly string _audience = cfg["Jwt:Audience"] ?? "WorkSupport360";
    private readonly int _accessMins  = int.Parse(cfg["Jwt:AccessTokenExpireMinutes"]  ?? "60");
    private readonly int _refreshDays = int.Parse(cfg["Jwt:RefreshTokenExpireDays"]    ?? "30");
    private readonly string _googleClientId = cfg["Google:ClientId"] ?? "";

    public async Task<AuthResponse?> LoginAsync(LoginRequest req)
    {
        var user = await db.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Email == req.Email && u.IsActive);

        if (user is null || string.IsNullOrEmpty(user.PasswordHash)) return null;
        if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash)) return null;

        // Block unverified
        if (!user.EmailVerified)
            throw new InvalidOperationException("EMAIL_NOT_VERIFIED");

        // Check for existing active sessions
        var activeSessions = user.RefreshTokens
            .Where(r => !r.IsRevoked && r.ExpiresAt > DateTime.UtcNow)
            .ToList();

        if (activeSessions.Any())
        {
            // Return special response indicating active session exists
            throw new InvalidOperationException($"ACTIVE_SESSION|{user.LastLoginAt:yyyy-MM-ddTHH:mm:ss}|{activeSessions.Count}");
        }

        // Update tracking
        user.LastLoginAt    = DateTime.UtcNow;
        user.LoginCount    += 1;
        user.ActiveSessions = 1;

        db.AttendanceLogs.Add(new AttendanceLog {
            UserId = user.Id, Action = "login",
            Note = $"Login #{user.LoginCount} from {req.DeviceInfo ?? "unknown device"}"
        });
        await db.SaveChangesAsync();

        return await BuildTokensAsync(user, req.DeviceInfo);
    }

    // Force login — kills existing sessions then logs in
    public async Task<AuthResponse?> ForceLoginAsync(LoginRequest req)
    {
        var user = await db.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Email == req.Email && u.IsActive);

        if (user is null || string.IsNullOrEmpty(user.PasswordHash)) return null;
        if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash)) return null;
        if (!user.EmailVerified) throw new InvalidOperationException("EMAIL_NOT_VERIFIED");

        // Kill all existing sessions
        var sessions = user.RefreshTokens.Where(r => !r.IsRevoked).ToList();
        foreach (var s in sessions) s.IsRevoked = true;

        db.AttendanceLogs.Add(new AttendanceLog {
            UserId = user.Id, Action = "logout",
            Note = $"Force-logged out by new login. Killed {sessions.Count} session(s)."
        });

        // Now login
        user.LastLoginAt    = DateTime.UtcNow;
        user.LastLogoutAt   = DateTime.UtcNow; // previous session ended
        user.LoginCount    += 1;
        user.ActiveSessions = 1;

        db.AttendanceLogs.Add(new AttendanceLog {
            UserId = user.Id, Action = "login",
            Note = $"Force login #{user.LoginCount} — previous session killed"
        });
        await db.SaveChangesAsync();

        return await BuildTokensAsync(user, req.DeviceInfo);
    }

    public async Task<AuthResponse> GoogleSignInAsync(GoogleSignInRequest req)
    {
        GoogleJsonWebSignature.Payload payload;
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings { Audience = [_googleClientId] };
            payload = await GoogleJsonWebSignature.ValidateAsync(req.IdToken, settings);
        }
        catch (Exception ex)
        {
            log.LogWarning("Google token validation failed: {Msg}", ex.Message);
            throw new UnauthorizedAccessException("Invalid Google token");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.GoogleId == payload.Subject || u.Email == payload.Email);
        bool isNew = false;

        if (user is null)
        {
            isNew = true;
            user = new User
            {
                Email         = payload.Email,
                Name          = payload.Name,
                GoogleId      = payload.Subject,
                GoogleEmail   = payload.Email,
                Picture       = payload.Picture,
                Role          = "client",
                EmailVerified = payload.EmailVerified,
            };
            db.Users.Add(user);

            db.Clients.Add(new Client
            {
                UserId      = user.Id,
                CompanyName = payload.Name,
                ContactName = payload.Name,
                Plan        = "payg",
            });

            await db.SaveChangesAsync();
            await email.SendWelcomeEmailAsync(user.Email, user.Name, "Google");
        }
        else
        {
            if (user.GoogleId is null) user.GoogleId = payload.Subject;
            user.Picture     = payload.Picture;
            user.LastLoginAt = DateTime.UtcNow;
            user.EmailVerified = true;
        }

        db.AttendanceLogs.Add(new AttendanceLog { UserId = user.Id, Action = "login" });
        await db.SaveChangesAsync();

        var tokens = await BuildTokensAsync(user, req.DeviceInfo);
        return tokens with { IsNewUser = isNew };
    }

    public async Task<AuthResponse?> RegisterAsync(RegisterRequest req)
    {
        if (await db.Users.AnyAsync(u => u.Email == req.Email)) return null;

        var verifyToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        var user = new User
        {
            Email            = req.Email,
            Name             = req.Name,
            Role             = req.Role,
            MobileNumber     = req.MobileNumber,
            PasswordHash     = BCrypt.Net.BCrypt.HashPassword(req.Password),
            EmailVerified    = false,
            EmailVerifyToken = verifyToken,
        };
        db.Users.Add(user);

        if (req.Role == "freelancer")
        {
            var parts     = req.Name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var aliasName = req.AliasName ?? (parts.Length > 1 ? $"{parts[0]} {parts[^1][0]}." : parts[0]);

            var fl = new Freelancer
            {
                UserId         = user.Id,
                AliasName      = aliasName,
                RealName       = req.Name,
                CurrentCompany = req.CurrentCompany ?? "",
                CurrentRole    = req.CurrentRole    ?? "Tech Professional",
                TotalExp       = req.TotalExp,
                FreelanceExp   = req.FreelanceExp,
                HourlyRate     = req.HourlyRate > 0 ? req.HourlyRate : 0,
                Currency       = req.Currency       ?? "USD",
                Country        = req.Country        ?? "India",
                Timezone       = req.Timezone        ?? "IST (UTC+5:30)",
                Bio            = req.Bio            ?? "",
                IsAvailable    = req.Availability?.Any(a => a.IsAvailable) ?? false,
                IsVerified     = false,
                TrustScore     = 60,
                Tier           = 1,
                Skills         = req.Skills?.Select(s => new FreelancerSkill { Skill = s }).ToList() ?? [],
                Availability   = req.Availability?.Select(a => new WeeklyAvailability
                {
                    DayOfWeek = a.DayOfWeek, IsAvailable = a.IsAvailable,
                    StartTime = a.StartTime, EndTime = a.EndTime,
                }).ToList() ?? [],
            };
            db.Freelancers.Add(fl);
        }
        else if (req.Role == "client")
        {
            db.Clients.Add(new Client
            {
                UserId      = user.Id,
                CompanyName = req.CompanyName ?? req.Name,
                ContactName = req.ContactName ?? req.Name,
                Industry    = req.Industry    ?? "",
                Country     = req.Country     ?? "",
                Plan        = "payg",
            });
        }

        db.AttendanceLogs.Add(new AttendanceLog { UserId = user.Id, Action = "login" });
        await db.SaveChangesAsync();
        await email.SendVerificationEmailAsync(user.Email, user.Name, verifyToken);

        return await BuildTokensAsync(user, null);
    }

    public async Task<AuthResponse?> RefreshAsync(string refreshToken)
    {
        var token = await db.RefreshTokens.Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == refreshToken && !r.IsRevoked && r.ExpiresAt > DateTime.UtcNow);
        if (token is null) return null;
        token.IsRevoked = true;
        await db.SaveChangesAsync();
        return await BuildTokensAsync(token.User, token.DeviceInfo);
    }

    public async Task RevokeTokenAsync(string refreshToken)
    {
        var token = await db.RefreshTokens.FirstOrDefaultAsync(r => r.Token == refreshToken);
        if (token is null) return;

        // Log logout
        db.AttendanceLogs.Add(new AttendanceLog { UserId = token.UserId, Action = "logout" });
        token.IsRevoked = true;

        // Track last logout
        var user = await db.Users.FindAsync(token.UserId);
        if (user is not null) user.LastLogoutAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    public async Task LogoutAllDevicesAsync(Guid userId)
    {
        await db.RefreshTokens.Where(r => r.UserId == userId && !r.IsRevoked)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsRevoked, true));
        db.AttendanceLogs.Add(new AttendanceLog { UserId = userId, Action = "logout" });
        var user = await db.Users.FindAsync(userId);
        if (user is not null) user.LastLogoutAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<User?> VerifyEmailAsync(string token)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.EmailVerifyToken == token);
        if (user is null) return null;
        user.EmailVerified    = true;
        user.EmailVerifyToken = null;
        await db.SaveChangesAsync();
        return user;
    }

    public async Task CompleteFreelancerProfileAsync(Guid userId, CompleteProfileRequest req)
    {
        var fl = await db.Freelancers.Include(f => f.Skills).Include(f => f.Availability)
            .FirstOrDefaultAsync(f => f.UserId == userId);

        if (fl is null)
        {
            var user = await db.Users.FindAsync(userId);
            if (user is null) return;
            user.Role = "freelancer";
            var parts = user.Name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            fl = new Freelancer
            {
                UserId    = userId,
                AliasName = parts.Length > 1 ? $"{parts[0]} {parts[^1][0]}." : parts[0],
                RealName  = user.Name,
            };
            db.Freelancers.Add(fl);
        }

        fl.CurrentRole    = req.CurrentRole;
        fl.CurrentCompany = req.CurrentCompany;
        fl.TotalExp       = req.TotalExp;
        fl.FreelanceExp   = req.FreelanceExp;
        fl.HourlyRate     = req.HourlyRate;
        fl.Currency       = req.Currency;
        fl.Country        = req.Country;
        fl.Timezone       = req.Timezone;
        fl.Bio            = req.Bio;
        fl.IsAvailable    = req.Availability?.Any(a => a.IsAvailable) ?? false;

        db.FreelancerSkills.RemoveRange(fl.Skills);
        fl.Skills = req.Skills?.Select(s => new FreelancerSkill { FreelancerId = fl.Id, Skill = s }).ToList() ?? [];

        db.WeeklyAvailabilities.RemoveRange(fl.Availability);
        fl.Availability = req.Availability?.Select(a => new WeeklyAvailability
        {
            FreelancerId = fl.Id, DayOfWeek = a.DayOfWeek,
            IsAvailable  = a.IsAvailable, StartTime = a.StartTime, EndTime = a.EndTime,
        }).ToList() ?? [];

        await db.SaveChangesAsync();
    }

    private async Task<AuthResponse> BuildTokensAsync(User user, string? deviceInfo)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email,          user.Email),
            new Claim(ClaimTypes.Role,           user.Role),
            new Claim(ClaimTypes.Name,           user.Name),
            new Claim("picture",                 user.Picture ?? ""),
        };
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var jwt   = new JwtSecurityToken(_issuer, _audience, claims,
            expires: DateTime.UtcNow.AddMinutes(_accessMins), signingCredentials: creds);
        var accessToken = new JwtSecurityTokenHandler().WriteToken(jwt);

        var refreshTokenValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId    = user.Id, Token = refreshTokenValue,
            ExpiresAt = DateTime.UtcNow.AddDays(_refreshDays), DeviceInfo = deviceInfo,
        });

        // Clean expired tokens
        var expired = await db.RefreshTokens.Where(r => r.UserId == user.Id && r.ExpiresAt < DateTime.UtcNow).ToListAsync();
        db.RefreshTokens.RemoveRange(expired);

        await db.SaveChangesAsync();
        return new AuthResponse(accessToken, refreshTokenValue, user.Role, user.Name, user.Id.ToString(), user.Picture);
    }
}
