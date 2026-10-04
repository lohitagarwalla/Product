using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Enums;
using ResourceBooking.Core.Interfaces;

namespace ResourceBooking.Web.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
[OutputCache(NoStore = true)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class OrdersController(IOrderService orders) : ControllerBase
{
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpPost]
    public async Task<ActionResult<OrderResponseDto>> Create(OrderCreateDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId)) return Unauthorized();
        var result = await orders.CreateAsync(dto, UserId, ct);
        return result.IsReplay ? Ok(result.Order) : CreatedAtAction(nameof(Get), new { id = result.Order.Id }, result.Order);
    }

    [HttpGet]
    public async Task<ActionResult<OrderPageDto>> List([FromQuery] OrderQueryDto query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId)) return Unauthorized();
        return Ok(await orders.ListAsync(query, UserId, false, ct));
    }

    [HttpGet("manage"), Authorize(Roles = "Admin")]
    public async Task<ActionResult<OrderPageDto>> Manage([FromQuery] OrderQueryDto query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId)) return Unauthorized();
        return Ok(await orders.ListAsync(query, UserId, true, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderResponseDto>> Get(int id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId)) return Unauthorized();
        return Ok(await orders.GetAsync(id, UserId, User.IsInRole("Admin"), ct));
    }

    [HttpPost("{id:int}/cancel")]
    public Task<ActionResult<OrderResponseDto>> Cancel(int id, OrderCancelDto dto, CancellationToken ct) =>
        ChangeStatus(id, OrderStatus.Cancelled, dto, dto.Reason, ct);

    [HttpPost("{id:int}/ship"), Authorize(Roles = "Admin")]
    public Task<ActionResult<OrderResponseDto>> Ship(int id, OrderStatusChangeDto dto, CancellationToken ct) =>
        ChangeStatus(id, OrderStatus.Shipped, dto, null, ct);

    [HttpPost("{id:int}/deliver"), Authorize(Roles = "Admin")]
    public Task<ActionResult<OrderResponseDto>> Deliver(int id, OrderStatusChangeDto dto, CancellationToken ct) =>
        ChangeStatus(id, OrderStatus.Delivered, dto, null, ct);

    private async Task<ActionResult<OrderResponseDto>> ChangeStatus(int id, OrderStatus target,
        OrderStatusChangeDto dto, string? reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId)) return Unauthorized();
        return Ok(await orders.ChangeStatusAsync(id, target, dto.RowVersion, reason, UserId, User.IsInRole("Admin"), ct));
    }
}
