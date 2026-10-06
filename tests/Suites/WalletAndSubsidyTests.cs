using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Payments;
using TapAndEat.Api.Services;
using TapAndEat.Tests.Framework;

namespace TapAndEat.Tests.Suites;

/// <summary>Tasks 5.1–5.6 — wallet, top-up, RFID card linking, subsidy scheduler.</summary>
public static class WalletAndSubsidyTests
{
    public static void Register(TestRunner runner)
    {
        const string wallet = "WalletService (Tasks 5.1-5.6)";
        const string subsidy = "SubsidyService (Task 5.4)";

        runner.Add(wallet, "A new user gets an empty wallet on first use", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var w = await f.WalletService.GetWalletAsync(user.Id);
            Assert.Equal(0m, w.Balance);
            Assert.Null(w.RfidCardUid);
        });

        runner.Add(wallet, "Credit adds to the balance and records a ledger entry", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();

            await f.WalletService.CreditAsync(user.Id, 250.50m, WalletTransactionType.TopUp, "top-up", "k1");

            Assert.Equal(250.50m, (await f.WalletService.GetWalletAsync(user.Id)).Balance);
            var tx = (await f.WalletService.GetTransactionsAsync(user.Id)).Single();
            Assert.Equal(250.50m, tx.Amount);
            Assert.Equal(250.50m, tx.BalanceAfter);
        });

        runner.Add(wallet, "Zero, negative and 3-decimal amounts are rejected", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            foreach (var bad in new[] { 0m, -5m, 10.999m })
            {
                await Assert.ThrowsAsync<WalletException>(() =>
                    f.WalletService.CreditAsync(user.Id, bad, WalletTransactionType.TopUp, "x", $"k{bad}"));
            }
            Assert.Equal(0m, (await f.WalletService.GetWalletAsync(user.Id)).Balance);
        });

        runner.Add(wallet, "Same idempotency key posts once", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var a = await f.WalletService.CreditAsync(user.Id, 100, WalletTransactionType.TopUp, "x", "dup");
            var b = await f.WalletService.CreditAsync(user.Id, 100, WalletTransactionType.TopUp, "x", "dup");

            Assert.True(a.Created);
            Assert.False(b.Created);
            Assert.Equal(100m, (await f.WalletService.GetWalletAsync(user.Id)).Balance);
        });

        runner.Add(wallet, "Debit can't overdraw and leaves the balance untouched", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddFundedUserAsync(40, null);
            var ex = await Assert.ThrowsAsync<WalletException>(() =>
                f.WalletService.DebitAsync(user.Id, 41, WalletTransactionType.Purchase, "x", "d1"));
            Assert.True(ex.Message.Contains("Insufficient"));
            Assert.Equal(40m, (await f.WalletService.GetWalletAsync(user.Id)).Balance);
        });

        runner.Add(wallet, "Concurrent debits never overdraw", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddFundedUserAsync(100, null);
            var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(async i =>
            {
                try { await f.WalletService.DebitAsync(user.Id, 30, WalletTransactionType.Purchase, "x", $"c{i}"); return true; }
                catch (WalletException) { return false; }
            }));
            Assert.Equal(3, results.Count(r => r));
            Assert.Equal(10m, (await f.WalletService.GetWalletAsync(user.Id)).Balance);
        });

        runner.Add(wallet, "Top-up rejects a negative amount before touching any gateway", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var ex = await Assert.ThrowsAsync<PaymentException>(() =>
                f.PaymentService.InitiateTopUpAsync(user.Id, -100m, PaymentMethod.bKash, null, "http://x"));
            Assert.True(ex.Message.Contains("greater than zero"));
            Assert.Equal(0, f.Gateway.InitCalls);
            await Assert.ThrowsAsync<PaymentException>(() => f.PaymentService.InitiateTopUpAsync(user.Id, 60_000m, PaymentMethod.bKash, null, "http://x"));
            await Assert.ThrowsAsync<PaymentException>(() => f.PaymentService.InitiateTopUpAsync(user.Id, 100m, PaymentMethod.Wallet, null, "http://x"));
        });

        runner.Add(wallet, "Top-up credits the wallet only after the gateway confirms — and only once", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var payment = await f.PaymentService.InitiateTopUpAsync(user.Id, 300m, PaymentMethod.bKash, "t1", "http://x");
            Assert.Equal(0m, (await f.WalletService.GetWalletAsync(user.Id)).Balance);

            f.Gateway.VerifyResult = GatewayVerifyResult.Success("TRX", 300m);
            var empty = new Dictionary<string, string>();
            await f.PaymentService.HandleCallbackAsync(PaymentMethod.bKash, payment.Id, empty);
            await f.PaymentService.HandleCallbackAsync(PaymentMethod.bKash, payment.Id, empty);

            Assert.Equal(300m, (await f.WalletService.GetWalletAsync(user.Id)).Balance);
            Assert.Equal(1, (await f.WalletService.GetTransactionsAsync(user.Id)).Count);
        });

        runner.Add(wallet, "A failed top-up leaves the balance alone", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var payment = await f.PaymentService.InitiateTopUpAsync(user.Id, 300m, PaymentMethod.Nagad, null, "http://x");
            f.Gateway.VerifyResult = GatewayVerifyResult.Failed("Declined");
            var result = await f.PaymentService.HandleCallbackAsync(PaymentMethod.Nagad, payment.Id, new Dictionary<string, string>());
            Assert.Equal("Failed", result.Status);
            Assert.Equal(0m, (await f.WalletService.GetWalletAsync(user.Id)).Balance);
        });

        // ------------------------------------------------ card linking (5.3)

        runner.Add(wallet, "Card UIDs are normalised (case, spaces, colons) when linking", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var w = await f.WalletService.LinkCardAsync(user.Id, "04:a1 b2-c3");
            Assert.Equal("04A1B2C3", w.RfidCardUid);
            Assert.Equal(user.Id, await f.WalletService.FindUserByCardAsync("04 A1 B2 C3"));
        });

        runner.Add(wallet, "A card already linked to another wallet is rejected with a clear error", async () =>
        {
            var f = new ServiceFixture();
            var first = await f.AddUserAsync();
            var second = await f.AddUserAsync();
            await f.WalletService.LinkCardAsync(first.Id, "AABBCCDD");

            var ex = await Assert.ThrowsAsync<WalletException>(() => f.WalletService.LinkCardAsync(second.Id, "aabbccdd"));

            Assert.Equal(ErrorKind.Conflict, ex.Kind);
            Assert.True(ex.Message.Contains("already linked to another wallet"));
            Assert.Null((await f.WalletService.GetWalletAsync(second.Id)).RfidCardUid);
        });

        runner.Add(wallet, "Re-linking the same card to the same wallet is a no-op; a second card needs an unlink first", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            await f.WalletService.LinkCardAsync(user.Id, "11223344");
            await f.WalletService.LinkCardAsync(user.Id, "11223344");
            await Assert.ThrowsAsync<WalletException>(() => f.WalletService.LinkCardAsync(user.Id, "55667788"));

            await f.WalletService.UnlinkCardAsync(user.Id);
            await f.WalletService.LinkCardAsync(user.Id, "55667788");
            Assert.Null(await f.WalletService.FindUserByCardAsync("11223344")); // old card is free again
        });

        runner.Add(wallet, "Malformed card IDs and unknown users are rejected", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            await Assert.ThrowsAsync<WalletException>(() => f.WalletService.LinkCardAsync(user.Id, "not-hex!"));
            await Assert.ThrowsAsync<WalletException>(() => f.WalletService.LinkCardAsync(user.Id, "AB"));
            await Assert.ThrowsAsync<WalletException>(() => f.WalletService.LinkCardAsync(Guid.NewGuid(), "AABBCCDD"));
        });

        // ------------------------------------------------ subsidy (5.4)

        runner.Add(subsidy, "A due schedule credits the wallet with a SubsidyCredit entry", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            await f.SubsidyService.SetScheduleAsync(user.Id, 100m, SubsidyFrequency.Daily, true);

            var run = await f.SubsidyService.RunDueCreditsAsync();

            Assert.Equal(1, run.Credited);
            Assert.Equal(100m, (await f.WalletService.GetWalletAsync(user.Id)).Balance);
            Assert.Equal("SubsidyCredit", (await f.WalletService.GetTransactionsAsync(user.Id)).Single().Type);
        });

        runner.Add(subsidy, "Running the job again in the same period never double-credits", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            await f.SubsidyService.SetScheduleAsync(user.Id, 100m, SubsidyFrequency.Daily, true);

            await f.SubsidyService.RunDueCreditsAsync();
            f.Clock.Advance(TimeSpan.FromHours(3));
            var again = await f.SubsidyService.RunDueCreditsAsync();

            Assert.Equal(0, again.Credited);
            Assert.Equal(100m, (await f.WalletService.GetWalletAsync(user.Id)).Balance);
        });

        runner.Add(subsidy, "Overlapping runs (and a lost schedule marker) still credit once", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            await f.SubsidyService.SetScheduleAsync(user.Id, 100m, SubsidyFrequency.Daily, true);

            await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => f.SubsidyService.RunDueCreditsAsync()));
            // Simulate a crash before the marker was saved: only the ledger key stands between us and a double credit.
            var schedule = (await f.Wallets.GetScheduleAsync(user.Id))!;
            schedule.LastCreditedPeriodKey = null;
            await f.Wallets.UpsertScheduleAsync(schedule);
            await f.SubsidyService.RunDueCreditsAsync();

            Assert.Equal(100m, (await f.WalletService.GetWalletAsync(user.Id)).Balance);
        });

        runner.Add(subsidy, "Daily credits again the next day; weekly and monthly wait for their period", async () =>
        {
            var f = new ServiceFixture(); // 2026-09-28 (Monday) 12:00 Dhaka
            var daily = await f.AddUserAsync();
            var weekly = await f.AddUserAsync();
            var monthly = await f.AddUserAsync();
            await f.SubsidyService.SetScheduleAsync(daily.Id, 10m, SubsidyFrequency.Daily, true);
            await f.SubsidyService.SetScheduleAsync(weekly.Id, 20m, SubsidyFrequency.Weekly, true);
            await f.SubsidyService.SetScheduleAsync(monthly.Id, 30m, SubsidyFrequency.Monthly, true);
            await f.SubsidyService.RunDueCreditsAsync();

            f.Clock.Advance(TimeSpan.FromDays(1));   // Tue 29 Sep — same week, same month
            await f.SubsidyService.RunDueCreditsAsync();
            Assert.Equal(20m, (await f.WalletService.GetWalletAsync(daily.Id)).Balance);
            Assert.Equal(20m, (await f.WalletService.GetWalletAsync(weekly.Id)).Balance);
            Assert.Equal(30m, (await f.WalletService.GetWalletAsync(monthly.Id)).Balance);

            f.Clock.Advance(TimeSpan.FromDays(6));   // Mon 5 Oct — new week, new month
            await f.SubsidyService.RunDueCreditsAsync();
            Assert.Equal(40m, (await f.WalletService.GetWalletAsync(weekly.Id)).Balance);
            Assert.Equal(60m, (await f.WalletService.GetWalletAsync(monthly.Id)).Balance);
        });

        runner.Add(subsidy, "Inactive schedules and deactivated users are skipped", async () =>
        {
            var f = new ServiceFixture();
            var off = await f.AddUserAsync();
            var deactivated = await f.AddUserAsync();
            await f.SubsidyService.SetScheduleAsync(off.Id, 50m, SubsidyFrequency.Daily, isActive: false);
            await f.SubsidyService.SetScheduleAsync(deactivated.Id, 50m, SubsidyFrequency.Daily, true);
            deactivated.IsActive = false;
            await f.Users.UpdateAsync(deactivated);

            var run = await f.SubsidyService.RunDueCreditsAsync();

            Assert.Equal(0, run.Credited);
            Assert.Equal(0m, (await f.WalletService.GetWalletAsync(off.Id)).Balance);
            Assert.Equal(0m, (await f.WalletService.GetWalletAsync(deactivated.Id)).Balance);
        });

        runner.Add(subsidy, "Invalid schedule (zero/negative/unknown user) is rejected", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            await Assert.ThrowsAsync<WalletException>(() => f.SubsidyService.SetScheduleAsync(user.Id, 0m, SubsidyFrequency.Daily, true));
            await Assert.ThrowsAsync<WalletException>(() => f.SubsidyService.SetScheduleAsync(user.Id, -5m, SubsidyFrequency.Daily, true));
            await Assert.ThrowsAsync<WalletException>(() => f.SubsidyService.SetScheduleAsync(Guid.NewGuid(), 5m, SubsidyFrequency.Daily, true));
        });
    }
}
