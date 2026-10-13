using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Services;

namespace TapAndEat.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orders;

    public OrdersController(IOrderService orders)
    {
        _orders = orders;
    }

    /// <summary>Places an order (reserves stock, awaits payment).</summary>
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Customer))]
    public async Task<ActionResult<OrderDto>> Create(CreateOrderRequest request)
    {
        try
        {
            var order = await _orders.CreateOrderAsync(this.CurrentUserId(), request);
            return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
        }
        catch (AppException ex) { return ex.ToActionResult(); }
    }

    /// <summary>The caller's orders, newest first, with their queue token and live prep-time estimate.</summary>
    [HttpGet("mine")]
    [Authorize(Roles = nameof(UserRole.Customer))]
    public async Task<ActionResult<IReadOnlyList<OrderDto>>> Mine() =>
        Ok(await _orders.GetMyOrdersAsync(this.CurrentUserId()));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> GetById(Guid id)
    {
        try { return Ok(await _orders.GetOrderAsync(id, this.CurrentUserId(), this.IsStaff())); }
        catch (AppException ex) { return ex.ToActionResult(); }
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = nameof(UserRole.Customer))]
    public async Task<ActionResult<OrderDto>> Cancel(Guid id)
    {
        try { return Ok(await _orders.CancelOrderAsync(id, this.CurrentUserId())); }
        catch (AppException ex) { return ex.ToActionResult(); }
    }

    /// <summary>Task 12.2 — Filtered, paginated order history for the current user.</summary>
    [HttpGet("history")]
    [Authorize(Roles = nameof(UserRole.Customer))]
    public async Task<ActionResult<OrderHistoryPageDto>> History(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        OrderStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<OrderStatus>(status, true, out var s))
        {
            parsedStatus = s;
        }

        try
        {
            var result = await _orders.GetOrderHistoryAsync(this.CurrentUserId(), from, to, parsedStatus, page, pageSize);
            return Ok(result);
        }
        catch (AppException ex) { return ex.ToActionResult(); }
    }
}
