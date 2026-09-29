using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
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
    public async Task<ActionResult<IReadOnlyList<OrderDto>>> Mine() =>
        Ok(await _orders.GetMyOrdersAsync(this.CurrentUserId()));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> GetById(Guid id)
    {
        try { return Ok(await _orders.GetOrderAsync(id, this.CurrentUserId(), this.IsStaff())); }
        catch (AppException ex) { return ex.ToActionResult(); }
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<OrderDto>> Cancel(Guid id)
    {
        try { return Ok(await _orders.CancelOrderAsync(id, this.CurrentUserId())); }
        catch (AppException ex) { return ex.ToActionResult(); }
    }
}
