using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public interface IIngredientRepository
{
    Task<Ingredient?> GetByIdAsync(Guid id);
    Task<Ingredient?> GetByNameAsync(string name);
    Task<IReadOnlyList<Ingredient>> GetAllAsync();
    Task<Ingredient> AddAsync(Ingredient ingredient);
    Task UpdateAsync(Ingredient ingredient);

    Task<IReadOnlyList<MenuItemIngredient>> GetRecipeAsync(Guid menuItemId);
    Task SetRecipeLineAsync(MenuItemIngredient line);
    Task RemoveRecipeLineAsync(Guid menuItemId, Guid ingredientId);

    /// <summary>
    /// Atomically adjusts one ingredient's stock and appends the audit entry,
    /// clamped at zero (ingredient depletion never blocks an order — see
    /// InventoryService — it only ever lowers stock toward, and not below,
    /// zero and raises a low-stock condition). Returns the transaction
    /// actually recorded (its ChangeAmount may be smaller than requested if
    /// clamped).
    /// </summary>
    Task<InventoryTransaction> AdjustStockAsync(Guid ingredientId, decimal change, InventoryTransactionReason reason, Guid? referenceOrderId);

    Task<IReadOnlyList<InventoryTransaction>> GetLedgerAsync(Guid ingredientId, int count);
}
