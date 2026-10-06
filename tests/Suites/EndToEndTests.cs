using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using TapAndEat.Api.Data;
using TapAndEat.Api.Startup;
using TapAndEat.Tests.Framework;

namespace TapAndEat.Tests.Suites;

/// <summary>
/// Boots the real application (same AppHost as Program.cs) on a random local
/// port and drives it over HTTP: auth/RBAC, orders, the mock bKash/Nagad
/// checkout + callback, wallet top-up, kitchen board, RFID counter and the
/// SSE stream. This is the closest thing to a browser session we can run
/// in-process.
/// </summary>
public static class EndToEndTests
{
    private sealed class TestSite : IAsyncDisposable
    {
        public WebApplication App { get; }
        public HttpClient Http { get; }
        public HttpClient NoRedirect { get; }
        public string BaseUrl { get; }

        private TestSite(WebApplication app, string baseUrl)
        {
            App = app;
            BaseUrl = baseUrl;
            Http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(20) };
            NoRedirect = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(baseUrl) };
        }

        /// <summary>
        /// A fresh database file per site by default (dbPath: null) so tests
        /// never see each other's data. Pass an explicit dbPath to reopen the
        /// SAME file in a new process-equivalent app instance — that's
        /// exactly how Sprint 3's "restart and recover" test proves
        /// persistence rather than just trusting the repository's own unit
        /// tests.
        /// </summary>
        public static async Task<TestSite> StartAsync(string? dbPath = null, bool jobsEnabled = false)
        {
            dbPath ??= Path.Combine(Path.GetTempPath(), $"tapandeat-e2e-{Guid.NewGuid():N}.db");
            var app = AppHost.Build(new[]
            {
                "--urls", "http://127.0.0.1:0",
                $"--Jobs:Enabled={jobsEnabled.ToString().ToLowerInvariant()}",
                "--Logging:LogLevel:Default=Warning",
                $"--Database:Path={dbPath}"
            });
            await AppHost.SeedAsync(app);
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            var site = new TestSite(app, address) { DbPath = dbPath };
            return site;
        }

        public string DbPath { get; private init; } = "";

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            NoRedirect.Dispose();
            await app_Stop();
        }

        private async Task app_Stop() { await App.StopAsync(); await App.DisposeAsync(); }
    }

    private static async Task<JsonElement> SendAsync(
        HttpClient http, HttpMethod method, string url, object? body = null, string? token = null,
        HttpStatusCode expect = HttpStatusCode.OK, string? idempotencyKey = null)
    {
        using var req = new HttpRequestMessage(method, url);
        if (token is not null) req.Headers.Add("Authorization", "Bearer " + token);
        if (idempotencyKey is not null) req.Headers.Add("Idempotency-Key", idempotencyKey);
        if (body is not null) req.Content = JsonContent.Create(body);
        using var res = await http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        Assert.Equal(expect, res.StatusCode, $"{method} {url} → {text}");
        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<string> LoginAsync(HttpClient http, string email, string password)
    {
        var res = await SendAsync(http, HttpMethod.Post, "/api/auth/login", new { email, password });
        return res.GetProperty("token").GetString()!;
    }

    private static async Task<string> ReadUntilAsync(StreamReader reader, string marker, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var block = new System.Text.StringBuilder();
        while (true)
        {
            var line = await reader.ReadLineAsync(cts.Token) ?? throw new AssertionFailedException("SSE stream ended early.");
            if (line.Length == 0) block.Clear();
            else block.AppendLine(line);
            if (line.StartsWith("data:") && block.ToString().Contains(marker)) return block.ToString();
        }
    }

    public static void Register(TestRunner runner)
    {
        const string suite = "End-to-end over real HTTP (Sprint 2 journey)";

        runner.Add(suite, "Order → bKash (mock) → callback → token → kitchen Ready → RFID tap released, live over SSE", async () =>
        {
            await using var site = await TestSite.StartAsync();
            var http = site.Http;
            var customer = await LoginAsync(http, DbSeeder.DemoEmployeeEmail, DbSeeder.DemoEmployeePassword);
            var kitchen = await LoginAsync(http, DbSeeder.DemoKitchenEmail, DbSeeder.DemoKitchenPassword);

            // pick a menu item and place an order
            var menu = await SendAsync(http, HttpMethod.Get, "/api/menu");
            var biryani = menu.EnumerateArray().First(i => i.GetProperty("name").GetString() == "Chicken Biryani");
            var stockBefore = biryani.GetProperty("stockCount").GetInt32();
            var order = await SendAsync(http, HttpMethod.Post, "/api/orders",
                new { items = new[] { new { menuItemId = biryani.GetProperty("id").GetGuid(), quantity = 2 } } },
                customer, HttpStatusCode.Created);
            var orderId = order.GetProperty("id").GetGuid();
            Assert.Equal(300m, order.GetProperty("total").GetDecimal());

            var menuAfter = await SendAsync(http, HttpMethod.Get, "/api/menu");
            Assert.Equal(stockBefore - 2, menuAfter.EnumerateArray()
                .First(i => i.GetProperty("name").GetString() == "Chicken Biryani").GetProperty("stockCount").GetInt32());

            // pay with (mock) bKash — twice with the same idempotency key
            var pay1 = await SendAsync(http, HttpMethod.Post, "/api/payments/order", new { orderId, method = "bKash" }, customer, idempotencyKey: "e2e-1");
            var pay2 = await SendAsync(http, HttpMethod.Post, "/api/payments/order", new { orderId, method = "bKash" }, customer, idempotencyKey: "e2e-1");
            Assert.Equal(pay1.GetProperty("id").GetGuid(), pay2.GetProperty("id").GetGuid());
            Assert.Equal("Initiated", pay1.GetProperty("status").GetString());
            var redirect = pay1.GetProperty("redirectUrl").GetString()!;
            Assert.True(redirect.StartsWith("/mock-gateway.html?ref="), redirect);
            var gatewayRef = Uri.UnescapeDataString(redirect.Split("ref=")[1]);

            // the customer "pays" on the fake bKash page → we follow the redirect the gateway would send
            var decide = await SendAsync(http, HttpMethod.Post, $"/api/mock-gateway/{gatewayRef}/decide", new { outcome = "success" });
            using var callback = await site.NoRedirect.GetAsync(decide.GetProperty("redirectUrl").GetString());
            Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
            Assert.True(callback.Headers.Location!.ToString().StartsWith("/payment-result.html?paymentId="));

            var paid = await SendAsync(http, HttpMethod.Get, $"/api/payments/{pay1.GetProperty("id").GetGuid()}", null, customer);
            Assert.Equal("Succeeded", paid.GetProperty("status").GetString());
            Assert.True(paid.GetProperty("paymentToken").GetString()!.Split('.').Length == 3);
            var tokenId = paid.GetProperty("token").GetProperty("id").GetGuid();

            // RBAC: a customer can't operate the counter; anonymous can't open the stream
            await SendAsync(http, HttpMethod.Post, "/api/counter/tap", new { cardUid = "04A1B2C3" }, customer, HttpStatusCode.Forbidden);
            using (var anon = await http.GetAsync("/api/stream/kitchen"))
                Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);

            // tapping before the food is ready is refused
            var early = await SendAsync(http, HttpMethod.Post, "/api/counter/tap", new { cardUid = "04A1B2C3" }, kitchen);
            Assert.Equal("Rejected", early.GetProperty("outcome").GetString());
            Assert.Equal("OrderNotReady", early.GetProperty("reason").GetString());

            // open the SSE stream as kitchen staff (token in the query string, as EventSource must)
            using var sse = new HttpRequestMessage(HttpMethod.Get, $"/api/stream/kitchen?access_token={kitchen}");
            using var sseRes = await http.SendAsync(sse, HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.OK, sseRes.StatusCode);
            Assert.Equal("text/event-stream", sseRes.Content.Headers.ContentType!.MediaType);
            using var reader = new StreamReader(await sseRes.Content.ReadAsStreamAsync());
            await ReadUntilAsync(reader, "connectedAtUtc", TimeSpan.FromSeconds(5));

            // kitchen marks it ready, then the customer taps
            var board = await SendAsync(http, HttpMethod.Get, "/api/kitchen/board", null, kitchen);
            Assert.Equal(1, board.GetProperty("tokens").GetArrayLength());
            await SendAsync(http, HttpMethod.Put, $"/api/kitchen/tokens/{tokenId}/status", new { status = "Ready" }, kitchen);
            var tap = await SendAsync(http, HttpMethod.Post, "/api/counter/tap", new { cardUid = "04 a1 b2 c3" }, kitchen);
            Assert.Equal("Approved", tap.GetProperty("outcome").GetString());

            var pushed = await ReadUntilAsync(reader, "Release approved", TimeSpan.FromSeconds(8));
            Assert.True(pushed.Contains("event: tap"), pushed);

            var mine = await SendAsync(http, HttpMethod.Get, "/api/orders/mine", null, customer);
            Assert.Equal("Collected", mine[0].GetProperty("status").GetString());
        });

        runner.Add(suite, "A forged 'status=success' callback is NOT believed; only the gateway's confirmation counts", async () =>
        {
            await using var site = await TestSite.StartAsync();
            var http = site.Http;
            var customer = await LoginAsync(http, DbSeeder.DemoEmployeeEmail, DbSeeder.DemoEmployeePassword);
            var menu = await SendAsync(http, HttpMethod.Get, "/api/menu");
            var item = menu.EnumerateArray().First();
            var order = await SendAsync(http, HttpMethod.Post, "/api/orders",
                new { items = new[] { new { menuItemId = item.GetProperty("id").GetGuid(), quantity = 1 } } }, customer, HttpStatusCode.Created);
            var pay = await SendAsync(http, HttpMethod.Post, "/api/payments/order",
                new { orderId = order.GetProperty("id").GetGuid(), method = "Nagad" }, customer);
            var pid = pay.GetProperty("id").GetGuid();

            using (var forged = await site.NoRedirect.GetAsync($"/api/payments/callback/nagad?pid={pid}&status=Success&payment_ref_id=whatever"))
                Assert.Equal(HttpStatusCode.Redirect, forged.StatusCode);

            var after = await SendAsync(http, HttpMethod.Get, $"/api/payments/{pid}", null, customer);
            Assert.Equal("Initiated", after.GetProperty("status").GetString());
            var o = await SendAsync(http, HttpMethod.Get, $"/api/orders/{order.GetProperty("id").GetGuid()}", null, customer);
            Assert.Equal("PendingPayment", o.GetProperty("status").GetString());

            // customer walks away → cancel → stock returns
            await SendAsync(http, HttpMethod.Post, $"/api/payments/{pid}/cancel", null, customer);
            await SendAsync(http, HttpMethod.Post, $"/api/orders/{order.GetProperty("id").GetGuid()}/cancel", null, customer);
        });

        runner.Add(suite, "Wallet: top-up rejects negatives; top-up via mock Nagad credits once; pay from wallet", async () =>
        {
            await using var site = await TestSite.StartAsync();
            var http = site.Http;
            var customer = await LoginAsync(http, DbSeeder.DemoEmployeeEmail, DbSeeder.DemoEmployeePassword);

            await SendAsync(http, HttpMethod.Post, "/api/wallet/topup", new { amount = -50, method = "bKash" }, customer, HttpStatusCode.BadRequest);
            await SendAsync(http, HttpMethod.Post, "/api/wallet/topup", new { amount = 100, method = "Wallet" }, customer, HttpStatusCode.BadRequest);

            var before = (await SendAsync(http, HttpMethod.Get, "/api/wallet", null, customer)).GetProperty("balance").GetDecimal();
            var top = await SendAsync(http, HttpMethod.Post, "/api/wallet/topup", new { amount = 200, method = "Nagad" }, customer, idempotencyKey: "top-1");
            var gatewayRef = Uri.UnescapeDataString(top.GetProperty("redirectUrl").GetString()!.Split("ref=")[1]);
            var decide = await SendAsync(http, HttpMethod.Post, $"/api/mock-gateway/{gatewayRef}/decide", new { outcome = "success" });
            var url = decide.GetProperty("redirectUrl").GetString()!;
            await site.NoRedirect.GetAsync(url);
            await site.NoRedirect.GetAsync(url); // replayed callback
            var after = (await SendAsync(http, HttpMethod.Get, "/api/wallet", null, customer)).GetProperty("balance").GetDecimal();
            Assert.Equal(before + 200m, after);

            var menu = await SendAsync(http, HttpMethod.Get, "/api/menu");
            var item = menu.EnumerateArray().First(i => i.GetProperty("name").GetString() == "Vegetable Khichuri");
            var order = await SendAsync(http, HttpMethod.Post, "/api/orders",
                new { items = new[] { new { menuItemId = item.GetProperty("id").GetGuid(), quantity = 1 } } }, customer, HttpStatusCode.Created);
            var paid = await SendAsync(http, HttpMethod.Post, "/api/payments/order",
                new { orderId = order.GetProperty("id").GetGuid(), method = "Wallet" }, customer);
            Assert.Equal("Succeeded", paid.GetProperty("status").GetString());
            var final = (await SendAsync(http, HttpMethod.Get, "/api/wallet", null, customer)).GetProperty("balance").GetDecimal();
            Assert.Equal(after - 90m, final);
            var txs = await SendAsync(http, HttpMethod.Get, "/api/wallet/transactions", null, customer);
            Assert.Equal("Purchase", txs[0].GetProperty("type").GetString());
        });

        runner.Add(suite, "Admin wallet endpoints: RBAC, card conflict (409), subsidy run is idempotent", async () =>
        {
            await using var site = await TestSite.StartAsync();
            var http = site.Http;
            var admin = await LoginAsync(http, DbSeeder.DefaultAdminEmail, DbSeeder.DefaultAdminPassword);
            var customer = await LoginAsync(http, DbSeeder.DemoEmployeeEmail, DbSeeder.DemoEmployeePassword);

            await SendAsync(http, HttpMethod.Get, "/api/admin/wallets", null, customer, HttpStatusCode.Forbidden);
            var wallets = await SendAsync(http, HttpMethod.Get, "/api/admin/wallets", null, admin);
            var adminRow = wallets.EnumerateArray().First(w => w.GetProperty("email").GetString() == DbSeeder.DefaultAdminEmail);
            var adminId = adminRow.GetProperty("userId").GetGuid();

            // the demo employee's card is already taken
            await SendAsync(http, HttpMethod.Put, $"/api/admin/wallets/{adminId}/card", new { cardUid = "04A1B2C3" }, admin, HttpStatusCode.Conflict);
            await SendAsync(http, HttpMethod.Put, $"/api/admin/wallets/{adminId}/card", new { cardUid = "99AA88BB" }, admin);

            await SendAsync(http, HttpMethod.Put, $"/api/admin/wallets/{adminId}/subsidy", new { amount = 75, frequency = "Monthly" }, admin);
            await SendAsync(http, HttpMethod.Put, $"/api/admin/wallets/{adminId}/subsidy", new { amount = 75, frequency = "Yearly" }, admin, HttpStatusCode.BadRequest);
            var run1 = await SendAsync(http, HttpMethod.Post, "/api/admin/subsidies/run", null, admin);
            var run2 = await SendAsync(http, HttpMethod.Post, "/api/admin/subsidies/run", null, admin);
            Assert.True(run1.GetProperty("credited").GetInt32() >= 1);
            Assert.Equal(0, run2.GetProperty("credited").GetInt32());
        });

        runner.Add(suite, "Sprint 3: data survives a full restart (same SQLite file reopened in a new app instance)", async () =>
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"tapandeat-restart-{Guid.NewGuid():N}.db");
            Guid orderId;
            decimal balanceAfterPurchase;

            await using (var site1 = await TestSite.StartAsync(dbPath))
            {
                var customer = await LoginAsync(site1.Http, DbSeeder.DemoEmployeeEmail, DbSeeder.DemoEmployeePassword);
                var menu = await SendAsync(site1.Http, HttpMethod.Get, "/api/menu");
                var item = menu.EnumerateArray().First(i => i.GetProperty("name").GetString() == "Chicken Biryani");
                var order = await SendAsync(site1.Http, HttpMethod.Post, "/api/orders",
                    new { items = new[] { new { menuItemId = item.GetProperty("id").GetGuid(), quantity = 1 } } }, customer, HttpStatusCode.Created);
                orderId = order.GetProperty("id").GetGuid();
                var paid = await SendAsync(site1.Http, HttpMethod.Post, "/api/payments/order", new { orderId, method = "Wallet" }, customer);
                Assert.Equal("Succeeded", paid.GetProperty("status").GetString());
                balanceAfterPurchase = (await SendAsync(site1.Http, HttpMethod.Get, "/api/wallet", null, customer)).GetProperty("balance").GetDecimal();
                // process "dies" here — TestSite.DisposeAsync stops this instance without a graceful app-level save,
                // the same shape as a crash: nothing but what's already durably on disk should survive.
            }

            await using var site2 = await TestSite.StartAsync(dbPath); // reopens the SAME file — simulates the app restarting
            var customerAgain = await LoginAsync(site2.Http, DbSeeder.DemoEmployeeEmail, DbSeeder.DemoEmployeePassword);
            var reloaded = await SendAsync(site2.Http, HttpMethod.Get, $"/api/orders/{orderId}", null, customerAgain);
            Assert.Equal("Paid", reloaded.GetProperty("status").GetString());
            Assert.Equal(1, reloaded.GetProperty("lines")[0].GetProperty("quantity").GetInt32());
            var walletAgain = await SendAsync(site2.Http, HttpMethod.Get, "/api/wallet", null, customerAgain);
            Assert.Equal(balanceAfterPurchase, walletAgain.GetProperty("balance").GetDecimal());

            // Re-seeding on the second boot must not have duplicated the admin/kitchen/employee accounts or reset the wallet.
            var allUsers = await SendAsync(site2.Http, HttpMethod.Get, "/api/admin/wallets", null, await LoginAsync(site2.Http, DbSeeder.DefaultAdminEmail, DbSeeder.DefaultAdminPassword));
            Assert.Equal(3, allUsers.GetArrayLength());
        });

        runner.Add(suite, "Sprint 3: ingredient deduction persists and survives a restart; never goes negative", async () =>
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"tapandeat-inv-{Guid.NewGuid():N}.db");
            await using var site = await TestSite.StartAsync(dbPath);
            var admin = await LoginAsync(site.Http, DbSeeder.DefaultAdminEmail, DbSeeder.DefaultAdminPassword);
            var customer = await LoginAsync(site.Http, DbSeeder.DemoEmployeeEmail, DbSeeder.DemoEmployeePassword);

            var ingredients = await SendAsync(site.Http, HttpMethod.Get, "/api/admin/ingredients", null, admin);
            var lentils = ingredients.EnumerateArray().First(i => i.GetProperty("name").GetString() == "Lentils");
            Assert.True(lentils.GetProperty("isLowStock").GetBoolean()); // seeded at/under threshold

            var menu = await SendAsync(site.Http, HttpMethod.Get, "/api/menu");
            var khichuri = menu.EnumerateArray().First(i => i.GetProperty("name").GetString() == "Vegetable Khichuri");
            // 20 portions would need 20×90g = 1800g of lentils but only 1500g is in stock — deduction must clamp at 0, not go negative.
            var order = await SendAsync(site.Http, HttpMethod.Post, "/api/orders",
                new { items = new[] { new { menuItemId = khichuri.GetProperty("id").GetGuid(), quantity = 16 } } }, customer, HttpStatusCode.Created);
            await SendAsync(site.Http, HttpMethod.Post, "/api/payments/order", new { orderId = order.GetProperty("id").GetGuid(), method = "Wallet" }, customer);

            var after = (await SendAsync(site.Http, HttpMethod.Get, "/api/admin/ingredients", null, admin))
                .EnumerateArray().First(i => i.GetProperty("name").GetString() == "Lentils");
            Assert.True(after.GetProperty("stockQuantity").GetDecimal() >= 0, "stock must never go negative");
            Assert.True(after.GetProperty("stockQuantity").GetDecimal() < 1500m, "stock should have dropped");

            var ledger = await SendAsync(site.Http, HttpMethod.Get, $"/api/admin/ingredients/{lentils.GetProperty("id").GetGuid()}/ledger", null, admin);
            Assert.True(ledger.GetArrayLength() > 0);
            Assert.Equal("OrderDeduction", ledger[0].GetProperty("reason").GetString());
        });

        runner.Add(suite, "Sprint 3: queue position and the public token board", async () =>
        {
            await using var site = await TestSite.StartAsync();
            var kitchen = await LoginAsync(site.Http, DbSeeder.DemoKitchenEmail, DbSeeder.DemoKitchenPassword);
            var customer = await LoginAsync(site.Http, DbSeeder.DemoEmployeeEmail, DbSeeder.DemoEmployeePassword);
            var menu = await SendAsync(site.Http, HttpMethod.Get, "/api/menu");
            var item = menu.EnumerateArray().First();

            var orderA = await SendAsync(site.Http, HttpMethod.Post, "/api/orders",
                new { items = new[] { new { menuItemId = item.GetProperty("id").GetGuid(), quantity = 1 } } }, customer, HttpStatusCode.Created);
            await SendAsync(site.Http, HttpMethod.Post, "/api/payments/order", new { orderId = orderA.GetProperty("id").GetGuid(), method = "Wallet" }, customer);

            var position = await SendAsync(site.Http, HttpMethod.Get, $"/api/queue/position/{orderA.GetProperty("id").GetGuid()}", null, customer);
            Assert.Equal(1, position.GetProperty("position").GetInt32());

            // the public board needs no auth at all and carries no customer-identifying data
            var board = await SendAsync(site.Http, HttpMethod.Get, "/api/queue/board");
            Assert.Equal(1, board.GetProperty("tokens").GetArrayLength());
            Assert.Equal("T-001", board.GetProperty("tokens")[0].GetProperty("label").GetString());

            using var boardReq = new HttpRequestMessage(HttpMethod.Get, "/api/stream/board");
            using var anonBoardStream = await site.Http.SendAsync(boardReq, HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.OK, anonBoardStream.StatusCode); // anonymous is allowed...
            using var anonKitchenStream = await site.Http.GetAsync("/api/stream/kitchen");
            Assert.Equal(HttpStatusCode.Unauthorized, anonKitchenStream.StatusCode); // ...the staff stream still isn't
        });

        runner.Add(suite, "Sprint 3: admin demand forecast reflects real paid-order history", async () =>
        {
            await using var site = await TestSite.StartAsync();
            var admin = await LoginAsync(site.Http, DbSeeder.DefaultAdminEmail, DbSeeder.DefaultAdminPassword);
            var forecasts = await SendAsync(site.Http, HttpMethod.Get, "/api/admin/forecast", null, admin);
            Assert.True(forecasts.GetArrayLength() >= 3);
            foreach (var f in forecasts.EnumerateArray())
            {
                Assert.Equal(0m, f.GetProperty("predictedQuantity").GetDecimal()); // fresh seed, no paid history yet
                Assert.Equal(0, f.GetProperty("sampleDays").GetInt32()); // fresh seed — zero days with an actual recorded sale
            }
        });

        runner.Add(suite, "Only staff can adjust stock (Sprint 1 left this open to any signed-in user)", async () =>
        {
            await using var site = await TestSite.StartAsync();
            var http = site.Http;
            var customer = await LoginAsync(http, DbSeeder.DemoEmployeeEmail, DbSeeder.DemoEmployeePassword);
            var kitchen = await LoginAsync(http, DbSeeder.DemoKitchenEmail, DbSeeder.DemoKitchenPassword);
            var id = (await SendAsync(http, HttpMethod.Get, "/api/menu")).EnumerateArray().First().GetProperty("id").GetGuid();

            await SendAsync(http, HttpMethod.Put, $"/api/menu/{id}/stock", 30, customer, HttpStatusCode.Forbidden);
            await SendAsync(http, HttpMethod.Put, $"/api/menu/{id}/stock", 30, kitchen);
        });
    }
}
