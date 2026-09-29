using Microsoft.AspNetCore.SignalR;

namespace TapAndEat.Api.Hubs;

/// <summary>
/// Task 3.4 — real-time stock update mechanism. Clients connect and are
/// automatically added to the "menu-watchers" group; MenuService broadcasts
/// a "StockUpdated" event to that group whenever a menu item's stock count
/// changes, so the menu UI reflects the new value within seconds without a
/// manual page refresh. No client-invokable methods are needed for Sprint 1.
/// </summary>
public class MenuHub : Hub
{
    public const string MenuWatchersGroup = "menu-watchers";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, MenuWatchersGroup);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, MenuWatchersGroup);
        await base.OnDisconnectedAsync(exception);
    }
}
