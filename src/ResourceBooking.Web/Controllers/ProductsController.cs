using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Interfaces;

namespace ResourceBooking.Web.Controllers;

[ApiController]
[Route("api/products")]
[Authorize(Roles = "Admin")]
[OutputCache(NoStore = true)]
public class ProductsController(IProductService products) : ControllerBase
{
    [HttpGet, AllowAnonymous]
    public async Task<ActionResult<ProductPageDto>> List([FromQuery] ProductQueryDto query, CancellationToken ct) =>
        Ok(await products.ListAsync(query, false, ct));

    [HttpGet("manage")]
    public async Task<ActionResult<ProductPageDto>> Manage([FromQuery] ProductQueryDto query, CancellationToken ct) =>
        Ok(await products.ListAsync(query, true, ct));

    [HttpGet("{id:int}"), AllowAnonymous]
    public async Task<ActionResult<ProductResponseDto>> Get(int id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var product = await products.GetAsync(id, User.IsInRole("Admin"), ct);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpPost]
    public async Task<ActionResult<ProductResponseDto>> Create(ProductWriteDto dto, CancellationToken ct)
    {
        var product = await products.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ProductResponseDto>> Update(int id, ProductWriteDto dto, CancellationToken ct) =>
        Ok(await products.UpdateAsync(id, dto, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await products.DeleteAsync(id, ct);
        return NoContent();
    }

    // One file per request: React can upload each selected image and retry independently.
    [HttpPost("{id:int}/images")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(21 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 21 * 1024 * 1024)]
    public async Task<ActionResult<ProductImageResponseDto>> Upload(int id, [FromForm] ProductImageUploadForm form,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        await using var content = form.File.OpenReadStream();
        var image = await products.AddImageAsync(id, content, form.File.FileName, form.AltText ?? "", userId, ct);
        return Created(image.Url, image);
    }

    [HttpPut("{id:int}/images/order")]
    public async Task<IActionResult> Reorder(int id, ProductImageOrderDto dto, CancellationToken ct)
    {
        await products.ReorderImagesAsync(id, dto.ImageIds, ct);
        return NoContent();
    }

    [HttpPut("{id:int}/images/{imageId:int}")]
    public async Task<IActionResult> UpdateImage(int id, int imageId, ProductImageUpdateDto dto, CancellationToken ct)
    {
        await products.UpdateImageAsync(id, imageId, dto.AltText, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}/images/{imageId:int}")]
    public async Task<IActionResult> DeleteImage(int id, int imageId, CancellationToken ct)
    {
        await products.RemoveImageAsync(id, imageId, ct);
        return NoContent();
    }
}

public class ProductImageUploadForm
{
    [Required] public IFormFile File { get; set; } = null!;
    [StringLength(250)] public string? AltText { get; set; }
}
