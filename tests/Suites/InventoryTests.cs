using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Services;
using TapAndEat.Tests.Framework;

namespace TapAndEat.Tests.Suites;

/// <summary>Sprint 3 — Tasks 10.1-10.4, 10.6: inventory deduction, low-stock alerts, KDS integration.</summary>
public static class InventoryTests
{
    public static void Register(TestRunner runner)
    {
        const string inventory = "InventoryService (Tasks 10.1-10.4, 10.6)";

        runner.Add(inventory, "Placing an order deducts every recipe ingredient by quantity × portions", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var item = await f.AddMenuItemAsync(stock: 10);
            var chicken = await f.AddIngredientAsync("Chicken", stock: 1000, threshold: 100);
            var rice = await f.AddIngredientAsync("Rice", stock: 2000, threshold: 200);
            await f.SetRecipeAsync(item.Id, chicken.Id, 150);
            await f.SetRecipeAsync(item.Id, rice.Id, 200);

            var order = (await f.OrderService.CreateOrderAsync(user.Id,
                new CreateOrderRequest(new List<OrderItemRequest> { new(item.Id, 3) })));
            await f.InventoryService.DeductForOrderAsync((await f.Orders.GetByIdAsync(order.Id))!);

            var ingredients = await f.InventoryService.GetIngredientsAsync();
            Assert.Equal(1000 - 450, ingredients.Single(i => i.Name == "Chicken").StockQuantity);
            Assert.Equal(2000 - 600, ingredients.Single(i => i.Name == "Rice").StockQuantity);
        });

        runner.Add(inventory, "Deduction never drives stock below zero, and logs the clamped amount", async () =>
        {
            var f = new ServiceFixture();
            var flour = await f.AddIngredientAsync("Flour", stock: 50, threshold: 10);

            var tx = await f.Ingredients.AdjustStockAsync(flour.Id, -80, InventoryTransactionReason.OrderDeduction, Guid.NewGuid());

            Assert.Equal(-50m, tx.ChangeAmount); // clamped: only 50 was available, not the requested 80
            Assert.Equal(0m, tx.BalanceAfter);
            Assert.Equal(0m, (await f.InventoryService.GetIngredientsAsync()).Single(i => i.Name == "Flour").StockQuantity);
        });

        runner.Add(inventory, "A missing recipe doesn't block the order — deduction for that item is simply skipped", async () =>
        {
            var f = new ServiceFixture();
            var user = await f.AddUserAsync();
            var item = await f.AddMenuItemAsync(stock: 10); // no recipe configured at all
            var order = await f.OrderService.CreateOrderAsync(user.Id, new CreateOrderRequest(new List<OrderItemRequest> { new(item.Id, 2) }));

            await f.InventoryService.DeductForOrderAsync((await f.Orders.GetByIdAsync(order.Id))!); // must not throw
            Assert.Equal("PendingPayment", order.Status);
        });

        runner.Add(inventory, "Low-stock list includes exactly the ingredients at or under their threshold", async () =>
        {
            var f = new ServiceFixture();
            await f.AddIngredientAsync("Plenty", stock: 500, threshold: 50);
            await f.AddIngredientAsync("Right at threshold", stock: 50, threshold: 50);
            await f.AddIngredientAsync("Under", stock: 10, threshold: 50);

            var low = await f.InventoryService.GetLowStockAsync();

            Assert.Equal(2, low.Count);
            Assert.True(low.All(i => i.Name != "Plenty"));
        });

        runner.Add(inventory, "A duplicate ingredient name is rejected; restock and manual adjustment both post to the ledger", async () =>
        {
            var f = new ServiceFixture();
            await f.InventoryService.CreateIngredientAsync(new CreateIngredientRequest("Onion", "g", 100, 20));
            await Assert.ThrowsAsync<InventoryException>(() => f.InventoryService.CreateIngredientAsync(new CreateIngredientRequest("onion", "g", 0, 0)));

            var onion = (await f.InventoryService.GetIngredientsAsync()).Single(i => i.Name == "Onion");
            await f.InventoryService.AdjustStockAsync(onion.Id, 50); // restock
            var after = await f.InventoryService.AdjustStockAsync(onion.Id, -30); // manual correction
            Assert.Equal(120m, after.StockQuantity);
            var ledger = await f.InventoryService.GetLedgerAsync(onion.Id);
            Assert.Equal(2, ledger.Count);
            Assert.Equal("ManualRestock", ledger[1].Reason);
            Assert.Equal("ManualAdjustment", ledger[0].Reason);
        });
    }
}
