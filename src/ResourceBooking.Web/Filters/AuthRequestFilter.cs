using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using ResourceBooking.Core.DTOs;

namespace ResourceBooking.Web.Filters;

// CORS alone does not stop a browser from sending a cookie-bearing request.
public class AuthRequestFilter(IOptions<AuthSessionOptions> options) : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var request = context.HttpContext.Request;
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.HttpContext.Response.Headers.Pragma = "no-cache";
        if (!request.Headers.TryGetValue("Origin", out var origin)) return;

        var sameOrigin = $"{request.Scheme}://{request.Host}";
        if (origin.Count != 1 || (origin[0] != sameOrigin &&
            !options.Value.AllowedOrigins.Contains(origin[0], StringComparer.Ordinal)))
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}

// This non-simple header forces cross-origin browsers to pass a CORS preflight.
//[AttributeUsage(AttributeTargets.Method)]
public sealed class RefreshCookieRequestAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.HttpContext.Request.Headers["X-Refresh-Token"] != "1")
            context.Result = new BadRequestObjectResult(new { error = "The X-Refresh-Token: 1 header is required." });
    }
}
