using TapAndEat.Api.DTOs;

namespace TapAndEat.Api.Services;

public class MenuException : Exception
{
    public MenuException(string message) : base(message) { }
}

public record StockLine(Guid MenuItemId, int Quantity);

public interface IMenuService
{
    Task<IReadOnlyList<MenuItemDto>> GetPublishedMenuAsync(string? tag);
    Task<MenuItemDto?> GetByIdAsync(Guid id);

    /// <summary>Updates stock and broadcasts the change over SignalR (Task 3.4).</summary>
    Task<MenuItemDto> SetStockAsync(Guid id, int newStockCount);

    /// <summary>
    /// Sprint 2 — reserves stock for an order, all-or-nothing: if any line is
    /// unpublished or short on stock nothing is decremented and a
    /// <see cref="MenuException"/> says which item. Broadcasts the new stock.
    /// </summary>
    Task ReserveStockAsync(IReadOnlyList<StockLine> lines);

    /// <summary>Sprint 2 — gives reserved stock back (order cancelled/expired). Broadcasts the new stock.</summary>
    Task ReleaseStockAsync(IReadOnlyList<StockLine> lines);
}
