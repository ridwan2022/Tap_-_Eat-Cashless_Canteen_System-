using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Services;
using TapAndEat.Tests.Framework;

namespace TapAndEat.Tests.Suites;

/// <summary>Sprint 3 — Tasks 8.1, 8.2, 8.6: queue position accuracy and real-time behavior.</summary>
public static class QueuePositionTests
{
    public static void Register(TestRunner runner)
    {
        const string queue = "QueuePositionService (Tasks 8.1, 8.2, 8.6)";

        runner.Add(queue, "First paid order is position 1 of 1", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddFundedUserAsync();
            var order = await f.PlaceAndPayAsync(user, await f.AddMenuItemAsync());

            var pos = await f.QueuePosition.GetMyPositionAsync(order.Id, user.Id);

            Assert.Equal(1, pos.Position);
            Assert.Equal(1, pos.TotalActive);
            Assert.Equal(order.Token!.Label, pos.Label);
        });

        runner.Add(queue, "Position reflects real queue order and drops as orders ahead become Ready", async () =>
        {
            var f = new ServiceFixture { Stations = 1 };
            var user = await f.AddFundedUserAsync(5000);
            var item = await f.AddMenuItemAsync(stock: 50);
            var first = await f.PlaceAndPayAsync(user, item);
            f.Clock.Advance(TimeSpan.FromSeconds(10));
            var second = await f.PlaceAndPayAsync(user, item);
            f.Clock.Advance(TimeSpan.FromSeconds(10));
            var third = await f.PlaceAndPayAsync(user, item);

            Assert.Equal(1, (await f.QueuePosition.GetMyPositionAsync(first.Id, user.Id)).Position);
            Assert.Equal(2, (await f.QueuePosition.GetMyPositionAsync(second.Id, user.Id)).Position);
            Assert.Equal(3, (await f.QueuePosition.GetMyPositionAsync(third.Id, user.Id)).Position);

            await f.Queue.SetStatusAsync(first.Token!.Id, QueueTokenStatus.Ready);
            Assert.Equal(0, (await f.QueuePosition.GetMyPositionAsync(first.Id, user.Id)).Position);
            Assert.Equal(1, (await f.QueuePosition.GetMyPositionAsync(second.Id, user.Id)).Position); // moved up
            Assert.Equal(2, (await f.QueuePosition.GetMyPositionAsync(third.Id, user.Id)).Position);
        });

        runner.Add(queue, "An unpaid order has no position yet, and nobody else can read your position", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddFundedUserAsync();
            var other = await f.AddUserAsync();
            var unpaid = await f.PlaceOrderAsync(user, await f.AddMenuItemAsync());

            var ex = await Assert.ThrowsAsync<OrderException>(() => f.QueuePosition.GetMyPositionAsync(unpaid.Id, user.Id));
            Assert.Equal(ErrorKind.Conflict, ex.Kind);

            var paid = await f.PlaceAndPayAsync(user, await f.AddMenuItemAsync());
            await Assert.ThrowsAsync<OrderException>(() => f.QueuePosition.GetMyPositionAsync(paid.Id, other.Id));
        });

        runner.Add(queue, "The public board carries only token number and status (no names, no items)", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddFundedUserAsync();
            await f.PlaceAndPayAsync(user, await f.AddMenuItemAsync());

            var board = await f.QueuePosition.GetBoardAsync();

            Assert.Equal(1, board.Tokens.Count);
            Assert.Equal("T-001", board.Tokens[0].Label);
            Assert.Equal("Queued", board.Tokens[0].Status);
        });
    }
}
