using TapAndEat.Api.DTOs;
using TapAndEat.Api.Models;
using TapAndEat.Api.Services;
using TapAndEat.Tests.Framework;

namespace TapAndEat.Tests.Suites;

/// <summary>Tasks 6.1–6.6 — RFID tap verification and meal release.</summary>
public static class CounterTests
{
    public static void Register(TestRunner runner)
    {
        const string suite = "CounterService (Tasks 6.1-6.6)";

        async Task<(ServiceFixture f, User customer, User staff, OrderDto order)> PaidReadyAsync()
        {
            var f = new ServiceFixture();
            var customer = await f.AddFundedUserAsync(1000, "04A1B2C3");
            var staff = await f.AddUserAsync("Counter", UserRole.KitchenStaff);
            var order = await f.PlaceAndPayAsync(customer, await f.AddMenuItemAsync("Biryani", 150));
            await f.Queue.SetStatusAsync(order.Token!.Id, QueueTokenStatus.Ready);
            return (f, customer, staff, order);
        }

        runner.Add(suite, "Tap for a paid, ready order → release approved; order and token become Collected", async () =>
        {
            var (f, customer, staff, order) = await PaidReadyAsync();

            var result = await f.Counter.TapAsync("04A1B2C3", staff.Id);

            Assert.Equal("Approved", result.Outcome);
            Assert.Equal(order.Token!.TokenNumber, result.TokenNumber);
            Assert.Equal("Biryani", result.Items![0].Name);
            Assert.Equal("Collected", (await f.OrderService.GetOrderAsync(order.Id, customer.Id, false)).Status);
            Assert.Equal(0, (await f.Queue.GetBoardAsync()).Tokens.Count);
        });

        runner.Add(suite, "Unknown card → Rejected/UnknownCard", async () =>
        {
            var (f, _, staff, _) = await PaidReadyAsync();
            var result = await f.Counter.TapAsync("DEADBEEF", staff.Id);
            Assert.Equal("Rejected", result.Outcome);
            Assert.Equal("UnknownCard", result.Reason);
        });

        runner.Add(suite, "Unreadable card ID → Rejected, not an exception", async () =>
        {
            var (f, _, staff, _) = await PaidReadyAsync();
            var result = await f.Counter.TapAsync("???", staff.Id);
            Assert.Equal("UnknownCard", result.Reason);
        });

        runner.Add(suite, "Unpaid order → Rejected/NoPaidOrder and nothing is released", async () =>
        {
            var f = new ServiceFixture();
            var customer = await f.AddFundedUserAsync();
            var staff = await f.AddUserAsync("Counter", UserRole.KitchenStaff);
            var order = await f.PlaceOrderAsync(customer, await f.AddMenuItemAsync()); // never paid

            var result = await f.Counter.TapAsync("04A1B2C3", staff.Id);

            Assert.Equal("Rejected", result.Outcome);
            Assert.Equal("NoPaidOrder", result.Reason);
            Assert.True(result.Message.Contains("not paid"));
            Assert.Equal("PendingPayment", (await f.OrderService.GetOrderAsync(order.Id, customer.Id, false)).Status);
        });

        runner.Add(suite, "Card with no orders at all → Rejected/NoPaidOrder", async () =>
        {
            var f = new ServiceFixture();
            await f.AddFundedUserAsync();
            var staff = await f.AddUserAsync("Counter", UserRole.KitchenStaff);
            Assert.Equal("NoPaidOrder", (await f.Counter.TapAsync("04A1B2C3", staff.Id)).Reason);
        });

        runner.Add(suite, "Paid but still cooking → Rejected/OrderNotReady with the token", async () =>
        {
            var f = new ServiceFixture();
            var customer = await f.AddFundedUserAsync();
            var staff = await f.AddUserAsync("Counter", UserRole.KitchenStaff);
            var order = await f.PlaceAndPayAsync(customer, await f.AddMenuItemAsync());
            await f.Queue.SetStatusAsync(order.Token!.Id, QueueTokenStatus.Preparing);

            var result = await f.Counter.TapAsync("04A1B2C3", staff.Id);

            Assert.Equal("OrderNotReady", result.Reason);
            Assert.Equal(order.Token.TokenNumber, result.TokenNumber);
            Assert.Equal("Paid", (await f.OrderService.GetOrderAsync(order.Id, customer.Id, false)).Status);
        });

        runner.Add(suite, "Second tap right after collection → Rejected/AlreadyCollected", async () =>
        {
            var (f, _, staff, _) = await PaidReadyAsync();
            await f.Counter.TapAsync("04A1B2C3", staff.Id);

            var again = await f.Counter.TapAsync("04A1B2C3", staff.Id);

            Assert.Equal("Rejected", again.Outcome);
            Assert.Equal("AlreadyCollected", again.Reason);
        });

        runner.Add(suite, "Simultaneous taps release the meal exactly once", async () =>
        {
            var (f, _, staff, _) = await PaidReadyAsync();
            var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => f.Counter.TapAsync("04A1B2C3", staff.Id)));
            Assert.Equal(1, results.Count(r => r.Outcome == "Approved"));
        });

        runner.Add(suite, "With two paid orders, the oldest READY one is released first", async () =>
        {
            var f = new ServiceFixture();
            var customer = await f.AddFundedUserAsync(5000);
            var staff = await f.AddUserAsync("Counter", UserRole.KitchenStaff);
            var item = await f.AddMenuItemAsync(stock: 20);
            var first = await f.PlaceAndPayAsync(customer, item);
            f.Clock.Advance(TimeSpan.FromMinutes(1));
            var second = await f.PlaceAndPayAsync(customer, item);
            await f.Queue.SetStatusAsync(second.Token!.Id, QueueTokenStatus.Ready); // only the second is ready

            var result = await f.Counter.TapAsync("04A1B2C3", staff.Id);

            Assert.Equal("Approved", result.Outcome);
            Assert.Equal(second.Token.TokenNumber, result.TokenNumber);
            Assert.Equal("Paid", (await f.OrderService.GetOrderAsync(first.Id, customer.Id, false)).Status);
        });

        runner.Add(suite, "Every tap — approved or rejected — is recorded and appears in Recent", async () =>
        {
            var (f, _, staff, _) = await PaidReadyAsync();
            await f.Counter.TapAsync("DEADBEEF", staff.Id);
            await f.Counter.TapAsync("04A1B2C3", staff.Id);

            var recent = await f.Counter.GetRecentAsync(10);

            Assert.Equal(2, recent.Count);
            Assert.True(recent.Any(t => t.Outcome == "Approved" && t.CustomerName != null));
            Assert.True(recent.Any(t => t.Reason == "UnknownCard"));
        });

        runner.Add(suite, "Taps are pushed to SSE subscribers (Task 6.4)", async () =>
        {
            var (f, _, staff, _) = await PaidReadyAsync();
            using var sub = f.Events.Subscribe();

            await f.Counter.TapAsync("04A1B2C3", staff.Id);

            var seen = new List<string>();
            while (sub.Reader.TryRead(out var evt)) seen.Add(evt.Type);
            Assert.True(seen.Contains("tap"), "expected a 'tap' event, saw: " + string.Join(",", seen));
        });

        // ------------------------------------------------ manual fallback

        runner.Add(suite, "Manual lookup by token number releases a ready order", async () =>
        {
            var (f, _, staff, order) = await PaidReadyAsync();
            var result = await f.Counter.VerifyManuallyAsync(new ManualVerifyRequest(order.Token!.TokenNumber, null), staff.Id);
            Assert.Equal("Approved", result.Outcome);
        });

        runner.Add(suite, "Manual lookup by payment token works; a forged token is rejected", async () =>
        {
            var (f, _, staff, order) = await PaidReadyAsync();

            var forged = await f.Counter.VerifyManuallyAsync(
                new ManualVerifyRequest(null, f.PaymentTokens.Issue(order.Id, Guid.NewGuid()).Replace('a', 'b')), staff.Id);
            Assert.Equal("InvalidToken", forged.Reason);

            var ok = await f.Counter.VerifyManuallyAsync(new ManualVerifyRequest(null, order.PaymentToken), staff.Id);
            Assert.Equal("Approved", ok.Outcome);
        });

        runner.Add(suite, "Manual lookup: unknown number, missing input, and repeat use are all rejected", async () =>
        {
            var (f, _, staff, order) = await PaidReadyAsync();
            Assert.Equal("InvalidToken", (await f.Counter.VerifyManuallyAsync(new ManualVerifyRequest(999, null), staff.Id)).Reason);
            Assert.Equal("InvalidToken", (await f.Counter.VerifyManuallyAsync(new ManualVerifyRequest(null, null), staff.Id)).Reason);
            await f.Counter.VerifyManuallyAsync(new ManualVerifyRequest(order.Token!.TokenNumber, null), staff.Id);
            Assert.Equal("AlreadyCollected", (await f.Counter.VerifyManuallyAsync(new ManualVerifyRequest(order.Token.TokenNumber, null), staff.Id)).Reason);
        });

        // ------------------------------------------------ tap to pay

        runner.Add(suite, "Tap-to-pay settles the holder's pending order from their wallet and issues a token", async () =>
        {
            var f = new ServiceFixture();
            var customer = await f.AddFundedUserAsync(500);
            var staff = await f.AddUserAsync("Counter", UserRole.KitchenStaff);
            var order = await f.PlaceOrderAsync(customer, await f.AddMenuItemAsync(price: 120));

            var result = await f.Counter.TapToPayAsync("04A1B2C3", null, staff.Id);

            Assert.Equal("Approved", result.Outcome);
            Assert.Equal(380m, (await f.WalletService.GetWalletAsync(customer.Id)).Balance);
            Assert.Equal("Paid", (await f.OrderService.GetOrderAsync(order.Id, customer.Id, false)).Status);
            Assert.NotNull(result.TokenNumber);
        });

        runner.Add(suite, "Tap-to-pay with too little balance is rejected and charges nothing", async () =>
        {
            var f = new ServiceFixture();
            var customer = await f.AddFundedUserAsync(50);
            var staff = await f.AddUserAsync("Counter", UserRole.KitchenStaff);
            await f.PlaceOrderAsync(customer, await f.AddMenuItemAsync(price: 120));

            var result = await f.Counter.TapToPayAsync("04A1B2C3", null, staff.Id);

            Assert.Equal("PaymentFailed", result.Reason);
            Assert.Equal(50m, (await f.WalletService.GetWalletAsync(customer.Id)).Balance);
        });

        runner.Add(suite, "Tap-to-pay with nothing to pay / unknown card is rejected", async () =>
        {
            var f = new ServiceFixture();
            await f.AddFundedUserAsync();
            var staff = await f.AddUserAsync("Counter", UserRole.KitchenStaff);
            Assert.Equal("NoPendingOrder", (await f.Counter.TapToPayAsync("04A1B2C3", null, staff.Id)).Reason);
            Assert.Equal("UnknownCard", (await f.Counter.TapToPayAsync("DEADBEEF", null, staff.Id)).Reason);
        });
    }
}
