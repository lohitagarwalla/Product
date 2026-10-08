using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Interfaces;
using ResourceBooking.Web.Filters;

namespace ResourceBooking.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[ServiceFilter(typeof(AuthRequestFilter))]
public class AuthController : ControllerBase
{
    public const string RefreshCookieName = "__Secure-ResourceBooking.Refresh";
    private readonly IAccountService _accountService;

    public AuthController(IAccountService accountService)
    {
        _accountService = accountService;
    }

    /// <summary>
    /// Register a new user account.
    /// </summary>
    [HttpPost("register")]
    [Consumes("application/json")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AuthResponseDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        var result = await _accountService.RegisterAsync(dto);
        if (!result.IsSuccess)
        {
            return BadRequest(result);
        }

        SetRefreshCookie(result);
        return Ok(result);
    }

    /// <summary>
    /// Authenticate a user and receive a JWT Bearer token.
    /// </summary>
    [HttpPost("login")]
    [Consumes("application/json")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AuthResponseDto))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var result = await _accountService.LoginAsync(dto);
        if (!result.IsSuccess)
        {
            return Unauthorized(result);
        }

        SetRefreshCookie(result);
        return Ok(result);
    }

    /// <summary>Rotate the refresh cookie and issue a new access token. Requires X-Refresh-Token: 1.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [RefreshCookieRequest]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AuthResponseDto))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var result = await _accountService.RefreshAsync(Request.Cookies[RefreshCookieName], ct);
        if (!result.IsSuccess)
        {
            Response.Cookies.Delete(RefreshCookieName, RefreshCookieOptions());
            return Unauthorized(result);
        }
        SetRefreshCookie(result);
        return Ok(result);
    }

    /// <summary>
    /// Terminate the active session.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [RefreshCookieRequest]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        // The cookie identifies the session, including when its access token has expired.
        await _accountService.LogoutAsync(Request.Cookies[RefreshCookieName], ct);
        Response.Cookies.Delete(RefreshCookieName, RefreshCookieOptions());
        return Ok(new { message = "Logged out successfully." });
    }

    private void SetRefreshCookie(AuthResponseDto result)
    {
        var options = RefreshCookieOptions();
        options.Expires = new DateTimeOffset(result.RefreshTokenExpiresAt);
        Response.Cookies.Append(RefreshCookieName, result.RefreshToken, options);
    }

    private static CookieOptions RefreshCookieOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.None,
        Path = "/api/auth",
        IsEssential = true
    };
}
