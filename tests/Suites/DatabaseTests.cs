using Microsoft.Extensions.Options;
using TapAndEat.Api.Db;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories.Sql;
using TapAndEat.Tests.Framework;

namespace TapAndEat.Tests.Suites;

/// <summary>
/// Sprint 3 — the P/Invoke SQLite layer itself (Db/Database.cs, Db/Schema.cs)
/// and a couple of its repositories, exercised directly against real files on
/// disk. This is what actually proves "saved in the database and
/// recoverable" — the service-level unit tests all run against the
/// in-memory repositories for speed, so without this suite the SQL layer
/// would only be covered by manual testing.
/// </summary>
public static class DatabaseTests
{
    private static Database Open(string? path = null)
    {
        path ??= Path.Combine(Path.GetTempPath(), $"tapandeat-dbtest-{Guid.NewGuid():N}.db");
        return new Database(Options.Create(new DatabaseOptions { Path = path }));
    }

    public static void Register(TestRunner runner)
    {
        const string suite = "SQLite data layer (Db/Database.cs, Db/Schema.cs)";

        runner.Add(suite, "Round-trips every supported type, including unicode and apostrophes", async () =>
        {
            using var db = Open();
            await Schema.EnsureCreatedAsync(db);
            var id = Guid.NewGuid();
            await db.ExecuteAsync(
                "INSERT INTO ingredients (id, name, unit, stock_quantity, reorder_threshold, created_at) VALUES (?,?,?,?,?,?)",
                id, "Chicken 🍗 (d'Anjou)", "g", 1234.56m, 10m, DateTimeOffset.UtcNow);

            var rows = await db.QueryAsync("SELECT * FROM ingredients WHERE id = ?",
                r => (Name: r.GetString("name"), Qty: r.GetDecimal("stock_quantity"), Threshold: r.GetDecimal("reorder_threshold")), id);

            Assert.Equal(1, rows.Count);
            Assert.Equal("Chicken 🍗 (d'Anjou)", rows[0].Name);
            Assert.Equal(1234.56m, rows[0].Qty);
            Assert.Equal(10m, rows[0].Threshold);
        });

        runner.Add(suite, "A transaction with a failing statement rolls back everything, including earlier statements in it", async () =>
        {
            using var db = Open();
            await Schema.EnsureCreatedAsync(db);
            var id = Guid.NewGuid();
            await db.ExecuteAsync("INSERT INTO ingredients (id, name, unit, stock_quantity, reorder_threshold, created_at) VALUES (?,?,?,?,?,?)",
                id, "Rice", "g", 100m, 10m, DateTimeOffset.UtcNow);

            await Assert.ThrowsAsync<InvalidOperationException>(() => db.ExecuteInTransactionAsync(tx =>
            {
                tx.Execute("UPDATE ingredients SET stock_quantity = ? WHERE id = ?", 999m, id);
                tx.Execute("INSERT INTO no_such_table (x) VALUES (1)"); // forces a failure after the update above
                return 0;
            }));

            var after = await db.QueryAsync("SELECT stock_quantity q FROM ingredients WHERE id = ?", r => r.GetDecimal("q"), id);
            Assert.Equal(100m, after[0]); // the update inside the failed transaction must not have stuck
        });

        runner.Add(suite, "A transaction's check-then-act is atomic under concurrent callers (no lost update)", async () =>
        {
            using var db = Open();
            await Schema.EnsureCreatedAsync(db);
            var id = Guid.NewGuid();
            await db.ExecuteAsync("INSERT INTO ingredients (id, name, unit, stock_quantity, reorder_threshold, created_at) VALUES (?,?,?,?,?,?)",
                id, "Flour", "g", 0m, 0m, DateTimeOffset.UtcNow);

            var tasks = Enumerable.Range(0, 25).Select(_ => db.ExecuteInTransactionAsync(tx =>
            {
                var current = tx.Query("SELECT stock_quantity q FROM ingredients WHERE id = ?", r => r.GetDecimal("q"), id)[0];
                tx.Execute("UPDATE ingredients SET stock_quantity = ? WHERE id = ?", current + 1, id);
                return 0;
            }));
            await Task.WhenAll(tasks);

            var final = await db.QueryAsync("SELECT stock_quantity q FROM ingredients WHERE id = ?", r => r.GetDecimal("q"), id);
            Assert.Equal(25m, final[0]); // every one of the 25 read-then-write increments must have counted
        });

        runner.Add(suite, "Schema creation is idempotent — running it twice is a no-op, not an error", async () =>
        {
            using var db = Open();
            await Schema.EnsureCreatedAsync(db);
            await Schema.EnsureCreatedAsync(db); // must not throw ("table already exists")
            await db.ExecuteAsync("INSERT INTO ingredients (id, name, unit, stock_quantity, reorder_threshold, created_at) VALUES (?,?,?,?,?,?)",
                Guid.NewGuid(), "Sugar", "g", 1m, 1m, DateTimeOffset.UtcNow);
            await Task.CompletedTask;
        });

        runner.Add(suite, "Data survives closing and reopening the same file (process-restart simulation)", async () =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"tapandeat-dbtest-{Guid.NewGuid():N}.db");
            var id = Guid.NewGuid();
            using (var db1 = Open(path))
            {
                await Schema.EnsureCreatedAsync(db1);
                var users = new SqlUserRepository(db1);
                await users.AddAsync(new User { Id = id, FullName = "Durable User", Email = "durable@example.com", Role = UserRole.Customer });
            } // db1 disposed — closes the native handle, exactly like the process exiting

            using var db2 = Open(path); // reopens the SAME file
            await Schema.EnsureCreatedAsync(db2); // idempotent — must not wipe existing data
            var usersAgain = new SqlUserRepository(db2);
            var recovered = await usersAgain.GetByIdAsync(id);

            Assert.NotNull(recovered);
            Assert.Equal("Durable User", recovered!.FullName);
            Assert.Equal("durable@example.com", recovered.Email);
        });

        runner.Add(suite, "SqlOrderRepository persists an order together with its lines, in the given order", async () =>
        {
            using var db = Open();
            await Schema.EnsureCreatedAsync(db);
            var users = new SqlUserRepository(db);
            var userId = Guid.NewGuid();
            await users.AddAsync(new User { Id = userId, FullName = "Orderer", Email = "orderer@example.com", Role = UserRole.Customer });
            var orders = new SqlOrderRepository(db);
            var order = new Order
            {
                UserId = userId,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15),
                Lines =
                {
                    new OrderLine { MenuItemId = Guid.NewGuid(), Name = "Biryani", UnitPrice = 150m, Quantity = 2, PrepTimeMinutes = 8 },
                    new OrderLine { MenuItemId = Guid.NewGuid(), Name = "Lassi", UnitPrice = 40m, Quantity = 1, PrepTimeMinutes = 2 }
                }
            };
            await orders.AddAsync(order);

            var reloaded = await orders.GetByIdAsync(order.Id);

            Assert.NotNull(reloaded);
            Assert.Equal(2, reloaded!.Lines.Count);
            Assert.Equal("Biryani", reloaded.Lines[0].Name);
            Assert.Equal(340m, reloaded.Total);
        });

        runner.Add(suite, "SqlWalletRepository.TryLinkCardAsync is atomic: a card can never end up linked to two wallets", async () =>
        {
            using var db = Open();
            await Schema.EnsureCreatedAsync(db);
            var wallets = new SqlWalletRepository(db);
            var contenders = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => wallets.GetOrCreateAsync(Guid.NewGuid())));

            var results = await Task.WhenAll(contenders.Select(w => wallets.TryLinkCardAsync(w.Id, "SHARED-CARD")));

            Assert.Equal(1, results.Count(r => r)); // exactly one of 10 DIFFERENT wallets racing for the same card should win
            var owner = await wallets.GetByCardUidAsync("SHARED-CARD");
            Assert.NotNull(owner);
            // re-linking the SAME card to its own (already-winning) wallet stays a no-op success — only a different wallet is rejected
            Assert.True(await wallets.TryLinkCardAsync(owner!.Id, "SHARED-CARD"));
        });
    }
}
