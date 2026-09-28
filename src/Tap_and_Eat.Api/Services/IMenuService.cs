using TapAndEat.Api.DTOs;

namespace TapAndEat.Api.Services;

public class MenuException : Exception
{
    public MenuException(string message) : base(message) { }
}

public interface IMenuService
{
    Task<IReadOnlyList<MenuItemDto>> GetPublishedMenuAsync(string? tag);
    Task<MenuItemDto?> GetByIdAsync(Guid id);

    /// <summary>Updates stock and broadcasts the change over SignalR (Task 3.4).</summary>
    Task<MenuItemDto> SetStockAsync(Guid id, int newStockCount);
}
