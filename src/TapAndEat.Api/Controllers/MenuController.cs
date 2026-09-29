using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Services;

namespace TapAndEat.Api.Controllers;

[ApiController]
[Route("api/menu")]
public class MenuController : ControllerBase
{
    private readonly IMenuService _menuService;

    public MenuController(IMenuService menuService)
    {
        _menuService = menuService;
    }

    /// <summary>Task 3.2 — published menu with dietary tags & current stock. Optional ?tag= filter.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<MenuItemDto>>> GetMenu([FromQuery] string? tag)
    {
        return Ok(await _menuService.GetPublishedMenuAsync(tag));
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<MenuItemDto>> GetById(Guid id)
    {
        var item = await _menuService.GetByIdAsync(id);
        return item is null ? NotFound() : Ok(item);
    }

    /// <summary>
    /// Task 3.4 demo/test hook — any authenticated user can adjust stock so the
    /// real-time SignalR broadcast can be exercised without the ordering
    /// feature (which lands in a later sprint). Restrict this to Admin/
    /// KitchenStaff once ordering is implemented and stock is decremented
    /// automatically by a paid order instead.
    /// </summary>
    [HttpPut("{id:guid}/stock")]
    [Authorize]
    public async Task<ActionResult<MenuItemDto>> SetStock(Guid id, [FromBody] int stockCount)
    {
        try
        {
            return Ok(await _menuService.SetStockAsync(id, stockCount));
        }
        catch (MenuException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
