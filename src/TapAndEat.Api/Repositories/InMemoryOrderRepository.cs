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

    public Task<(IReadOnlyList<Order> Items, int TotalCount)> GetHistoryPagedAsync(
        Guid userId, DateTimeOffset? from = null, DateTimeOffset? to = null, OrderStatus? status = null, int page = 1, int pageSize = 10)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _orders.Values.Where(o => o.UserId == userId);
        if (from.HasValue) query = query.Where(o => o.CreatedAtUtc >= from.Value);
        if (to.HasValue) query = query.Where(o => o.CreatedAtUtc <= to.Value);
        if (status.HasValue) query = query.Where(o => o.Status == status.Value);

        var ordered = query.OrderByDescending(o => o.CreatedAtUtc).ToList();
        var totalCount = ordered.Count;
        var paged = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Task.FromResult< (IReadOnlyList<Order> Items, int TotalCount) >((paged, totalCount));
    }
}
