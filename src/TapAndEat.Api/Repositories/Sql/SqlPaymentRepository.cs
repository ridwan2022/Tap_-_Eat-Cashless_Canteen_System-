using TapAndEat.Api.Db;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories.Sql;

public class SqlPaymentRepository : IPaymentRepository
{
    private readonly Database _db;
    public SqlPaymentRepository(Database db) { _db = db; }

    private static Payment Map(DbRow r) => new()
    {
        Id = r.GetGuid("id"),
        Purpose = r.GetEnum<PaymentPurpose>("purpose"),
        UserId = r.GetGuid("user_id"),
        OrderId = r.GetGuidOrNull("order_id"),
        Amount = r.GetDecimal("amount"),
        Method = r.GetEnum<PaymentMethod>("method"),
        Status = r.GetEnum<PaymentStatus>("status"),
        IdempotencyKey = r.GetStringOrNull("idempotency_key"),
        GatewayPaymentRef = r.GetStringOrNull("gateway_payment_ref"),
        GatewayTransactionId = r.GetStringOrNull("gateway_transaction_id"),
        RedirectUrl = r.GetStringOrNull("redirect_url"),
        FailureReason = r.GetStringOrNull("failure_reason"),
        RefundedToWallet = r.GetBool("refunded_to_wallet"),
        CreatedAtUtc = r.GetDateTimeOffset("created_at"),
        CompletedAtUtc = r.GetDateTimeOffsetOrNull("completed_at")
    };

    public async Task<Payment?> GetByIdAsync(Guid id) =>
        await _db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE id = ?", Map, id);

    public async Task<Payment?> GetByIdempotencyKeyAsync(Guid userId, string key) =>
        await _db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE user_id = ? AND idempotency_key = ?", Map, userId, key);

    public async Task<Payment?> GetInitiatedForOrderAsync(Guid orderId) =>
        await _db.QuerySingleOrDefaultAsync(
            "SELECT * FROM payments WHERE order_id = ? AND status = ? ORDER BY created_at DESC LIMIT 1",
            Map, orderId, PaymentStatus.Initiated.ToString());

    public async Task<IReadOnlyList<Payment>> GetForOrderAsync(Guid orderId) =>
        await _db.QueryAsync("SELECT * FROM payments WHERE order_id = ? ORDER BY created_at", Map, orderId);

    public async Task<Payment> AddAsync(Payment payment)
    {
        await _db.ExecuteAsync(
            """
            INSERT INTO payments (id, purpose, user_id, order_id, amount, method, status, idempotency_key,
                gateway_payment_ref, gateway_transaction_id, redirect_url, failure_reason, refunded_to_wallet, created_at, completed_at)
            VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)
            """,
            payment.Id, payment.Purpose.ToString(), payment.UserId, payment.OrderId, payment.Amount, payment.Method.ToString(),
            payment.Status.ToString(), payment.IdempotencyKey, payment.GatewayPaymentRef, payment.GatewayTransactionId,
            payment.RedirectUrl, payment.FailureReason, payment.RefundedToWallet, payment.CreatedAtUtc, payment.CompletedAtUtc);
        return payment;
    }

    public Task UpdateAsync(Payment payment) => _db.ExecuteAsync(
        """
        UPDATE payments SET status=?, gateway_payment_ref=?, gateway_transaction_id=?, redirect_url=?,
            failure_reason=?, refunded_to_wallet=?, completed_at=? WHERE id=?
        """,
        payment.Status.ToString(), payment.GatewayPaymentRef, payment.GatewayTransactionId, payment.RedirectUrl,
        payment.FailureReason, payment.RefundedToWallet, payment.CompletedAtUtc, payment.Id);
}
