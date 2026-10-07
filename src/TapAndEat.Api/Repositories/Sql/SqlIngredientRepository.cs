using TapAndEat.Api.Db;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories.Sql;

public class SqlIngredientRepository : IIngredientRepository
{
    private readonly Database _db;
    public SqlIngredientRepository(Database db) { _db = db; }

    private static Ingredient MapIngredient(DbRow r) => new()
    {
        Id = r.GetGuid("id"),
        Name = r.GetString("name"),
        Unit = r.GetString("unit"),
        StockQuantity = r.GetDecimal("stock_quantity"),
        ReorderThreshold = r.GetDecimal("reorder_threshold"),
        CreatedAtUtc = r.GetDateTimeOffset("created_at")
    };

    private static MenuItemIngredient MapRecipeLine(DbRow r) => new()
    {
        MenuItemId = r.GetGuid("menu_item_id"),
        IngredientId = r.GetGuid("ingredient_id"),
        QuantityPerItem = r.GetDecimal("quantity_per_item")
    };

    private static InventoryTransaction MapTx(DbRow r) => new()
    {
        Id = r.GetGuid("id"),
        IngredientId = r.GetGuid("ingredient_id"),
        ChangeAmount = r.GetDecimal("change_amount"),
        BalanceAfter = r.GetDecimal("balance_after"),
        Reason = r.GetEnum<InventoryTransactionReason>("reason"),
        ReferenceOrderId = r.GetGuidOrNull("reference_order_id"),
        CreatedAtUtc = r.GetDateTimeOffset("created_at")
    };

    public async Task<Ingredient?> GetByIdAsync(Guid id) =>
        await _db.QuerySingleOrDefaultAsync("SELECT * FROM ingredients WHERE id = ?", MapIngredient, id);

    public async Task<Ingredient?> GetByNameAsync(string name) =>
        await _db.QuerySingleOrDefaultAsync("SELECT * FROM ingredients WHERE name = ? COLLATE NOCASE", MapIngredient, name);

    public async Task<IReadOnlyList<Ingredient>> GetAllAsync() =>
        await _db.QueryAsync("SELECT * FROM ingredients ORDER BY name", MapIngredient);

    public async Task<Ingredient> AddAsync(Ingredient ingredient)
    {
        await _db.ExecuteAsync(
            "INSERT INTO ingredients (id, name, unit, stock_quantity, reorder_threshold, created_at) VALUES (?,?,?,?,?,?)",
            ingredient.Id, ingredient.Name, ingredient.Unit, ingredient.StockQuantity, ingredient.ReorderThreshold, ingredient.CreatedAtUtc);
        return ingredient;
    }

    public Task UpdateAsync(Ingredient ingredient) => _db.ExecuteAsync(
        "UPDATE ingredients SET name=?, unit=?, reorder_threshold=? WHERE id=?",
        ingredient.Name, ingredient.Unit, ingredient.ReorderThreshold, ingredient.Id);

    public async Task<IReadOnlyList<MenuItemIngredient>> GetRecipeAsync(Guid menuItemId) =>
        await _db.QueryAsync("SELECT * FROM menu_item_ingredients WHERE menu_item_id = ?", MapRecipeLine, menuItemId);

    public Task SetRecipeLineAsync(MenuItemIngredient line) => _db.ExecuteAsync(
        """
        INSERT INTO menu_item_ingredients (menu_item_id, ingredient_id, quantity_per_item) VALUES (?,?,?)
        ON CONFLICT(menu_item_id, ingredient_id) DO UPDATE SET quantity_per_item = excluded.quantity_per_item
        """,
        line.MenuItemId, line.IngredientId, line.QuantityPerItem);

    public Task RemoveRecipeLineAsync(Guid menuItemId, Guid ingredientId) => _db.ExecuteAsync(
        "DELETE FROM menu_item_ingredients WHERE menu_item_id = ? AND ingredient_id = ?", menuItemId, ingredientId);

    public Task<InventoryTransaction> AdjustStockAsync(Guid ingredientId, decimal change, InventoryTransactionReason reason, Guid? referenceOrderId) =>
        _db.ExecuteInTransactionAsync(tx =>
        {
            var current = tx.Query("SELECT stock_quantity FROM ingredients WHERE id = ?", r => r.GetDecimal("stock_quantity"), ingredientId);
            if (current.Count == 0) throw new InvalidOperationException("Ingredient not found.");

            // Consumption never drives stock below zero — depletion is a visibility/alerting
            // problem (IsLowStock / low-stock endpoint), not a reason to block an order that
            // already passed the (blocking) portion-stock check in MenuService.
            var applied = change < 0 ? -Math.Min(-change, current[0]) : change;
            var after = current[0] + applied;

            tx.Execute("UPDATE ingredients SET stock_quantity = ? WHERE id = ?", after, ingredientId);
            var txRow = new InventoryTransaction
            {
                IngredientId = ingredientId,
                ChangeAmount = applied,
                BalanceAfter = after,
                Reason = reason,
                ReferenceOrderId = referenceOrderId
            };
            tx.Execute(
                "INSERT INTO inventory_transactions (id, ingredient_id, change_amount, balance_after, reason, reference_order_id, created_at) VALUES (?,?,?,?,?,?,?)",
                txRow.Id, txRow.IngredientId, txRow.ChangeAmount, txRow.BalanceAfter, txRow.Reason.ToString(), txRow.ReferenceOrderId, txRow.CreatedAtUtc);
            return txRow;
        });

    public async Task<IReadOnlyList<InventoryTransaction>> GetLedgerAsync(Guid ingredientId, int count) =>
        await _db.QueryAsync("SELECT * FROM inventory_transactions WHERE ingredient_id = ? ORDER BY created_at DESC LIMIT ?", MapTx, ingredientId, count);
}
