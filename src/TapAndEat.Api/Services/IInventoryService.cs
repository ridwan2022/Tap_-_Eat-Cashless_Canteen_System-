using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Services;

public class InventoryException : AppException
{
    public InventoryException(ErrorKind kind, string message) : base(kind, message) { }
}

public interface IInventoryService
{
    Task<IReadOnlyList<IngredientDto>> GetIngredientsAsync();
    Task<IngredientDto> CreateIngredientAsync(CreateIngredientRequest request);

    /// <summary>Admin restock/correction — recorded in the ledger as ManualRestock (positive) or ManualAdjustment (negative).</summary>
    Task<IngredientDto> AdjustStockAsync(Guid ingredientId, decimal amount);
    Task<IReadOnlyList<InventoryTransactionDto>> GetLedgerAsync(Guid ingredientId, int count = 50);

    Task<IReadOnlyList<RecipeLineDto>> GetRecipeAsync(Guid menuItemId);
    Task<RecipeLineDto> SetRecipeLineAsync(Guid menuItemId, SetRecipeLineRequest request);
    Task RemoveRecipeLineAsync(Guid menuItemId, Guid ingredientId);

    /// <summary>
    /// Task 10.2 — deducts every ingredient a just-placed order's lines
    /// consume. Best-effort and never blocks or rolls back the order: an
    /// item with no recipe configured simply has nothing to deduct, and
    /// consumption never drives an ingredient below zero (see
    /// IIngredientRepository.AdjustStockAsync) — it only triggers the
    /// low-stock alert.
    /// </summary>
    Task DeductForOrderAsync(Order order);

    /// <summary>Task 10.4 — ingredients at or below their reorder threshold.</summary>
    Task<IReadOnlyList<IngredientDto>> GetLowStockAsync();
}
