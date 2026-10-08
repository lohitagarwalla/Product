using ResourceBooking.Core.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using ResourceBooking.Core.Interfaces;

namespace ResourceBooking.Web.Controllers;

[ApiController]
[Route("api/images")]
[AllowAnonymous]
[OutputCache(NoStore = true)]
public class ImagesController(IImageService images) : ControllerBase
{
    [HttpGet("{id:int}/content")]
    public Task<IActionResult> Content(int id, CancellationToken ct) => Read(id, false, ct);

    [HttpGet("{id:int}/download")]
    public Task<IActionResult> Download(int id, CancellationToken ct) => Read(id, true, ct);

    private async Task<IActionResult> Read(int id, bool download, CancellationToken ct)
    {
        // A draft can only be viewed by an authenticated administrator.
        var image = await images.OpenAsync(id, User.IsInRole(Roles.Admin), ct);
        if (image is null) return NotFound();
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return download ? File(image.Stream, image.ContentType, image.FileName)
            : File(image.Stream, image.ContentType);
    }
}
