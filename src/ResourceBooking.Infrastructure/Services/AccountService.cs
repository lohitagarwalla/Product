using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Interfaces;

namespace ResourceBooking.Infrastructure.Services;

public class AccountService : IAccountService
{
    private readonly UserManager<ApplicationUser> _userManager;

    // SignInManager is used for Identity-authentication-cookies. this is not required here. we will use jwt for authentication here.
    // Cookies are generally well suited to browser-based applications where the browser can automatically manage the authentication cookie. JWT bearer tokens are generally well suited to APIs consumed by SPAs, mobile clients, or other services, especially when token-based authentication across services is useful. Neither is inherently better; the choice depends on the client and architecture. (JWT is mostly used for api's) (and cookies are natural choise for browser-based applications)
    //private readonly SignInManager<ApplicationUser> _signInManager; 
    private readonly IConfiguration _configuration;

    public AccountService(
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration)
    {
        _userManager = userManager;
        _configuration = configuration;
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

        // Assign default 'Employee' role to newly registered users
        await _userManager.AddToRoleAsync(user, "Employee");

        var token = await GenerateJwtTokenAsync(user);
        var roles = await _userManager.GetRolesAsync(user);

        return new AuthResponseDto
        {
            IsSuccess = true,
            Token = token,
            UserId = user.Id,
            Email = user.Email,
            Roles = roles.ToList()
        };
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
        if (!result)
        {
            return new AuthResponseDto { IsSuccess = false, Errors = new[] { "Invalid email or password." } };
        }

        var token = await GenerateJwtTokenAsync(user);
        var roles = await _userManager.GetRolesAsync(user);

        return new AuthResponseDto
        {
            IsSuccess = true,
            Token = token,
            UserId = user.Id,
            Email = user.Email ?? string.Empty,
            Roles = roles.ToList()
        };
    }

    public async Task LogoutAsync()
    {
        // can implement jwt blacklist in future for revocation of jwt when logout is requested
        //await _userManager.s.SignOutAsync();
    }

    public async Task<string> GenerateJwtTokenAsync(ApplicationUser user)
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
            expires: DateTime.UtcNow.AddHours(8),
            claims: authClaims,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
    }
}
