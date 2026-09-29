using System.Collections.Concurrent;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public class InMemoryPaymentRepository : IPaymentRepository
{
    private readonly ConcurrentDictionary<Guid, Payment> _payments = new();

    public Task<Payment?> GetByIdAsync(Guid id)
    {
        _payments.TryGetValue(id, out var payment);
        return Task.FromResult(payment);
    }

    public Task<Payment?> GetByIdempotencyKeyAsync(Guid userId, string key)
    {
        var match = _payments.Values.FirstOrDefault(p =>
            p.UserId == userId && string.Equals(p.IdempotencyKey, key, StringComparison.Ordinal));
        return Task.FromResult(match);
    }

    public Task<Payment?> GetInitiatedForOrderAsync(Guid orderId)
    {
        var match = _payments.Values
            .Where(p => p.OrderId == orderId && p.Status == PaymentStatus.Initiated)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefault();
        return Task.FromResult(match);
    }

    public Task<IReadOnlyList<Payment>> GetForOrderAsync(Guid orderId)
    {
        IReadOnlyList<Payment> result = _payments.Values
            .Where(p => p.OrderId == orderId)
            .OrderBy(p => p.CreatedAtUtc)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<Payment> AddAsync(Payment payment)
    {
        _payments[payment.Id] = payment;
        return Task.FromResult(payment);
    }

    public Task UpdateAsync(Payment payment)
    {
        _payments[payment.Id] = payment;
        return Task.CompletedTask;
    }
}

