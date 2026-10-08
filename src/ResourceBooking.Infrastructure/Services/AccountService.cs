using ResourceBooking.Core.Constants;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Interfaces;
using ResourceBooking.Infrastructure.Data;

namespace ResourceBooking.Infrastructure.Services;

public class AccountService : IAccountService
{
    private readonly UserManager<ApplicationUser> _userManager;

    // SignInManager is used for Identity-authentication-cookies. this is not required here. we will use jwt for authentication here.
    // Cookies are generally well suited to browser-based applications where the browser can automatically manage the authentication cookie. JWT bearer tokens are generally well suited to APIs consumed by SPAs, mobile clients, or other services, especially when token-based authentication across services is useful. Neither is inherently better; the choice depends on the client and architecture. (JWT is mostly used for api's) (and cookies are natural choise for browser-based applications)
    //private readonly SignInManager<ApplicationUser> _signInManager; 
    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _db;
    private readonly AuthSessionOptions _sessionOptions;
    private readonly TimeProvider _clock;

    public AccountService(
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        ApplicationDbContext db,
        IOptions<AuthSessionOptions> sessionOptions,
        TimeProvider clock)
    {
        _userManager = userManager;
        _configuration = configuration;
        _db = db;
        _sessionOptions = sessionOptions.Value;
        _clock = clock;
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterDto dto)
    {
        var existingUser = await _userManager.FindByEmailAsync(dto.Email);
        if (existingUser != null)
        {
            return new AuthResponseDto
            {
                IsSuccess = false,
                Errors = new[] { "A user with this email address already exists." }
            };
        }

        var user = new ApplicationUser
        {
            UserName = dto.Email,
            Email = dto.Email,
            PhoneNumber = AddressPhoneNumber.Normalize(dto.PhoneNumber),
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Department = dto.Department
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
        {
            return new AuthResponseDto
            {
                IsSuccess = false,
                Errors = result.Errors.Select(e => e.Description).ToArray()
            };
        }

        // Assign the Customer role to newly registered users.
        await _userManager.AddToRoleAsync(user, Roles.Customer);

        return await CreateSessionAsync(user);
    }

    public async Task<AuthResponseDto> LoginAsync(LoginDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user == null)
        {
            return new AuthResponseDto
            {
                IsSuccess = false,
                Errors = new[] { "Invalid email or password." }
            };
        }

        var result = await _userManager.CheckPasswordAsync(user, dto.Password);
        if (!result || await _userManager.IsLockedOutAsync(user))
        {
            return new AuthResponseDto { IsSuccess = false, Errors = new[] { "Invalid email or password." } };
        }

        return await CreateSessionAsync(user);
    }

