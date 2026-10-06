using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Payments;
using TapAndEat.Api.Services;
using TapAndEat.Tests.Framework;

namespace TapAndEat.Tests.Suites;

/// <summary>Tasks 4.1–4.6 — payment flow, idempotency, failure handling and the verified token.</summary>
public static class PaymentTests
{
    private const string Base = "http://localhost";
    private static readonly IReadOnlyDictionary<string, string> NoParams = new Dictionary<string, string>();

    public static void Register(TestRunner runner)
    {
        const string suite = "PaymentService (Tasks 4.1-4.6)";

        runner.Add(suite, "Gateway payment starts Initiated with a redirect URL; the order isn't paid yet", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var order = await f.PlaceOrderAsync(user, await f.AddMenuItemAsync(price: 150));

            var payment = await f.PaymentService.InitiateOrderPaymentAsync(user.Id, order.Id, PaymentMethod.bKash, "k1", Base);

            Assert.Equal("Initiated", payment.Status);
            Assert.Equal("https://gateway.test/pay", payment.RedirectUrl);
            Assert.Equal(150m, payment.Amount);
            Assert.Equal("PendingPayment", (await f.OrderService.GetOrderAsync(order.Id, user.Id, false)).Status);
        });

        runner.Add(suite, "Confirmed callback marks the order paid and issues a verified token + queue token", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var order = await f.PlaceOrderAsync(user, await f.AddMenuItemAsync(price: 150));
            var payment = await f.PaymentService.InitiateOrderPaymentAsync(user.Id, order.Id, PaymentMethod.bKash, "k1", Base);
            f.Gateway.VerifyResult = GatewayVerifyResult.Success("TRX123", 150m);

            var settled = await f.PaymentService.HandleCallbackAsync(PaymentMethod.bKash, payment.Id, NoParams);

            Assert.Equal("Succeeded", settled.Status);
            Assert.Equal("TRX123", settled.GatewayTransactionId);
            Assert.NotNull(settled.PaymentToken);
            Assert.NotNull(settled.Token);
            Assert.True(f.PaymentTokens.TryValidate(settled.PaymentToken, out var orderId, out _));
            Assert.Equal(order.Id, orderId);
        });

        runner.Add(suite, "Idempotency: same key returns the original payment and never starts a second one", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var order = await f.PlaceOrderAsync(user, await f.AddMenuItemAsync());

            var first = await f.PaymentService.InitiateOrderPaymentAsync(user.Id, order.Id, PaymentMethod.bKash, "same-key", Base);
            var second = await f.PaymentService.InitiateOrderPaymentAsync(user.Id, order.Id, PaymentMethod.bKash, "same-key", Base);

            Assert.Equal(first.Id, second.Id);
            Assert.Equal(1, f.Gateway.InitCalls);
        });

        runner.Add(suite, "Idempotency: a different key for the same in-flight method reuses the open payment", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var order = await f.PlaceOrderAsync(user, await f.AddMenuItemAsync());

            var first = await f.PaymentService.InitiateOrderPaymentAsync(user.Id, order.Id, PaymentMethod.bKash, "a", Base);
            var second = await f.PaymentService.InitiateOrderPaymentAsync(user.Id, order.Id, PaymentMethod.bKash, "b", Base);

            Assert.Equal(first.Id, second.Id);
        });

        runner.Add(suite, "A key reused for a different order is refused", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var item = await f.AddMenuItemAsync();
            var o1 = await f.PlaceOrderAsync(user, item);
            var o2 = await f.PlaceOrderAsync(user, item);
            await f.PaymentService.InitiateOrderPaymentAsync(user.Id, o1.Id, PaymentMethod.bKash, "dup", Base);

            var ex = await Assert.ThrowsAsync<PaymentException>(() =>
                f.PaymentService.InitiateOrderPaymentAsync(user.Id, o2.Id, PaymentMethod.bKash, "dup", Base));
            Assert.Equal(ErrorKind.Conflict, ex.Kind);
        });

        runner.Add(suite, "An already-paid order can't be paid again (no double charge, no second token)", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddFundedUserAsync();
            var paid = await f.PlaceAndPayAsync(user, await f.AddMenuItemAsync(price: 100));
            var balance = (await f.WalletService.GetWalletAsync(user.Id)).Balance;

            var ex = await Assert.ThrowsAsync<PaymentException>(() =>
                f.PaymentService.InitiateOrderPaymentAsync(user.Id, paid.Id, PaymentMethod.Wallet, "again", Base));

            Assert.Equal(ErrorKind.Conflict, ex.Kind);
            Assert.Equal(balance, (await f.WalletService.GetWalletAsync(user.Id)).Balance);
            Assert.Equal(1, (await f.Queue.GetBoardAsync()).Tokens.Count);
        });

        runner.Add(suite, "Concurrent double-submit charges exactly once", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddFundedUserAsync(1000);
            var order = await f.PlaceOrderAsync(user, await f.AddMenuItemAsync(price: 100));

            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
            {
                try { return (await f.PaymentService.InitiateOrderPaymentAsync(user.Id, order.Id, PaymentMethod.Wallet, $"k{i}", Base)).Status; }
                catch (PaymentException) { return "Rejected"; }
            }));

            Assert.Equal(1, results.Count(r => r == "Succeeded"));
            Assert.Equal(900m, (await f.WalletService.GetWalletAsync(user.Id)).Balance);
            Assert.Equal(1, (await f.Queue.GetBoardAsync()).Tokens.Count);
        });

        runner.Add(suite, "Replaying the callback settles once (no duplicate token or credit)", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var order = await f.PlaceOrderAsync(user, await f.AddMenuItemAsync());
            var payment = await f.PaymentService.InitiateOrderPaymentAsync(user.Id, order.Id, PaymentMethod.bKash, null, Base);
            f.Gateway.VerifyResult = GatewayVerifyResult.Success("T1", order.Total);

            await f.PaymentService.HandleCallbackAsync(PaymentMethod.bKash, payment.Id, NoParams);
            await f.PaymentService.HandleCallbackAsync(PaymentMethod.bKash, payment.Id, NoParams);

            Assert.Equal(1, (await f.Queue.GetBoardAsync()).Tokens.Count);
            Assert.Equal(1, f.Gateway.VerifyCalls); // second callback short-circuits once settled
        });

        runner.Add(suite, "Declined payment → Failed with a reason; order stays payable and can be retried", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var order = await f.PlaceOrderAsync(user, await f.AddMenuItemAsync());
            var payment = await f.PaymentService.InitiateOrderPaymentAsync(user.Id, order.Id, PaymentMethod.bKash, "try1", Base);
            f.Gateway.VerifyResult = GatewayVerifyResult.Failed("Insufficient bKash balance.");

            var failed = await f.PaymentService.HandleCallbackAsync(PaymentMethod.bKash, payment.Id, NoParams);
            Assert.Equal("Failed", failed.Status);
            Assert.Equal("Insufficient bKash balance.", failed.FailureReason);
            Assert.Equal("PendingPayment", (await f.OrderService.GetOrderAsync(order.Id, user.Id, false)).Status);

            var retry = await f.PaymentService.InitiateOrderPaymentAsync(user.Id, order.Id, PaymentMethod.bKash, "try2", Base);
            Assert.Equal("Initiated", retry.Status);
            Assert.True(retry.Id != payment.Id, "retry must be a new payment attempt");
        });

        runner.Add(suite, "Gateway that can't be reached → Failed 'nothing charged', retryable", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var order = await f.PlaceOrderAsync(user, await f.AddMenuItemAsync());
            f.Gateway.InitResult = GatewayInitResult.Fail("bKash did not respond in time. Nothing was charged — please try again.");

            var payment = await f.PaymentService.InitiateOrderPaymentAsync(user.Id, order.Id, PaymentMethod.bKash, "t1", Base);

            Assert.Equal("Failed", payment.Status);
            Assert.True(payment.FailureReason!.Contains("Nothing was charged"));
            f.Gateway.InitResult = GatewayInitResult.Ok("R2", "https://gateway.test/pay2");
            Assert.Equal("Initiated", (await f.PaymentService.InitiateOrderPaymentAsync(user.Id, order.Id, PaymentMethod.bKash, "t2", Base)).Status);
        });

        runner.Add(suite, "Timeout while confirming leaves the payment Initiated, and re-verify can finish it", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var order = await f.PlaceOrderAsync(user, await f.AddMenuItemAsync());
            var payment = await f.PaymentService.InitiateOrderPaymentAsync(user.Id, order.Id, PaymentMethod.bKash, null, Base);
            f.Gateway.VerifyResult = GatewayVerifyResult.Pending("timeout");

            var stillOpen = await f.PaymentService.HandleCallbackAsync(PaymentMethod.bKash, payment.Id, NoParams);
            Assert.Equal("Initiated", stillOpen.Status); // NOT failed — the money may have moved

            f.Gateway.VerifyResult = GatewayVerifyResult.Success("T9", order.Total);
            var done = await f.PaymentService.VerifyPaymentAsync(user.Id, payment.Id);
            Assert.Equal("Succeeded", done.Status);
        });

        runner.Add(suite, "Gateway amount that doesn't match the order is not accepted", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var order = await f.PlaceOrderAsync(user, await f.AddMenuItemAsync(price: 200));
            var payment = await f.PaymentService.InitiateOrderPaymentAsync(user.Id, order.Id, PaymentMethod.bKash, null, Base);
            f.Gateway.VerifyResult = GatewayVerifyResult.Success("T1", 20m);

            var result = await f.PaymentService.HandleCallbackAsync(PaymentMethod.bKash, payment.Id, NoParams);

            Assert.Equal("Failed", result.Status);
            Assert.Equal("PendingPayment", (await f.OrderService.GetOrderAsync(order.Id, user.Id, false)).Status);
        });

        runner.Add(suite, "Money arriving after the order expired is credited to the wallet, not lost", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var order = await f.PlaceOrderAsync(user, await f.AddMenuItemAsync(price: 130));
            var payment = await f.PaymentService.InitiateOrderPaymentAsync(user.Id, order.Id, PaymentMethod.Nagad, null, Base);
            f.Clock.Advance(TimeSpan.FromMinutes(20));
            await f.OrderService.ExpireStaleOrdersAsync();
            f.Gateway.VerifyResult = GatewayVerifyResult.Success("LATE", 130m);

            var settled = await f.PaymentService.HandleCallbackAsync(PaymentMethod.Nagad, payment.Id, NoParams);

            Assert.True(settled.RefundedToWallet);
            Assert.Null(settled.PaymentToken);
            Assert.Equal(130m, (await f.WalletService.GetWalletAsync(user.Id)).Balance);
        });

        runner.Add(suite, "Wallet payment debits the balance; insufficient balance fails cleanly", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddFundedUserAsync(50);
            var order = await f.PlaceOrderAsync(user, await f.AddMenuItemAsync(price: 100));

            var payment = await f.PaymentService.InitiateOrderPaymentAsync(user.Id, order.Id, PaymentMethod.Wallet, null, Base);

            Assert.Equal("Failed", payment.Status);
            Assert.True(payment.FailureReason!.Contains("Insufficient"));
            Assert.Equal(50m, (await f.WalletService.GetWalletAsync(user.Id)).Balance);
            Assert.Equal("PendingPayment", (await f.OrderService.GetOrderAsync(order.Id, user.Id, false)).Status);
        });

        runner.Add(suite, "Nobody else can pay for or inspect my order/payment", async () =>
        {
            var f = new ServiceFixture();
            var owner = await f.AddUserAsync();
            var intruder = await f.AddFundedUserAsync();
            var order = await f.PlaceOrderAsync(owner, await f.AddMenuItemAsync());
            var payment = await f.PaymentService.InitiateOrderPaymentAsync(owner.Id, order.Id, PaymentMethod.bKash, null, Base);

            await Assert.ThrowsAsync<PaymentException>(() => f.PaymentService.InitiateOrderPaymentAsync(intruder.Id, order.Id, PaymentMethod.Wallet, null, Base));
            await Assert.ThrowsAsync<PaymentException>(() => f.PaymentService.GetPaymentAsync(intruder.Id, payment.Id));
        });

        // ------------------------------------------------ Task 4.4 token

        runner.Add(suite, "Payment token verifies; tampering or a different secret fails", async () =>
        {
            var f = new ServiceFixture();
            var token = f.PaymentTokens.Issue(Guid.NewGuid(), Guid.NewGuid());

            Assert.True(f.PaymentTokens.TryValidate(token, out _, out _));
            Assert.False(f.PaymentTokens.TryValidate(token[..^1] + (token[^1] == 'a' ? 'b' : 'a'), out _, out _), "tampered signature");
            Assert.False(f.PaymentTokens.TryValidate(Guid.NewGuid().ToString("N") + token[32..], out _, out _), "swapped order id");
            Assert.False(f.PaymentTokens.TryValidate("garbage", out _, out _));
            var other = new PaymentTokenService(Microsoft.Extensions.Options.Options.Create(new PaymentOptions { TokenSecret = "another" }));
            Assert.False(other.TryValidate(token, out _, out _), "token from a different secret");
            await Task.CompletedTask;
        });

        // ------------------------------------------------ real adapters with a stubbed HTTP layer (Task 4.6)

        runner.Add(suite, "bKash adapter: create → execute happy path, amount parsed", async () =>
        {
            var gw = BkashWith(req => req.RequestUri!.AbsolutePath switch
            {
                var p when p.EndsWith("/token/grant") => """{"statusCode":"0000","id_token":"TOK","expires_in":3600}""",
                var p when p.EndsWith("/create") => """{"statusCode":"0000","paymentID":"PAY1","bkashURL":"https://pay.bka.sh/PAY1"}""",
                var p when p.EndsWith("/execute") => """{"statusCode":"0000","transactionStatus":"Completed","trxID":"TRX9","amount":"150.00"}""",
                _ => "{}"
            });

            var init = await gw.InitiateAsync(new GatewayInitRequest(Guid.NewGuid(), 150m, "u", Base + "/cb"));
            Assert.True(init.Success);
            Assert.Equal("PAY1", init.GatewayPaymentRef);

            var verify = await gw.VerifyAsync(new GatewayVerifyRequest("PAY1", 150m, new Dictionary<string, string> { ["status"] = "success" }));
            Assert.Equal(GatewayOutcome.Success, verify.Outcome);
            Assert.Equal("TRX9", verify.TransactionId);
            Assert.Equal(150m, verify.Amount);
        });

        runner.Add(suite, "bKash adapter: cancel/failure redirects are honoured without calling bKash", async () =>
        {
            var calls = 0;
            var gw = BkashWith(_ => { calls++; return "{}"; });
            var cancelled = await gw.VerifyAsync(new GatewayVerifyRequest("P", 1m, new Dictionary<string, string> { ["status"] = "cancel" }));
            var failed = await gw.VerifyAsync(new GatewayVerifyRequest("P", 1m, new Dictionary<string, string> { ["status"] = "failure" }));
            Assert.Equal(GatewayOutcome.Cancelled, cancelled.Outcome);
            Assert.Equal(GatewayOutcome.Failed, failed.Outcome);
            Assert.Equal(0, calls);
        });

        runner.Add(suite, "bKash adapter: a network timeout is Failed on create, Pending on execute", async () =>
        {
            var gw = new BkashGateway(new HttpClient(new StubHandler(_ => throw new TaskCanceledException("timeout"))),
                BkashOptions(), new BkashTokenCache(), Microsoft.Extensions.Logging.Abstractions.NullLogger<BkashGateway>.Instance);

            var init = await gw.InitiateAsync(new GatewayInitRequest(Guid.NewGuid(), 10m, "u", Base));
            Assert.False(init.Success);
            Assert.True(init.Error!.Contains("Nothing was charged"));

            var verify = await gw.VerifyAsync(new GatewayVerifyRequest("P", 10m, new Dictionary<string, string> { ["status"] = "success" }));
            Assert.Equal(GatewayOutcome.Pending, verify.Outcome);
        });

        runner.Add(suite, "bKash adapter: rejected create returns bKash's message", async () =>
        {
            var gw = BkashWith(req => req.RequestUri!.AbsolutePath.EndsWith("/token/grant")
                ? """{"statusCode":"0000","id_token":"T","expires_in":3600}"""
                : """{"statusCode":"2001","statusMessage":"Invalid App Key"}""");
            var init = await gw.InitiateAsync(new GatewayInitRequest(Guid.NewGuid(), 10m, "u", Base));
            Assert.False(init.Success);
            Assert.Equal("Invalid App Key", init.Error);
        });

        runner.Add(suite, "Nagad crypto: encrypt/decrypt and sign/verify round-trip", async () =>
        {
            using var merchant = System.Security.Cryptography.RSA.Create(2048);
            using var pg = System.Security.Cryptography.RSA.Create(2048);
            var merchantPriv = Convert.ToBase64String(merchant.ExportPkcs8PrivateKey());
            var merchantPub = Convert.ToBase64String(merchant.ExportSubjectPublicKeyInfo());
            var pgPub = Convert.ToBase64String(pg.ExportSubjectPublicKeyInfo());
            var pgPriv = Convert.ToBase64String(pg.ExportPkcs8PrivateKey());

            var secret = NagadCrypto.Encrypt("""{"orderId":"A1"}""", pgPub);
            Assert.Equal("""{"orderId":"A1"}""", NagadCrypto.Decrypt(secret, pgPriv));

            var sig = NagadCrypto.Sign("payload", merchantPriv);
            Assert.True(NagadCrypto.Verify("payload", sig, merchantPub));
            Assert.False(NagadCrypto.Verify("tampered", sig, merchantPub));
            await Task.CompletedTask;
        });

        runner.Add(suite, "Nagad adapter: verify maps Success / Aborted / Pending / timeout", async () =>
        {
            NagadGateway With(Func<HttpRequestMessage, string> respond) => new(
                new HttpClient(new StubHandler(respond)),
                new StaticOptions(new PaymentOptions { Nagad = { BaseUrl = "https://nagad.test/api" } }),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<NagadGateway>.Instance);
            var req = new GatewayVerifyRequest("REF", 100m, new Dictionary<string, string>());

            var ok = await With(_ => """{"status":"Success","issuerPaymentRefNo":"N77","amount":"100.00"}""").VerifyAsync(req);
            Assert.Equal(GatewayOutcome.Success, ok.Outcome);
            Assert.Equal("N77", ok.TransactionId);
            Assert.Equal(GatewayOutcome.Cancelled, (await With(_ => """{"status":"Aborted"}""").VerifyAsync(req)).Outcome);
            Assert.Equal(GatewayOutcome.Pending, (await With(_ => """{"status":"Pending"}""").VerifyAsync(req)).Outcome);
            Assert.Equal(GatewayOutcome.Failed, (await With(_ => """{"status":"Failed","message":"Declined"}""").VerifyAsync(req)).Outcome);
            var timeout = new NagadGateway(new HttpClient(new StubHandler(_ => throw new TaskCanceledException())),
                new StaticOptions(new PaymentOptions()), Microsoft.Extensions.Logging.Abstractions.NullLogger<NagadGateway>.Instance);
            Assert.Equal(GatewayOutcome.Pending, (await timeout.VerifyAsync(req)).Outcome);
        });

        runner.Add(suite, "Mock gateway: pending until decided; success/failed/cancelled map correctly; first decision wins", async () =>
        {
            var ledger = new MockGatewayLedger();
            var gw = new MockPaymentGateway(PaymentMethod.bKash, ledger);
            var init = await gw.InitiateAsync(new GatewayInitRequest(Guid.NewGuid(), 75m, "u", Base + "/cb"));
            var req = new GatewayVerifyRequest(init.GatewayPaymentRef!, 75m, new Dictionary<string, string>());

            Assert.Equal(GatewayOutcome.Pending, (await gw.VerifyAsync(req)).Outcome);
            Assert.True(ledger.Decide(init.GatewayPaymentRef!, "success"));
            Assert.False(ledger.Decide(init.GatewayPaymentRef!, "failed"));
            var result = await gw.VerifyAsync(req);
            Assert.Equal(GatewayOutcome.Success, result.Outcome);
            Assert.Equal(75m, result.Amount);
        });
    }

    // ---- helpers ----
    private static Microsoft.Extensions.Options.IOptionsMonitor<PaymentOptions> BkashOptions() =>
        new StaticOptions(new PaymentOptions { Bkash = { BaseUrl = "https://bkash.test/v1", AppKey = "k", AppSecret = "s", Username = "u", Password = "p" } });

    private static BkashGateway BkashWith(Func<HttpRequestMessage, string> respond) => new(
        new HttpClient(new StubHandler(respond)), BkashOptions(), new BkashTokenCache(),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<BkashGateway>.Instance);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string> _respond;
        public StubHandler(Func<HttpRequestMessage, string> respond) { _respond = respond; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_respond(request), System.Text.Encoding.UTF8, "application/json")
            });
    }

    private sealed class StaticOptions : Microsoft.Extensions.Options.IOptionsMonitor<PaymentOptions>
    {
        public StaticOptions(PaymentOptions value) { CurrentValue = value; }
        public PaymentOptions CurrentValue { get; }
        public PaymentOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<PaymentOptions, string?> listener) => null;
    }
}
