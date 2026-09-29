using System.Collections.Concurrent;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public class InMemoryOrderRepository : IOrderRepository
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    public Task<Order?> GetByIdAsync(Guid id)
    {
        _orders.TryGetValue(id, out var order);
        return Task.FromResult(order);
    }

    public Task<IReadOnlyList<Order>> GetByUserAsync(Guid userId)
    {
        IReadOnlyList<Order> result = _orders.Values
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Order>> GetByStatusAsync(OrderStatus status)
    {
        IReadOnlyList<Order> result = _orders.Values
            .Where(o => o.Status == status)
            .OrderBy(o => o.CreatedAtUtc)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<Order> AddAsync(Order order)
    {
        _orders[order.Id] = order;
        return Task.FromResult(order);
    }

    public Task UpdateAsync(Order order)
    {
        _orders[order.Id] = order;
        return Task.CompletedTask;
    }
}
