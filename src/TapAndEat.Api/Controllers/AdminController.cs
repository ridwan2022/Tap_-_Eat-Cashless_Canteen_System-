using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Models;
using TapAndEat.Api.Services;

namespace TapAndEat.Api.Controllers;

/// <summary>
/// Every endpoint here requires the Admin role (Task 2.2 acceptance
/// criteria: "only admin-role tokens can call these endpoints").
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;

    public AdminController(IAdminService adminService)
    {
        _adminService = adminService;
    }

    // ---- Task 2.2 — user & role management ----

    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyList<UserSummary>>> GetUsers() =>
        Ok(await _adminService.GetUsersAsync());

    [HttpPost("users")]
    public async Task<ActionResult<UserSummary>> CreateUser(CreateUserByAdminRequest request)
    {
        try
        {
            return Ok(await _adminService.CreateUserAsync(request));
        }
        catch (AdminException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPut("users/{id:guid}/role")]
    public async Task<ActionResult<UserSummary>> AssignRole(Guid id, AssignRoleRequest request)
    {
        try
        {
            return Ok(await _adminService.AssignRoleAsync(id, request.Role));
        }
        catch (AdminException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("users/{id:guid}/active")]
    public async Task<ActionResult<UserSummary>> SetActive(Guid id, SetActiveRequest request)
    {
        try
        {
            return Ok(await _adminService.SetActiveAsync(id, request.IsActive));
        }
        catch (AdminException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ---- Task 2.3/2.4 — menu publishing & override CRUD ----

    [HttpGet("menu")]
    public async Task<ActionResult<IReadOnlyList<MenuItemDto>>> GetAllMenuItems() =>
        Ok(await _adminService.GetAllMenuItemsAsync());

    [HttpPost("menu")]
    public async Task<ActionResult<MenuItemDto>> CreateMenuItem(CreateMenuItemRequest request) =>
        Ok(await _adminService.CreateMenuItemAsync(request));

    [HttpPut("menu/{id:guid}")]
    public async Task<ActionResult<MenuItemDto>> UpdateMenuItem(Guid id, UpdateMenuItemRequest request)
    {
        try
        {
            return Ok(await _adminService.UpdateMenuItemAsync(id, request));
        }
        catch (AdminException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("menu/{id:guid}/publish")]
    public async Task<ActionResult<MenuItemDto>> Publish(Guid id)
    {
        try
        {
            return Ok(await _adminService.SetPublishedAsync(id, true));
        }
        catch (AdminException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("menu/{id:guid}/unpublish")]
    public async Task<ActionResult<MenuItemDto>> Unpublish(Guid id)
    {
        try
        {
            return Ok(await _adminService.SetPublishedAsync(id, false));
        }
        catch (AdminException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>Task 2.4 — apply a same-day override, replacing today's standard item.</summary>
    [HttpPost("menu/{id:guid}/override")]
    public async Task<ActionResult<MenuItemDto>> Override(Guid id, OverrideMenuItemRequest request)
    {
        try
        {
            return Ok(await _adminService.ApplyOverrideAsync(id, request));
        }
        catch (AdminException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpDelete("menu/{id:guid}")]
    public async Task<IActionResult> DeleteMenuItem(Guid id)
    {
        try
        {
            await _adminService.DeleteMenuItemAsync(id);
            return NoContent();
        }
        catch (AdminException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
