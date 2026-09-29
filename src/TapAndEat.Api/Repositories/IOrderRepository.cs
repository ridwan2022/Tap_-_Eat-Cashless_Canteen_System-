using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<Order>> GetByUserAsync(Guid userId);
    Task<IReadOnlyList<Order>> GetByStatusAsync(OrderStatus status);
    Task<Order> AddAsync(Order order);
    Task UpdateAsync(Order order);
}
