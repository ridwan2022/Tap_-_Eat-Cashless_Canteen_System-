using Microsoft.Extensions.Options;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;

namespace TapAndEat.Api.Services;

/// <summary>
/// Sprint 2 — the order lifecycle: PendingPayment → Paid → Collected, with
/// Cancelled / Expired as the unpaid exits. Stock is reserved up front and
/// released on either exit, so an abandoned checkout never hides a dish.
/// </summary>
public class OrderService : IOrderService
{
    private const int MaxDistinctItems = 30;

    private readonly IOrderRepository _orders;
    private readonly IMenuRepository _menuRepo;
    private readonly IMenuService _menu;
    private readonly IQueueService _queue;
    private readonly IPaymentTokenService _paymentTokens;
    private readonly IInventoryService? _inventory;
    private readonly INotificationService? _notifications;
    private readonly ILogger<OrderService>? _logger;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _paymentWindow;

    public OrderService(
        IOrderRepository orders,
        IMenuRepository menuRepo,
        IMenuService menu,
        IQueueService queue,
        IPaymentTokenService paymentTokens,
        IOptions<OrderOptions> options,
        TimeProvider clock,
        IInventoryService? inventory = null,
        INotificationService? notifications = null,
        ILogger<OrderService>? logger = null)
    {
        _orders = orders;
        _menuRepo = menuRepo;
        _menu = menu;
        _queue = queue;
        _paymentTokens = paymentTokens;
        _clock = clock;
        _inventory = inventory;
        _notifications = notifications;
        _logger = logger;
        _paymentWindow = TimeSpan.FromMinutes(Math.Max(1, options.Value.PaymentWindowMinutes));
    }

