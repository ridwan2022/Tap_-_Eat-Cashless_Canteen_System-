using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(Guid id);

    /// <summary>Task 4.3 — lookup by the client-supplied idempotency key (scoped to the user).</summary>
    Task<Payment?> GetByIdempotencyKeyAsync(Guid userId, string key);

    /// <summary>The still-open (Initiated) payment for an order, if any.</summary>
    Task<Payment?> GetInitiatedForOrderAsync(Guid orderId);
    Task<IReadOnlyList<Payment>> GetForOrderAsync(Guid orderId);
    Task<Payment> AddAsync(Payment payment);
    Task UpdateAsync(Payment payment);
}
