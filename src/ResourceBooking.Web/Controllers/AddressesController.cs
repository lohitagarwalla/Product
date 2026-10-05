using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Interfaces;

namespace ResourceBooking.Web.Controllers;

[ApiController]
[Route("api/profile/addresses")]
[Authorize]
[OutputCache(NoStore = true)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AddressesController(IAddressService addresses) : ControllerBase
{
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AddressResponseDto>>> List(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId)) return Unauthorized();
        return Ok(await addresses.ListAsync(UserId, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AddressResponseDto>> Get(int id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId)) return Unauthorized();
        return Ok(await addresses.GetAsync(id, UserId, ct));
    }

    [HttpPost]
    public async Task<ActionResult<AddressResponseDto>> Create(AddressCreateDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId)) return Unauthorized();
        var result = await addresses.CreateAsync(dto, UserId, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AddressResponseDto>> Update(int id, AddressUpdateDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId)) return Unauthorized();
        return Ok(await addresses.UpdateAsync(id, dto, UserId, ct));
    }

    [HttpPut("{id:int}/default")]
    public async Task<ActionResult<AddressResponseDto>> SetDefault(int id, AddressVersionDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId)) return Unauthorized();
        return Ok(await addresses.SetDefaultAsync(id, dto.RowVersion, UserId, ct));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromBody] AddressVersionDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId)) return Unauthorized();
        await addresses.DeleteAsync(id, dto.RowVersion, UserId, ct);
        return NoContent();
    }
}