    public async Task<OrderDto> CreateOrderAsync(Guid userId, CreateOrderRequest request)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new OrderException(ErrorKind.Invalid, "Your order is empty.");
        }
        if (request.Items.Any(i => i.Quantity < 1 || i.Quantity > 20))
        {
            throw new OrderException(ErrorKind.Invalid, "Each item quantity must be between 1 and 20.");
        }

        // Same dish listed twice → one line.
        var merged = request.Items
            .GroupBy(i => i.MenuItemId)
            .Select(g => new StockLine(g.Key, g.Sum(i => i.Quantity)))
            .ToList();
        if (merged.Count > MaxDistinctItems)
        {
            throw new OrderException(ErrorKind.Invalid, $"An order can contain at most {MaxDistinctItems} different items.");
        }

        try
        {
            await _menu.ReserveStockAsync(merged);
        }
        catch (MenuException ex)
        {
            throw new OrderException(ErrorKind.Invalid, ex.Message);
        }

        var now = _clock.GetUtcNow();
        var order = new Order
        {
            UserId = userId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(_paymentWindow)
        };

        foreach (var line in merged)
        {
            var item = (await _menuRepo.GetByIdAsync(line.MenuItemId))!;
            order.Lines.Add(new OrderLine
            {
                MenuItemId = line.MenuItemId,
                Name = item.Name,
                UnitPrice = item.Price,
                Quantity = line.Quantity,
                PrepTimeMinutes = Math.Max(1, item.PrepTimeMinutes)
            });
        }

        await _orders.AddAsync(order);

        // Sprint 3 (Task 10.2) — ingredient deduction mirrors the portion-stock
        // reservation above: it happens the moment the order is placed, the
        // same instant the kitchen effectively commits to it. It is
        // best-effort and never blocks or undoes the order.
        if (_inventory is not null)
        {
            try { await _inventory.DeductForOrderAsync(order); }
            catch (Exception ex) { _logger?.LogError(ex, "Inventory deduction failed for order {OrderId}.", order.Id); }
        }

        return await ToDtoAsync(order);
    }

    public async Task<OrderDto> GetOrderAsync(Guid orderId, Guid requesterId, bool isStaff)
    {
        var order = await _orders.GetByIdAsync(orderId);
        if (order is null || (order.UserId != requesterId && !isStaff))
        {
            throw new OrderException(ErrorKind.NotFound, "Order not found.");
        }
        return await ToDtoAsync(order);
    }

    public async Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(Guid userId)
    {
        var orders = await _orders.GetByUserAsync(userId);
        var result = new List<OrderDto>();
        foreach (var order in orders)
        {
            result.Add(await ToDtoAsync(order));
        }
        return result;
    }

    public async Task<OrderDto> CancelOrderAsync(Guid orderId, Guid userId)
    {
        using var _ = await KeyedLock.AcquireAsync($"order:{orderId}");
        var order = await _orders.GetByIdAsync(orderId);
        if (order is null || order.UserId != userId)
        {
            throw new OrderException(ErrorKind.NotFound, "Order not found.");
        }
        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new OrderException(ErrorKind.Conflict,
                order.Status == OrderStatus.Cancelled || order.Status == OrderStatus.Expired
                    ? "This order is already closed."
                    : "A paid order can't be cancelled here — please speak to the counter staff.");
        }

        order.Status = OrderStatus.Cancelled;
        await _orders.UpdateAsync(order);
        await _menu.ReleaseStockAsync(StockLines(order));
        return await ToDtoAsync(order);
    }

    public async Task<OrderDto> MarkPaidAsync(Guid orderId, Guid paymentId)
    {
        using var _ = await KeyedLock.AcquireAsync($"order:{orderId}");
        var order = await _orders.GetByIdAsync(orderId)
            ?? throw new OrderException(ErrorKind.NotFound, "Order not found.");

        if (order.Status == OrderStatus.Paid && order.PaidByPaymentId == paymentId)
        {
            return await ToDtoAsync(order); // replay of the same confirmation — nothing more to do
        }
        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new OrderException(ErrorKind.Conflict, "This order can no longer be paid.");
        }

        order.Status = OrderStatus.Paid;
        order.PaidAtUtc = _clock.GetUtcNow();
        order.PaidByPaymentId = paymentId;
        order.PaymentToken = _paymentTokens.Issue(order.Id, paymentId);
        await _orders.UpdateAsync(order);

        var token = await _queue.EnsureTokenForOrderAsync(order);

        if (_notifications is not null)
        {
            try { await _notifications.NotifyOrderPaidAsync(order.Id, token.TokenNumber, order.Total, order.UserId); }
            catch (Exception ex) { _logger?.LogError(ex, "Failed to send order paid notification for order {OrderId}.", order.Id); }
        }

        return await ToDtoAsync(order);
    }

    public async Task MarkCollectedAsync(Guid orderId)
    {
        using var _ = await KeyedLock.AcquireAsync($"order:{orderId}");
        var order = await _orders.GetByIdAsync(orderId)
            ?? throw new OrderException(ErrorKind.NotFound, "Order not found.");
        if (order.Status == OrderStatus.Collected) return;
        if (order.Status != OrderStatus.Paid)
        {
            throw new OrderException(ErrorKind.Conflict, "Only a paid order can be collected.");
        }

        order.Status = OrderStatus.Collected;
        order.CollectedAtUtc = _clock.GetUtcNow();
        await _orders.UpdateAsync(order);

        if (_notifications is not null)
        {
            var token = await _queue.GetTokenForOrderAsync(order.Id);
            if (token is not null)
            {
                try { await _notifications.NotifyTokenStatusChangedAsync(token.Id, QueueTokenStatus.Collected); }
                catch (Exception ex) { _logger?.LogError(ex, "Failed to send order collected notification for order {OrderId}.", order.Id); }
            }
        }
    }

    public async Task<int> ExpireStaleOrdersAsync()
    {
        var now = _clock.GetUtcNow();
        var pending = await _orders.GetByStatusAsync(OrderStatus.PendingPayment);
        var expired = 0;

        foreach (var candidate in pending.Where(o => o.ExpiresAtUtc <= now))
        {
            using var _ = await KeyedLock.AcquireAsync($"order:{candidate.Id}");
            var order = await _orders.GetByIdAsync(candidate.Id);
            if (order is null || order.Status != OrderStatus.PendingPayment) continue; // paid/cancelled while we waited

            order.Status = OrderStatus.Expired;
            await _orders.UpdateAsync(order);
            await _menu.ReleaseStockAsync(StockLines(order));
            expired++;
        }

        return expired;
    }

    private static List<StockLine> StockLines(Order order) =>
        order.Lines.Select(l => new StockLine(l.MenuItemId, l.Quantity)).ToList();

    private async Task<OrderDto> ToDtoAsync(Order order)
    {
        QueueTokenDto? token = null;
        if (order.Status is OrderStatus.Paid or OrderStatus.Collected)
        {
            token = await _queue.GetTokenForOrderAsync(order.Id);
        }

        return new OrderDto(
            order.Id,
            order.Total,
            order.Status.ToString(),
            order.Lines.Select(l => new OrderLineDto(l.MenuItemId, l.Name, l.UnitPrice, l.Quantity, l.LineTotal)).ToList(),
            order.CreatedAtUtc,
            order.ExpiresAtUtc,
            order.PaidAtUtc,
            order.PaymentToken,
            token);
    }

    public async Task<OrderHistoryPageDto> GetOrderHistoryAsync(
        Guid userId, DateTimeOffset? from = null, DateTimeOffset? to = null, OrderStatus? status = null, int page = 1, int pageSize = 10)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, totalCount) = await _orders.GetHistoryPagedAsync(userId, from, to, status, page, pageSize);
        var dtos = new List<OrderDto>();
        foreach (var order in items)
        {
            dtos.Add(await ToDtoAsync(order));
        }

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        var allUserOrders = await _orders.GetByUserAsync(userId);
        var filteredAll = allUserOrders.AsEnumerable();
        if (from.HasValue) filteredAll = filteredAll.Where(o => o.CreatedAtUtc >= from.Value);
        if (to.HasValue) filteredAll = filteredAll.Where(o => o.CreatedAtUtc <= to.Value);
        if (status.HasValue) filteredAll = filteredAll.Where(o => o.Status == status.Value);
        var totalSpent = filteredAll.Where(o => o.Status is OrderStatus.Paid or OrderStatus.Collected).Sum(o => o.Total);

        return new OrderHistoryPageDto(totalCount, page, pageSize, totalPages, totalSpent, dtos);
    }
}
