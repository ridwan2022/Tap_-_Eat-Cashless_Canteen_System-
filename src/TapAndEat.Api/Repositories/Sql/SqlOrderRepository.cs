using TapAndEat.Api.Db;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories.Sql;

public class SqlOrderRepository : IOrderRepository
{
    private readonly Database _db;
    public SqlOrderRepository(Database db) { _db = db; }

    private static Order MapHeader(DbRow r) => new()
    {
        Id = r.GetGuid("id"),
        UserId = r.GetGuid("user_id"),
        Status = r.GetEnum<OrderStatus>("status"),
        CreatedAtUtc = r.GetDateTimeOffset("created_at"),
        ExpiresAtUtc = r.GetDateTimeOffset("expires_at"),
        PaidAtUtc = r.GetDateTimeOffsetOrNull("paid_at"),
        CollectedAtUtc = r.GetDateTimeOffsetOrNull("collected_at"),
        PaymentToken = r.GetStringOrNull("payment_token"),
        PaidByPaymentId = r.GetGuidOrNull("paid_by_payment_id")
    };

    private static OrderLine MapLine(DbRow r) => new()
    {
        MenuItemId = r.GetGuid("menu_item_id"),
        Name = r.GetString("name"),
        UnitPrice = r.GetDecimal("unit_price"),
        Quantity = r.GetInt32("quantity"),
        PrepTimeMinutes = r.GetInt32("prep_time_minutes")
    };

    private async Task AttachLinesAsync(List<Order> orders)
    {
        if (orders.Count == 0) return;
        var ids = string.Join(",", orders.Select(o => "'" + o.Id + "'"));
        var lines = await _db.QueryAsync($"SELECT * FROM order_lines WHERE order_id IN ({ids}) ORDER BY line_index",
            r => (OrderId: r.GetGuid("order_id"), Line: MapLine(r)));
        var byOrder = lines.ToLookup(l => l.OrderId);
        foreach (var o in orders) o.Lines.AddRange(byOrder[o.Id].Select(l => l.Line));
    }

    public async Task<Order?> GetByIdAsync(Guid id)
    {
        var order = await _db.QuerySingleOrDefaultAsync("SELECT * FROM orders WHERE id = ?", MapHeader, id);
        if (order is null) return null;
        var list = new List<Order> { order };
        await AttachLinesAsync(list);
        return list[0];
    }

    public async Task<IReadOnlyList<Order>> GetByUserAsync(Guid userId)
    {
        var orders = await _db.QueryAsync("SELECT * FROM orders WHERE user_id = ? ORDER BY created_at DESC", MapHeader, userId);
        await AttachLinesAsync(orders);
        return orders;
    }

    public async Task<IReadOnlyList<Order>> GetByStatusAsync(OrderStatus status)
    {
        var orders = await _db.QueryAsync("SELECT * FROM orders WHERE status = ? ORDER BY created_at", MapHeader, status.ToString());
        await AttachLinesAsync(orders);
        return orders;
    }

    public async Task<Order> AddAsync(Order order)
    {
        await _db.ExecuteInTransactionAsync(tx =>
        {
            tx.Execute(
                "INSERT INTO orders (id, user_id, status, created_at, expires_at, paid_at, collected_at, payment_token, paid_by_payment_id) VALUES (?,?,?,?,?,?,?,?,?)",
                order.Id, order.UserId, order.Status.ToString(), order.CreatedAtUtc, order.ExpiresAtUtc,
                order.PaidAtUtc, order.CollectedAtUtc, order.PaymentToken, order.PaidByPaymentId);
            for (var i = 0; i < order.Lines.Count; i++)
            {
                var l = order.Lines[i];
                tx.Execute(
                    "INSERT INTO order_lines (id, order_id, menu_item_id, name, unit_price, quantity, prep_time_minutes, line_index) VALUES (?,?,?,?,?,?,?,?)",
                    Guid.NewGuid(), order.Id, l.MenuItemId, l.Name, l.UnitPrice, l.Quantity, l.PrepTimeMinutes, i);
            }
        });
        return order;
    }

    public Task UpdateAsync(Order order) => _db.ExecuteAsync(
        "UPDATE orders SET status=?, paid_at=?, collected_at=?, payment_token=?, paid_by_payment_id=? WHERE id=?",
        order.Status.ToString(), order.PaidAtUtc, order.CollectedAtUtc, order.PaymentToken, order.PaidByPaymentId, order.Id);

    public async Task<(IReadOnlyList<Order> Items, int TotalCount)> GetHistoryPagedAsync(
        Guid userId, DateTimeOffset? from = null, DateTimeOffset? to = null, OrderStatus? status = null, int page = 1, int pageSize = 10)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var whereClauses = new List<string> { "user_id = ?" };
        var countParams = new List<DbParam> { userId };

        if (from.HasValue)
        {
            whereClauses.Add("created_at >= ?");
            countParams.Add(from.Value);
        }
        if (to.HasValue)
        {
            whereClauses.Add("created_at <= ?");
            countParams.Add(to.Value);
        }
        if (status.HasValue)
        {
            whereClauses.Add("status = ?");
            countParams.Add(status.Value.ToString());
        }

        var whereSql = string.Join(" AND ", whereClauses);
        var countRows = await _db.QueryAsync(
            $"SELECT COUNT(*) AS cnt FROM orders WHERE {whereSql}",
            r => r.GetInt32("cnt"),
            countParams.ToArray());
        var totalCount = countRows.Count > 0 ? countRows[0] : 0;

        var queryParams = new List<DbParam>(countParams)
        {
            pageSize,
            (page - 1) * pageSize
        };

        var orders = await _db.QueryAsync(
            $"SELECT * FROM orders WHERE {whereSql} ORDER BY created_at DESC LIMIT ? OFFSET ?",
            MapHeader,
            queryParams.ToArray());

        await AttachLinesAsync(orders);
        return (orders, totalCount);
    }
}
