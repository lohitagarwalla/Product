using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace ResourceBooking.Web.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            // Pass request to the next middleware in the pipeline
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred during request processing: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var problemDetails = new ProblemDetails
        {
            Instance = context.Request.Path
        };

        switch (exception)
        {
            case InvalidOperationException invalidOpEx:
                // Domain validation or business rule violation
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                problemDetails.Status = (int)HttpStatusCode.BadRequest;
                problemDetails.Title = "Bad Request";
                problemDetails.Detail = invalidOpEx.Message;
                problemDetails.Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1";
                break;

            case UnauthorizedAccessException unauthEx:
                // Authorization failure
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                problemDetails.Status = (int)HttpStatusCode.Forbidden;
                problemDetails.Title = "Access Denied";
                problemDetails.Detail = unauthEx.Message;
                problemDetails.Type = "https://tools.ietf.org/html/rfc7231#section-6.5.3";
                break;

            case KeyNotFoundException notFoundEx:
                // Missing record
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                problemDetails.Status = (int)HttpStatusCode.NotFound;
                problemDetails.Title = "Resource Not Found";
                problemDetails.Detail = notFoundEx.Message;
                problemDetails.Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4";
                break;

            default:
                // Unhandled server failure
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                problemDetails.Status = (int)HttpStatusCode.InternalServerError;
                problemDetails.Title = "Internal Server Error";
                problemDetails.Detail = _env.IsDevelopment()
                    ? $"{exception.Message} | StackTrace: {exception.StackTrace}"
                    : "An error occurred while processing your request. Please try again later.";
                problemDetails.Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1";
                break;
        }

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(problemDetails, jsonOptions);

        return context.Response.WriteAsync(json);
    }
}