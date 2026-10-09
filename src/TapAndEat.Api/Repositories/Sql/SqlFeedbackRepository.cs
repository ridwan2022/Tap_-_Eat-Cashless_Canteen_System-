using TapAndEat.Api.Db;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories.Sql;

public class SqlFeedbackRepository : IFeedbackRepository
{
    private readonly Database _db;

    public SqlFeedbackRepository(Database db)
    {
        _db = db;
    }

    private static OrderFeedback Map(DbRow r) => new()
    {
        Id = r.GetGuid("id"),
        OrderId = r.GetGuid("order_id"),
        UserId = r.GetGuid("user_id"),
        UserName = r.GetString("user_name"),
        Rating = r.GetInt32("rating"),
        Comment = r.GetString("comment"),
        MenuItemId = r.GetGuidOrNull("menu_item_id"),
        CreatedAtUtc = r.GetDateTimeOffset("created_at"),
        UpdatedAtUtc = r.GetDateTimeOffset("updated_at")
    };

    public async Task<OrderFeedback> UpsertAsync(OrderFeedback feedback)
    {
        await _db.ExecuteAsync(
            """
            INSERT INTO order_feedbacks (id, order_id, user_id, user_name, rating, comment, menu_item_id, created_at, updated_at)
            VALUES (?,?,?,?,?,?,?,?,?)
            ON CONFLICT(order_id) DO UPDATE SET
                rating = excluded.rating,
                comment = excluded.comment,
                updated_at = excluded.updated_at
            """,
            feedback.Id, feedback.OrderId, feedback.UserId, feedback.UserName, feedback.Rating, feedback.Comment,
            feedback.MenuItemId, feedback.CreatedAtUtc, feedback.UpdatedAtUtc);
        return feedback;
    }

    public async Task<OrderFeedback?> GetByOrderIdAsync(Guid orderId) =>
        await _db.QuerySingleOrDefaultAsync("SELECT * FROM order_feedbacks WHERE order_id = ?", Map, orderId);

    public async Task<IReadOnlyList<OrderFeedback>> GetByUserIdAsync(Guid userId) =>
        await _db.QueryAsync("SELECT * FROM order_feedbacks WHERE user_id = ? ORDER BY created_at DESC", Map, userId);

    public async Task<IReadOnlyList<OrderFeedback>> GetAllAsync(int limit = 100) =>
        await _db.QueryAsync("SELECT * FROM order_feedbacks ORDER BY created_at DESC LIMIT ?", Map, limit);

    public async Task<FeedbackSummaryData> GetSummaryAsync()
    {
        var all = await _db.QueryAsync("SELECT * FROM order_feedbacks ORDER BY created_at DESC", Map);
        var summary = new FeedbackSummaryData
        {
            TotalReviews = all.Count,
            AverageRating = all.Count > 0 ? Math.Round(all.Average(f => f.Rating), 1) : 0,
            StarCounts = Enumerable.Range(1, 5).ToDictionary(i => i, i => all.Count(f => f.Rating == i)),
            RecentReviews = all.Take(15).ToList()
        };

        // Get dish names
        var itemRows = await _db.QueryAsync(
            """
            SELECT f.menu_item_id AS item_id, COALESCE(m.name, 'Dish') AS item_name, AVG(f.rating) AS avg_rating, COUNT(*) AS review_count
            FROM order_feedbacks f
            LEFT JOIN menu_items m ON m.id = f.menu_item_id
            WHERE f.menu_item_id IS NOT NULL
            GROUP BY f.menu_item_id
            """,
            r => new MenuItemFeedbackRating
            {
                MenuItemId = r.GetGuid("item_id"),
                MenuItemName = r.GetString("item_name"),
                AverageRating = decimal.TryParse(r.GetString("avg_rating"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? (double)Math.Round(d, 1) : 0,
                ReviewCount = r.GetInt32("review_count")
            });

        summary.ItemRatings = itemRows;
        return summary;
    }
}
