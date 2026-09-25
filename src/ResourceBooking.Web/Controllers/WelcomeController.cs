using System.Collections;
using System.Drawing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Interfaces;
using static System.Net.Mime.MediaTypeNames;

namespace ResourceBooking.Web.Controllers;

[ApiController]
[Route("/Documentation")]
[Produces("application/json")]
[AllowAnonymous]
public class WelcomeController : ControllerBase
{
    public WelcomeController() { }

    [HttpGet]
    // Changed Content-Type to text/html so the browser interprets it as a webpage
    [Produces("text/html")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ContentResult> WelcomeScreen()
    {
        // 1. Define the URLs for your links
        string scalarUrl = "https://localhost:7030/scalar/v1";
        string jsonUrl = "https://localhost:7030/openapi/v1.json";

        // 2. Create the HTML string structure
        string htmlContent = $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>API Documentation Navigation</title>
        </head>
        <body>

            <div>
                <h1>Select Documentation Format</h1>
                <div>
                    <a href="{{scalarUrl}}" target="_blank">Scalar</a>
                    <a href="{{jsonUrl}}" target="_blank">JSON</a>
                </div>
            </div>

        </body>
        </html>
        """;

        // 3. Return Content with the explicit HTML content type
        return Content(htmlContent, "text/html");
    }
}
