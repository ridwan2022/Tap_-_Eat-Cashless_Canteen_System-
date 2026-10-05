using TapAndEat.Api.Db;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories.Sql;

public class SqlMenuRepository : IMenuRepository
{
    private readonly Database _db;
    public SqlMenuRepository(Database db) { _db = db; }

    private static MenuItem Map(DbRow r) => new()
    {
        Id = r.GetGuid("id"),
        Name = r.GetString("name"),
        Description = r.GetString("description"),
        Price = r.GetDecimal("price"),
        Category = r.GetString("category"),
        DietaryTags = r.GetString("dietary_tags").Split('\u001f', StringSplitOptions.RemoveEmptyEntries).ToList(),
        StockCount = r.GetInt32("stock_count"),
        LowStockThreshold = r.GetInt32("low_stock_threshold"),
        PrepTimeMinutes = r.GetInt32("prep_time_minutes"),
        IsPublished = r.GetBool("is_published"),
        IsOverride = r.GetBool("is_override"),
        CreatedAtUtc = r.GetDateTimeOffset("created_at"),
        UpdatedAtUtc = r.GetDateTimeOffset("updated_at")
    };

    // Dietary tags are a small, admin-entered set of short words (no commas/control
    // chars) — joined with U+001F (Unit Separator), a character that can never
    // appear in them, so no escaping is needed and no tag can "break out".
    private static string JoinTags(IEnumerable<string> tags) => string.Join('\u001f', tags);

    public async Task<MenuItem?> GetByIdAsync(Guid id) =>
        await _db.QuerySingleOrDefaultAsync("SELECT * FROM menu_items WHERE id = ?", Map, id);

    public async Task<IReadOnlyList<MenuItem>> GetAllAsync() =>
        await _db.QueryAsync("SELECT * FROM menu_items ORDER BY name", Map);

    public async Task<IReadOnlyList<MenuItem>> GetPublishedAsync(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return await _db.QueryAsync("SELECT * FROM menu_items WHERE is_published = 1 ORDER BY name", Map);
        }
        var rows = await _db.QueryAsync("SELECT * FROM menu_items WHERE is_published = 1 ORDER BY name", Map);
        return rows.Where(i => i.DietaryTags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public async Task<MenuItem> AddAsync(MenuItem item)
    {
        await _db.ExecuteAsync(
            """
            INSERT INTO menu_items (id, name, description, price, category, dietary_tags, stock_count,
                low_stock_threshold, prep_time_minutes, is_published, is_override, created_at, updated_at)
            VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?)
            """,
            item.Id, item.Name, item.Description, item.Price, item.Category, JoinTags(item.DietaryTags), item.StockCount,
            item.LowStockThreshold, item.PrepTimeMinutes, item.IsPublished, item.IsOverride, item.CreatedAtUtc, item.UpdatedAtUtc);
        return item;
    }

    public Task UpdateAsync(MenuItem item)
    {
        item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        return _db.ExecuteAsync(
            """
            UPDATE menu_items SET name=?, description=?, price=?, category=?, dietary_tags=?, stock_count=?,
                low_stock_threshold=?, prep_time_minutes=?, is_published=?, is_override=?, updated_at=? WHERE id=?
            """,
            item.Name, item.Description, item.Price, item.Category, JoinTags(item.DietaryTags), item.StockCount,
            item.LowStockThreshold, item.PrepTimeMinutes, item.IsPublished, item.IsOverride, item.UpdatedAtUtc, item.Id);
    }

    public async Task<bool> DeleteAsync(Guid id) => await _db.ExecuteAsync("DELETE FROM menu_items WHERE id = ?", id) > 0;
}
