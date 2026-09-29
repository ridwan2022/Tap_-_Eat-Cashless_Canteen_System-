using Microsoft.AspNetCore.SignalR;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Hubs;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;

namespace TapAndEat.Api.Services;

/// <summary>Tasks 3.2, 3.4 — menu browsing API and real-time stock updates.</summary>
public class MenuService : IMenuService
{
    // One process-wide gate so two orders can never both take the last portion.
    private static readonly SemaphoreSlim StockGate = new(1, 1);

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
        await BroadcastStockAsync(item);

        return dto;
    }

    public async Task ReserveStockAsync(IReadOnlyList<StockLine> lines)
    {
        var touched = new List<MenuItem>();
        await StockGate.WaitAsync();
        try
        {
            var plan = new List<(MenuItem Item, int Quantity)>();
            foreach (var line in lines)
            {
                var item = await _menu.GetByIdAsync(line.MenuItemId);
                if (item is null || !item.IsPublished)
                {
                    throw new MenuException("One of the items in your order is no longer on the menu.");
                }

                var alreadyPlanned = plan.Where(p => p.Item.Id == item.Id).Sum(p => p.Quantity);
                var available = item.StockCount - alreadyPlanned;
                if (available < line.Quantity)
                {
                    throw new MenuException(available <= 0
                        ? $"{item.Name} is out of stock."
                        : $"Only {available} of {item.Name} left.");
                }
                plan.Add((item, line.Quantity));
            }

            foreach (var (item, quantity) in plan)
            {
                item.StockCount -= quantity;
                await _menu.UpdateAsync(item);
                touched.Add(item);
            }
        }
        finally
        {
            StockGate.Release();
        }

        foreach (var item in touched.DistinctBy(i => i.Id))
        {
            await BroadcastStockAsync(item);
        }
    }

    public async Task ReleaseStockAsync(IReadOnlyList<StockLine> lines)
    {
        var touched = new List<MenuItem>();
        await StockGate.WaitAsync();
        try
        {
            foreach (var line in lines)
            {
                var item = await _menu.GetByIdAsync(line.MenuItemId);
                if (item is null) continue; // item was deleted meanwhile — nothing to give back
                item.StockCount += line.Quantity;
                await _menu.UpdateAsync(item);
                touched.Add(item);
            }
        }
        finally
        {
            StockGate.Release();
        }

        foreach (var item in touched.DistinctBy(i => i.Id))
        {
            await BroadcastStockAsync(item);
        }
    }

    private Task BroadcastStockAsync(MenuItem item) =>
        _hub.Clients.Group(MenuHub.MenuWatchersGroup).SendAsync(
            "StockUpdated",
            new StockUpdateNotification(item.Id, item.Name, item.StockCount, item.GetStockStatus().ToString()));
}
