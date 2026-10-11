using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;

namespace TapAndEat.Api.Services;

/// <summary>Tasks 10.1–10.4 — ingredient stock, recipes, and auto-deduction as orders are placed.</summary>
public class InventoryService : IInventoryService
{
    private readonly IIngredientRepository _ingredients;
    private readonly IMenuRepository _menu;
    private readonly INotificationService? _notifications;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(
        IIngredientRepository ingredients,
        IMenuRepository menu,
        ILogger<InventoryService> logger,
        INotificationService? notifications = null)
    {
        _ingredients = ingredients;
        _menu = menu;
        _logger = logger;
        _notifications = notifications;
    }

    public async Task<IReadOnlyList<IngredientDto>> GetIngredientsAsync() =>
        (await _ingredients.GetAllAsync()).Select(ToDto).ToList();

    public async Task<IngredientDto> CreateIngredientAsync(CreateIngredientRequest request)
    {
        var name = request.Name.Trim();
        if (name.Length == 0) throw new InventoryException(ErrorKind.Invalid, "Ingredient name is required.");
        if (await _ingredients.GetByNameAsync(name) is not null)
        {
            throw new InventoryException(ErrorKind.Conflict, $"An ingredient named '{name}' already exists.");
        }
        if (request.InitialStock < 0 || request.ReorderThreshold < 0)
        {
            throw new InventoryException(ErrorKind.Invalid, "Stock and threshold can't be negative.");
        }

        var ingredient = new Ingredient { Name = name, Unit = request.Unit.Trim(), StockQuantity = request.InitialStock, ReorderThreshold = request.ReorderThreshold };
        await _ingredients.AddAsync(ingredient);
        return ToDto(ingredient);
    }

    public async Task<IngredientDto> AdjustStockAsync(Guid ingredientId, decimal amount)
    {
        if (await _ingredients.GetByIdAsync(ingredientId) is null)
        {
            throw new InventoryException(ErrorKind.NotFound, "Ingredient not found.");
        }
        await _ingredients.AdjustStockAsync(ingredientId, amount,
            amount >= 0 ? InventoryTransactionReason.ManualRestock : InventoryTransactionReason.ManualAdjustment, null);
        var updated = (await _ingredients.GetByIdAsync(ingredientId))!;
        if (updated.IsLowStock && _notifications is not null)
        {
            try { await _notifications.NotifyLowStockAsync(updated.Id, updated.Name, updated.StockQuantity, updated.Unit); }
            catch { /* best effort */ }
        }
        return ToDto(updated);
    }

    public async Task<IReadOnlyList<InventoryTransactionDto>> GetLedgerAsync(Guid ingredientId, int count = 50) =>
        (await _ingredients.GetLedgerAsync(ingredientId, count))
        .Select(t => new InventoryTransactionDto(t.Id, t.ChangeAmount, t.BalanceAfter, t.Reason.ToString(), t.ReferenceOrderId, t.CreatedAtUtc))
        .ToList();

    public async Task<IReadOnlyList<RecipeLineDto>> GetRecipeAsync(Guid menuItemId)
    {
        var lines = await _ingredients.GetRecipeAsync(menuItemId);
        var result = new List<RecipeLineDto>();
        foreach (var line in lines)
        {
            var ingredient = await _ingredients.GetByIdAsync(line.IngredientId);
            if (ingredient is not null) result.Add(new RecipeLineDto(ingredient.Id, ingredient.Name, ingredient.Unit, line.QuantityPerItem));
        }
        return result;
    }

    public async Task<RecipeLineDto> SetRecipeLineAsync(Guid menuItemId, SetRecipeLineRequest request)
    {
        if (await _menu.GetByIdAsync(menuItemId) is null) throw new InventoryException(ErrorKind.NotFound, "Menu item not found.");
        var ingredient = await _ingredients.GetByIdAsync(request.IngredientId)
            ?? throw new InventoryException(ErrorKind.NotFound, "Ingredient not found.");

        await _ingredients.SetRecipeLineAsync(new MenuItemIngredient { MenuItemId = menuItemId, IngredientId = request.IngredientId, QuantityPerItem = request.QuantityPerItem });
        return new RecipeLineDto(ingredient.Id, ingredient.Name, ingredient.Unit, request.QuantityPerItem);
    }

    public Task RemoveRecipeLineAsync(Guid menuItemId, Guid ingredientId) => _ingredients.RemoveRecipeLineAsync(menuItemId, ingredientId);

    public async Task DeductForOrderAsync(Order order)
    {
        foreach (var line in order.Lines)
        {
            IReadOnlyList<MenuItemIngredient> recipe;
            try { recipe = await _ingredients.GetRecipeAsync(line.MenuItemId); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not read the recipe for menu item {MenuItemId}; skipping inventory deduction for order {OrderId}.", line.MenuItemId, order.Id);
                continue;
            }

            foreach (var recipeLine in recipe)
            {
                try
                {
                    await _ingredients.AdjustStockAsync(
                        recipeLine.IngredientId, -(recipeLine.QuantityPerItem * line.Quantity), InventoryTransactionReason.OrderDeduction, order.Id);

                    var current = await _ingredients.GetByIdAsync(recipeLine.IngredientId);
                    if (current is not null && current.IsLowStock && _notifications is not null)
                    {
                        try { await _notifications.NotifyLowStockAsync(current.Id, current.Name, current.StockQuantity, current.Unit); }
                        catch { /* best effort */ }
                    }
                }
                catch (Exception ex)
                {
                    // Best-effort: a missing/renamed ingredient must never fail the order itself.
                    _logger.LogError(ex, "Inventory deduction failed for ingredient {IngredientId} on order {OrderId}.", recipeLine.IngredientId, order.Id);
                }
            }
        }
    }

    public async Task<IReadOnlyList<IngredientDto>> GetLowStockAsync() =>
        (await _ingredients.GetAllAsync()).Where(i => i.IsLowStock).Select(ToDto).ToList();

    private static IngredientDto ToDto(Ingredient i) => new(i.Id, i.Name, i.Unit, i.StockQuantity, i.ReorderThreshold, i.IsLowStock);
}
