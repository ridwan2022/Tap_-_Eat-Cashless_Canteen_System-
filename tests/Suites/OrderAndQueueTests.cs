using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Services;
using TapAndEat.Tests.Framework;

namespace TapAndEat.Tests.Suites;

/// <summary>Tasks 7.1–7.4 plus the order lifecycle Sprint 2 introduces.</summary>
public static class OrderAndQueueTests
{
    public static void Register(TestRunner runner)
    {
        const string orders = "OrderService (Sprint 2)";
        const string queue = "QueueService (Tasks 7.1-7.4)";

        runner.Add(orders, "Placing an order reserves stock and snapshots prices", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var item = await f.AddMenuItemAsync(price: 120m, stock: 10);

            var order = await f.PlaceOrderAsync(user, item, 3);

            Assert.Equal(360m, order.Total);
            Assert.Equal("PendingPayment", order.Status);
            Assert.Equal(7, (await f.Menu.GetByIdAsync(item.Id))!.StockCount);
            item.Price = 999m; // later price edits must not change a placed order
            Assert.Equal(120m, (await f.OrderService.GetOrderAsync(order.Id, user.Id, false)).Lines[0].UnitPrice);
        });

        runner.Add(orders, "Ordering more than the stock is rejected and nothing is reserved", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var a = await f.AddMenuItemAsync("A", stock: 5);
            var b = await f.AddMenuItemAsync("B", stock: 1);

            var ex = await Assert.ThrowsAsync<OrderException>(() => f.OrderService.CreateOrderAsync(user.Id,
                new CreateOrderRequest(new List<OrderItemRequest> { new(a.Id, 2), new(b.Id, 2) })));

            Assert.True(ex.Message.Contains("Only 1 of B"), ex.Message);
            Assert.Equal(5, (await f.Menu.GetByIdAsync(a.Id))!.StockCount); // all-or-nothing
        });

        runner.Add(orders, "Unpublished items and empty carts are rejected", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var draft = new MenuItem { Name = "Draft", Price = 10, StockCount = 5, IsPublished = false };
            await f.Menu.AddAsync(draft);

            await Assert.ThrowsAsync<OrderException>(() => f.PlaceOrderAsync(user, draft));
            await Assert.ThrowsAsync<OrderException>(() => f.OrderService.CreateOrderAsync(user.Id, new CreateOrderRequest(new())));
        });

        runner.Add(orders, "Cancelling an unpaid order releases its stock; paid orders can't be cancelled", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddFundedUserAsync();
            var item = await f.AddMenuItemAsync(stock: 10);
            var unpaid = await f.PlaceOrderAsync(user, item, 4);

            await f.OrderService.CancelOrderAsync(unpaid.Id, user.Id);
            Assert.Equal(10, (await f.Menu.GetByIdAsync(item.Id))!.StockCount);

            var paid = await f.PlaceAndPayAsync(user, item);
            var ex = await Assert.ThrowsAsync<OrderException>(() => f.OrderService.CancelOrderAsync(paid.Id, user.Id));
            Assert.Equal(ErrorKind.Conflict, ex.Kind);
        });

        runner.Add(orders, "Unpaid orders expire after the payment window and release stock", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var item = await f.AddMenuItemAsync(stock: 10);
            var order = await f.PlaceOrderAsync(user, item, 2);

            f.Clock.Advance(TimeSpan.FromMinutes(5));
            Assert.Equal(0, await f.OrderService.ExpireStaleOrdersAsync());
            f.Clock.Advance(TimeSpan.FromMinutes(11));
            Assert.Equal(1, await f.OrderService.ExpireStaleOrdersAsync());

            Assert.Equal("Expired", (await f.OrderService.GetOrderAsync(order.Id, user.Id, false)).Status);
            Assert.Equal(10, (await f.Menu.GetByIdAsync(item.Id))!.StockCount);
        });

        runner.Add(orders, "A customer can't read someone else's order", async () =>
        {
            var f = new ServiceFixture();
            var owner = await f.AddUserAsync();
            var other = await f.AddUserAsync();
            var order = await f.PlaceOrderAsync(owner, await f.AddMenuItemAsync());

            await Assert.ThrowsAsync<OrderException>(() => f.OrderService.GetOrderAsync(order.Id, other.Id, isStaff: false));
            Assert.NotNull(await f.OrderService.GetOrderAsync(order.Id, other.Id, isStaff: true));
        });

        // ------------------------------------------------------------ queue

        runner.Add(queue, "Estimator: empty kitchen = the order's own time; queue load is shared across stations", () =>
        {
            Assert.Equal(5, PrepTimeEstimator.Estimate(0, 5, 2));
            Assert.Equal(10, PrepTimeEstimator.Estimate(10, 5, 2)); // ceil(10/2)+5
            Assert.Equal(11, PrepTimeEstimator.Estimate(11, 5, 2)); // ceil(5.5)=6 → 11
            Assert.Equal(1, PrepTimeEstimator.Estimate(0, 0, 0));    // never below 1, stations clamped
            return Task.CompletedTask;
        });

        runner.Add(queue, "Paying gives exactly one token, numbered from 1, with a stored estimate", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddFundedUserAsync();
            var item = await f.AddMenuItemAsync(prep: 6);

            var order = await f.PlaceAndPayAsync(user, item);

            Assert.NotNull(order.Token);
            Assert.Equal(1, order.Token!.TokenNumber);
            Assert.Equal("T-001", order.Token.Label);
            Assert.Equal("Queued", order.Token.Status);
            Assert.Equal(6, order.Token.EstimatedPrepMinutes);
        });

        runner.Add(queue, "Token numbers are unique per day and restart the next day", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddFundedUserAsync(5000);
            var item = await f.AddMenuItemAsync(stock: 50);

            var numbers = new List<int>();
            for (var i = 0; i < 5; i++) numbers.Add((await f.PlaceAndPayAsync(user, item)).Token!.TokenNumber);
            Assert.Equal(5, numbers.Distinct().Count());

            f.Clock.Advance(TimeSpan.FromDays(1));
            Assert.Equal(1, (await f.PlaceAndPayAsync(user, item)).Token!.TokenNumber);
        });

        runner.Add(queue, "EnsureToken is idempotent: a second call returns the same token", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddFundedUserAsync();
            var paid = await f.PlaceAndPayAsync(user, await f.AddMenuItemAsync());
            var order = (await f.Orders.GetByIdAsync(paid.Id))!;

            var again = await f.Queue.EnsureTokenForOrderAsync(order);

            Assert.Equal(paid.Token!.Id, again.Id);
            Assert.Equal(1, (await f.Queue.GetBoardAsync()).Tokens.Count);
        });

        runner.Add(queue, "A busier queue gives a longer estimate, and it drops as orders become ready", async () =>
        {
            var f = new ServiceFixture { Stations = 1 };
            var user = await f.AddFundedUserAsync(5000);
            var item = await f.AddMenuItemAsync(stock: 50, prep: 5);

            var first = await f.PlaceAndPayAsync(user, item, 2);   // 10 min of work
            var second = await f.PlaceAndPayAsync(user, item, 1);
            var third = await f.PlaceAndPayAsync(user, item, 1);

            Assert.Equal(5, first.Token!.EstimatedPrepMinutes);
            Assert.True(second.Token!.EstimatedPrepMinutes > first.Token.EstimatedPrepMinutes, "second should wait longer");
            Assert.True(third.Token!.EstimatedPrepMinutes > second.Token.EstimatedPrepMinutes, "third should wait longest");

            var before = (await f.Queue.GetBoardAsync()).Tokens.Single(t => t.Id == third.Token.Id).RemainingMinutes;
            await f.Queue.SetStatusAsync(first.Token.Id, QueueTokenStatus.Ready);
            var after = (await f.Queue.GetBoardAsync()).Tokens.Single(t => t.Id == third.Token.Id).RemainingMinutes;
            Assert.True(after < before, $"remaining should drop ({before} → {after})");
        });

        runner.Add(queue, "Board lists active tokens in order; Ready tokens show 0 minutes remaining", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddFundedUserAsync(5000);
            var item = await f.AddMenuItemAsync(stock: 50);
            var a = await f.PlaceAndPayAsync(user, item);
            f.Clock.Advance(TimeSpan.FromSeconds(30));
            var b = await f.PlaceAndPayAsync(user, item);
            await f.Queue.SetStatusAsync(a.Token!.Id, QueueTokenStatus.Ready);

            var board = await f.Queue.GetBoardAsync();

            Assert.Equal(a.Token.Id, board.Tokens[0].Id);
            Assert.Equal(0, board.Tokens[0].RemainingMinutes);
            Assert.Equal(b.Token!.Id, board.Tokens[1].Id);
            Assert.True(board.Tokens[1].RemainingMinutes > 0);
        });

        runner.Add(queue, "Status only moves forward, and Collected is reserved for the counter", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddFundedUserAsync();
            var paid = await f.PlaceAndPayAsync(user, await f.AddMenuItemAsync());
            var id = paid.Token!.Id;

            await f.Queue.SetStatusAsync(id, QueueTokenStatus.Preparing);
            await f.Queue.SetStatusAsync(id, QueueTokenStatus.Ready);
            Assert.Equal(ErrorKind.Conflict, (await Assert.ThrowsAsync<QueueException>(() => f.Queue.SetStatusAsync(id, QueueTokenStatus.Preparing))).Kind);
            Assert.Equal(ErrorKind.Invalid, (await Assert.ThrowsAsync<QueueException>(() => f.Queue.SetStatusAsync(id, QueueTokenStatus.Collected))).Kind);
        });
    }
}
