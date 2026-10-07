using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public class InMemoryIngredientRepository : IIngredientRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Ingredient> _ingredients = new();
    private readonly Dictionary<(Guid, Guid), MenuItemIngredient> _recipe = new();
    private readonly List<InventoryTransaction> _ledger = new();

    public Task<Ingredient?> GetByIdAsync(Guid id) { lock (_gate) { _ingredients.TryGetValue(id, out var i); return Task.FromResult(i); } }

    public Task<Ingredient?> GetByNameAsync(string name)
    {
        lock (_gate) { return Task.FromResult(_ingredients.Values.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))); }
    }

    public Task<IReadOnlyList<Ingredient>> GetAllAsync()
    {
        lock (_gate) { IReadOnlyList<Ingredient> all = _ingredients.Values.OrderBy(i => i.Name).ToList(); return Task.FromResult(all); }
    }

    public Task<Ingredient> AddAsync(Ingredient ingredient) { lock (_gate) { _ingredients[ingredient.Id] = ingredient; } return Task.FromResult(ingredient); }
    public Task UpdateAsync(Ingredient ingredient) { lock (_gate) { _ingredients[ingredient.Id] = ingredient; } return Task.CompletedTask; }

    public Task<IReadOnlyList<MenuItemIngredient>> GetRecipeAsync(Guid menuItemId)
    {
        lock (_gate) { IReadOnlyList<MenuItemIngredient> lines = _recipe.Values.Where(l => l.MenuItemId == menuItemId).ToList(); return Task.FromResult(lines); }
    }

    public Task SetRecipeLineAsync(MenuItemIngredient line) { lock (_gate) { _recipe[(line.MenuItemId, line.IngredientId)] = line; } return Task.CompletedTask; }
    public Task RemoveRecipeLineAsync(Guid menuItemId, Guid ingredientId) { lock (_gate) { _recipe.Remove((menuItemId, ingredientId)); } return Task.CompletedTask; }

    public Task<InventoryTransaction> AdjustStockAsync(Guid ingredientId, decimal change, InventoryTransactionReason reason, Guid? referenceOrderId)
    {
        lock (_gate)
        {
            var ingredient = _ingredients[ingredientId];
            var applied = change < 0 ? -Math.Min(-change, ingredient.StockQuantity) : change;
            ingredient.StockQuantity += applied;
            var tx = new InventoryTransaction { IngredientId = ingredientId, ChangeAmount = applied, BalanceAfter = ingredient.StockQuantity, Reason = reason, ReferenceOrderId = referenceOrderId };
            _ledger.Add(tx);
            return Task.FromResult(tx);
        }
    }

    public Task<IReadOnlyList<InventoryTransaction>> GetLedgerAsync(Guid ingredientId, int count)
    {
        lock (_gate)
        {
            IReadOnlyList<InventoryTransaction> rows = _ledger.Where(t => t.IngredientId == ingredientId)
                .OrderByDescending(t => t.CreatedAtUtc).Take(count).ToList();
            return Task.FromResult(rows);
        }
    }
}
