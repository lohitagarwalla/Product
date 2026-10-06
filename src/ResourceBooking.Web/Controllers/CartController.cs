using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Interfaces;

namespace ResourceBooking.Web.Controllers;

[ApiController]
[Route("api/cart")]
[Authorize]
[OutputCache(NoStore = true)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CartController(ICartService carts) : ControllerBase
{
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
    public async Task<ActionResult<CartResponseDto>> Get(CancellationToken ct) =>
        string.IsNullOrWhiteSpace(UserId) ? Unauthorized() : Ok(await carts.GetAsync(UserId, ct));

    [HttpPut("items/{productId:int:min(1)}")]
    public async Task<ActionResult<CartResponseDto>> SetItem(int productId, CartItemWriteDto dto, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(UserId) ? Unauthorized() : Ok(await carts.SetItemAsync(productId, dto, UserId, ct));

    [HttpDelete("items/{productId:int:min(1)}")]
    public async Task<ActionResult<CartResponseDto>> RemoveItem(int productId, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(UserId) ? Unauthorized() : Ok(await carts.RemoveItemAsync(productId, UserId, ct));

    [HttpDelete("items")]
    public async Task<ActionResult<CartResponseDto>> ClearItems(CancellationToken ct) =>
        string.IsNullOrWhiteSpace(UserId) ? Unauthorized() : Ok(await carts.ClearItemsAsync(UserId, ct));

    [HttpPut("address")]
    public async Task<ActionResult<CartResponseDto>> SetAddress(CartAddressWriteDto dto, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(UserId) ? Unauthorized() : Ok(await carts.SetAddressAsync(dto, UserId, ct));

}
