using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ResourceBooking.Web.Filters;

public class LogExecutionTimeFilter : IAsyncActionFilter
{
    private readonly ILogger<LogExecutionTimeFilter> _logger;

    public LogExecutionTimeFilter(ILogger<LogExecutionTimeFilter> logger)
    {
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var timer = Stopwatch.StartNew();
        var actionName = context.ActionDescriptor.DisplayName;

        _logger.LogInformation("Executing action: {ActionName}", actionName);

        // Execute controller action method
        var resultContext = await next();

        timer.Stop();
        var elapsedMilliseconds = timer.ElapsedMilliseconds;

        if (elapsedMilliseconds > 500)
        {
            _logger.LogWarning("PERFORMANCE WARNING: Action {ActionName} took {ElapsedMilliseconds} ms to execute.",
                actionName, elapsedMilliseconds);
        }
        else
        {
            _logger.LogInformation("Action {ActionName} executed in {ElapsedMilliseconds} ms.",
                actionName, elapsedMilliseconds);
        }
    }
}