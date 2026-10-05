using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Services;

namespace TapAndEat.Api.Controllers;

/// <summary>Tasks 10.1–10.4 — ingredient stock, recipes and low-stock alerts.</summary>
[ApiController]
[Route("api")]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventory;

    public InventoryController(IInventoryService inventory)
    {
        _inventory = inventory;
    }

    /// <summary>Read-only low-stock list for the kitchen display (Task 10.3) — kitchen staff don't need full admin rights to see this.</summary>
    [HttpGet("kitchen/low-stock")]
    [Authorize(Roles = Roles.Staff)]
    public async Task<ActionResult<IReadOnlyList<IngredientDto>>> LowStockForKitchen() => Ok(await _inventory.GetLowStockAsync());

    [HttpGet("admin/ingredients")]
    [Authorize(Roles = nameof(Models.UserRole.Admin))]
    public async Task<ActionResult<IReadOnlyList<IngredientDto>>> List() => Ok(await _inventory.GetIngredientsAsync());

    [HttpPost("admin/ingredients")]
    [Authorize(Roles = nameof(Models.UserRole.Admin))]
    public async Task<ActionResult<IngredientDto>> Create(CreateIngredientRequest request)
    {
        try { return Ok(await _inventory.CreateIngredientAsync(request)); }
        catch (AppException ex) { return ex.ToActionResult(); }
    }

    [HttpPost("admin/ingredients/{id:guid}/adjust")]
    [Authorize(Roles = nameof(Models.UserRole.Admin))]
    public async Task<ActionResult<IngredientDto>> Adjust(Guid id, AdjustStockRequest request)
    {
        try { return Ok(await _inventory.AdjustStockAsync(id, request.Amount)); }
        catch (AppException ex) { return ex.ToActionResult(); }
    }

    [HttpGet("admin/ingredients/{id:guid}/ledger")]
    [Authorize(Roles = nameof(Models.UserRole.Admin))]
    public async Task<ActionResult<IReadOnlyList<InventoryTransactionDto>>> Ledger(Guid id, [FromQuery] int count = 50) =>
        Ok(await _inventory.GetLedgerAsync(id, count));

    [HttpGet("admin/menu/{menuItemId:guid}/recipe")]
    [Authorize(Roles = nameof(Models.UserRole.Admin))]
    public async Task<ActionResult<IReadOnlyList<RecipeLineDto>>> GetRecipe(Guid menuItemId) => Ok(await _inventory.GetRecipeAsync(menuItemId));

    [HttpPut("admin/menu/{menuItemId:guid}/recipe")]
    [Authorize(Roles = nameof(Models.UserRole.Admin))]
    public async Task<ActionResult<RecipeLineDto>> SetRecipeLine(Guid menuItemId, SetRecipeLineRequest request)
    {
        try { return Ok(await _inventory.SetRecipeLineAsync(menuItemId, request)); }
        catch (AppException ex) { return ex.ToActionResult(); }
    }

    [HttpDelete("admin/menu/{menuItemId:guid}/recipe/{ingredientId:guid}")]
    [Authorize(Roles = nameof(Models.UserRole.Admin))]
    public async Task<IActionResult> RemoveRecipeLine(Guid menuItemId, Guid ingredientId)
    {
        await _inventory.RemoveRecipeLineAsync(menuItemId, ingredientId);
        return NoContent();
    }
}
