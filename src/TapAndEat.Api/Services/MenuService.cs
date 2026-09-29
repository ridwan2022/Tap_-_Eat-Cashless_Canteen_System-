using Microsoft.AspNetCore.SignalR;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Hubs;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;

namespace TapAndEat.Api.Services;

/// <summary>Tasks 3.2, 3.4 — menu browsing API and real-time stock updates.</summary>
public class MenuService : IMenuService
{
    private readonly IMenuRepository _menu;
    private readonly IHubContext<MenuHub> _hub;

    public MenuService(IMenuRepository menu, IHubContext<MenuHub> hub)
    {
        _menu = menu;
        _hub = hub;
    }

    public async Task<IReadOnlyList<MenuItemDto>> GetPublishedMenuAsync(string? tag)
    {
        var items = await _menu.GetPublishedAsync(tag);
        return items.Select(MenuMapper.ToDto).ToList();
    }

    public async Task<MenuItemDto?> GetByIdAsync(Guid id)
    {
        var item = await _menu.GetByIdAsync(id);
        return item is null || !item.IsPublished ? null : MenuMapper.ToDto(item);
    }

    public async Task<MenuItemDto> SetStockAsync(Guid id, int newStockCount)
    {
        if (newStockCount < 0)
        {
            throw new MenuException("Stock count cannot be negative.");
        }
        
        var item = await _menu.GetByIdAsync(id);
        if (item is null)
        {
            throw new MenuException("Menu item not found.");
        }

        item.StockCount = newStockCount;
        await _menu.UpdateAsync(item);

        var dto = MenuMapper.ToDto(item);

        // Task 3.4 acceptance criteria: the menu UI reflects the new stock
        // value within a few seconds without a manual page refresh.
        await _hub.Clients.Group(MenuHub.MenuWatchersGroup).SendAsync(
            "StockUpdated",
            new StockUpdateNotification(item.Id, item.Name, item.StockCount, item.GetStockStatus().ToString()));

        return dto;
    }
}