    public async Task<AuthResponseDto> RefreshAsync(string? refreshToken, CancellationToken ct = default)
    {
        var tokenHash = HashPresentedToken(refreshToken);
        if (tokenHash is null) return InvalidRefresh();
        var sessionId = await FindSessionAsync(tokenHash, ct);
        if (sessionId is null) return InvalidRefresh();

        return await RefreshSessionLock.RunAsync(_db, sessionId.Value, async () =>
        {
            // Read state only after acquiring the session lock.
            var token = await _db.RefreshTokens.Include(x => x.Session)
                .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, ct);
            var now = _clock.GetUtcNow().UtcDateTime;
            if (token is null || token.Session.RevokedAt.HasValue || token.Session.ExpiresAt <= now)
                return InvalidRefresh();

            var session = token.Session;
            if (token.UsedAt.HasValue)
            {
                // Reusing ANY ancestor token revokes the entire login session.
                session.RevokedAt = now;
                await _db.SaveChangesAsync(ct);
                return InvalidRefresh();
            }

            var user = await _db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == session.UserId, ct);
            if (user is null || user.SecurityStamp != session.SecurityStamp ||
                (user.LockoutEnabled && user.LockoutEnd > _clock.GetUtcNow()))
            {
                session.RevokedAt = now;
                await _db.SaveChangesAsync(ct);
                return InvalidRefresh();
            }

            var replacement = CreateRefreshToken(session, now);
            var response = await CreateResponseAsync(user, replacement, session.ExpiresAt);
            token.UsedAt = now;
            await _db.SaveChangesAsync(ct);
            return response;
        }, ct);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct = default)
    {
        var tokenHash = HashPresentedToken(refreshToken);
        if (tokenHash is null) return;
        var sessionId = await FindSessionAsync(tokenHash, ct);
        if (sessionId is null) return;
        await RefreshSessionLock.RunAsync(_db, sessionId.Value, async () =>
        {
            var session = await _db.RefreshSessions.SingleOrDefaultAsync(x => x.Id == sessionId, ct);
            if (session is not null && session.RevokedAt is null)
            {
                session.RevokedAt = _clock.GetUtcNow().UtcDateTime;
                await _db.SaveChangesAsync(ct);
            }
            return true;
        }, ct);
    }

    private async Task<AuthResponseDto> CreateSessionAsync(ApplicationUser user)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var session = new RefreshSession
        {
            UserId = user.Id,
            SecurityStamp = user.SecurityStamp ?? string.Empty,
            CreatedAt = now,
            ExpiresAt = now.AddDays(_sessionOptions.RefreshSessionDays)
        };
        _db.RefreshSessions.Add(session);
        var refreshToken = CreateRefreshToken(session, now);
        var response = await CreateResponseAsync(user, refreshToken, session.ExpiresAt);
        await _db.SaveChangesAsync();
        return response;
    }

    private string CreateRefreshToken(RefreshSession session, DateTime now)
    {
        var rawToken = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
        _db.RefreshTokens.Add(new RefreshToken
        {
            SessionId = session.Id,
            Session = session,
            TokenHash = HashPresentedToken(rawToken)!,
            CreatedAt = now
        });
        return rawToken;
    }

    private async Task<AuthResponseDto> CreateResponseAsync(ApplicationUser user, string refreshToken, DateTime sessionExpiry)
    {
        var expiresAt = _clock.GetUtcNow().UtcDateTime.AddMinutes(_sessionOptions.AccessTokenMinutes);
        var roles = await _userManager.GetRolesAsync(user);
        return new AuthResponseDto
        {
            IsSuccess = true,
            Token = await GenerateJwtTokenAsync(user, expiresAt),
            AccessTokenExpiresAt = expiresAt,
            RefreshToken = refreshToken,
            RefreshTokenExpiresAt = DateTime.SpecifyKind(sessionExpiry, DateTimeKind.Utc),
            UserId = user.Id,
            Email = user.Email ?? string.Empty,
            Roles = roles.ToList(),
            FirstName = user.FirstName
        };
    }

    private Task<Guid?> FindSessionAsync(string hash, CancellationToken ct) =>
        _db.RefreshTokens.AsNoTracking().Where(x => x.TokenHash == hash)
            .Select(x => (Guid?)x.SessionId).SingleOrDefaultAsync(ct);

    private static string? HashPresentedToken(string? token)
    {
        // 64 random bytes encode to 86 unpadded base64url characters.
        if (token is null || token.Length != 86 ||
            token.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')) return null;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    private static AuthResponseDto InvalidRefresh() => new()
    {
        IsSuccess = false,
        Errors = ["The refresh session is invalid or expired. Please log in again."]
    };

    public Task<string> GenerateJwtTokenAsync(ApplicationUser user) =>
        GenerateJwtTokenAsync(user, _clock.GetUtcNow().UtcDateTime.AddMinutes(_sessionOptions.AccessTokenMinutes));

    private async Task<string> GenerateJwtTokenAsync(ApplicationUser user, DateTime expiresAt)
    {
        var userRoles = await _userManager.GetRolesAsync(user);

        // Claims represent statements about the user (Subject, Email, Roles, Custom Claims)
        var authClaims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new("Department", user.Department),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        foreach (var role in userRoles)
        {
            authClaims.Add(new Claim(ClaimTypes.Role, role));
        }

        var jwtSecret = _configuration["JwtSettings:Secret"]
            ?? "SuperSecretSecurityKeyThatIsAtLeast32BytesLongForHS256!";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

        var tokenDescriptor = new JwtSecurityToken(
            issuer: _configuration["JwtSettings:Issuer"] ?? "ResourceBookingApi",
            audience: _configuration["JwtSettings:Audience"] ?? "ResourceBookingClients",
            expires: expiresAt,
            claims: authClaims,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
    }
}
