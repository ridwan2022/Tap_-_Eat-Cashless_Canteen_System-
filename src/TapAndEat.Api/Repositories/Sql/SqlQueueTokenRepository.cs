using TapAndEat.Api.Db;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories.Sql;

public class SqlQueueTokenRepository : IQueueTokenRepository
{
    private readonly Database _db;
    public SqlQueueTokenRepository(Database db) { _db = db; }

    private static QueueToken Map(DbRow r) => new()
    {
        Id = r.GetGuid("id"),
        OrderId = r.GetGuid("order_id"),
        DayKey = r.GetString("day_key"),
        TokenNumber = r.GetInt32("token_number"),
        Status = r.GetEnum<QueueTokenStatus>("status"),
        EstimatedPrepMinutes = r.GetInt32("estimated_prep_minutes"),
        WorkMinutes = r.GetInt32("work_minutes"),
        OwnMinutes = r.GetInt32("own_minutes"),
        CreatedAtUtc = r.GetDateTimeOffset("created_at"),
        StartedAtUtc = r.GetDateTimeOffsetOrNull("started_at"),
        ReadyAtUtc = r.GetDateTimeOffsetOrNull("ready_at"),
        CollectedAtUtc = r.GetDateTimeOffsetOrNull("collected_at")
    };

    public Task<QueueToken> AddForOrderAsync(Guid orderId, string dayKey, Func<int, QueueToken> factory) =>
        _db.ExecuteInTransactionAsync(tx =>
        {
            var existing = tx.Query("SELECT * FROM queue_tokens WHERE order_id = ?", Map, orderId);
            if (existing.Count > 0) return existing[0];

            var last = tx.Query("SELECT MAX(token_number) n FROM queue_tokens WHERE day_key = ?", r => r.IsNull("n") ? 0 : r.GetInt32("n"), dayKey);
            var token = factory(last[0] + 1);

            tx.Execute(
                """
                INSERT INTO queue_tokens (id, order_id, day_key, token_number, status, estimated_prep_minutes,
                    work_minutes, own_minutes, created_at, started_at, ready_at, collected_at)
                VALUES (?,?,?,?,?,?,?,?,?,?,?,?)
                """,
                token.Id, token.OrderId, token.DayKey, token.TokenNumber, token.Status.ToString(), token.EstimatedPrepMinutes,
                token.WorkMinutes, token.OwnMinutes, token.CreatedAtUtc, token.StartedAtUtc, token.ReadyAtUtc, token.CollectedAtUtc);
            return token;
        });

    public async Task<QueueToken?> GetByIdAsync(Guid id) =>
        await _db.QuerySingleOrDefaultAsync("SELECT * FROM queue_tokens WHERE id = ?", Map, id);

    public async Task<QueueToken?> GetByOrderIdAsync(Guid orderId) =>
        await _db.QuerySingleOrDefaultAsync("SELECT * FROM queue_tokens WHERE order_id = ?", Map, orderId);

    public async Task<QueueToken?> GetByNumberAsync(string dayKey, int tokenNumber) =>
        await _db.QuerySingleOrDefaultAsync("SELECT * FROM queue_tokens WHERE day_key = ? AND token_number = ?", Map, dayKey, tokenNumber);

    public async Task<IReadOnlyList<QueueToken>> GetActiveAsync() =>
        await _db.QueryAsync("SELECT * FROM queue_tokens WHERE status != ? ORDER BY created_at, token_number", Map, QueueTokenStatus.Collected.ToString());

    public Task UpdateAsync(QueueToken token) => _db.ExecuteAsync(
        "UPDATE queue_tokens SET status=?, started_at=?, ready_at=?, collected_at=? WHERE id=?",
        token.Status.ToString(), token.StartedAtUtc, token.ReadyAtUtc, token.CollectedAtUtc, token.Id);
}
