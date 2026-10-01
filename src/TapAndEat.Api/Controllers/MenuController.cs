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
    /// Task 3.4 — manual stock correction. Sprint 1 left this open to any
    /// signed-in user as a demo hook; now that orders reserve stock
    /// automatically (Sprint 2) it is restricted to kitchen staff and admins.
    /// </summary>
    [HttpPut("{id:guid}/stock")]
    [Authorize(Roles = "KitchenStaff,Admin")]
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
