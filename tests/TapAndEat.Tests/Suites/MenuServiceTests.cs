using TapAndEat.Api.Models;
using TapAndEat.Api.Services;
using TapAndEat.Tests.Framework;

namespace TapAndEat.Tests.Suites;

/// <summary>Task 3.5 — unit tests for menu browsing, tag filtering, and stock updates.</summary>
public static class MenuServiceTests
{
    public static void Register(TestRunner runner)
    {
        const string suite = "MenuService (Task 3.5)";

        runner.Add(suite, "Published menu excludes unpublished (draft) items", async () =>
        {
            var fixture = new ServiceFixture();
            await fixture.Menu.AddAsync(new MenuItem { Name = "Published Item", IsPublished = true, StockCount = 5 });
            await fixture.Menu.AddAsync(new MenuItem { Name = "Draft Item", IsPublished = false, StockCount = 5 });
            var menuService = fixture.BuildMenuService();

            var result = await menuService.GetPublishedMenuAsync(tag: null);

            Assert.Equal(1, result.Count);
            Assert.Equal("Published Item", result[0].Name);
        });

        runner.Add(suite, "Tag filter only returns items carrying that dietary tag (case-insensitive)", async () =>
        {
            var fixture = new ServiceFixture();
            await fixture.Menu.AddAsync(new MenuItem
            {
                Name = "Veg Curry", IsPublished = true, StockCount = 5, DietaryTags = new List<string> { "Vegan" }
            });
            await fixture.Menu.AddAsync(new MenuItem
            {
                Name = "Chicken Curry", IsPublished = true, StockCount = 5, DietaryTags = new List<string> { "Halal" }
            });
            var menuService = fixture.BuildMenuService();

            var result = await menuService.GetPublishedMenuAsync(tag: "vegan");

            Assert.Equal(1, result.Count);
            Assert.Equal("Veg Curry", result[0].Name);
        });

        runner.Add(suite, "GetById returns null for an unpublished item (not just anyone's to see)", async () =>
        {
            var fixture = new ServiceFixture();
            var item = new MenuItem { Name = "Hidden", IsPublished = false, StockCount = 5 };
            await fixture.Menu.AddAsync(item);
            var menuService = fixture.BuildMenuService();

            var result = await menuService.GetByIdAsync(item.Id);

            Assert.Null(result);
        });

        runner.Add(suite, "SetStock updates the stock count and recomputes stock status", async () =>
        {
            var fixture = new ServiceFixture();
            var item = new MenuItem { Name = "Rice", IsPublished = true, StockCount = 50, LowStockThreshold = 5 };
            await fixture.Menu.AddAsync(item);
            var menuService = fixture.BuildMenuService();

            var result = await menuService.SetStockAsync(item.Id, 3);

            Assert.Equal(3, result.StockCount);
            Assert.Equal(StockStatus.Low.ToString(), result.StockStatus);
        });

        runner.Add(suite, "SetStock to zero reports Out of stock", async () =>
        {
            var fixture = new ServiceFixture();
            var item = new MenuItem { Name = "Soup", IsPublished = true, StockCount = 10 };
            await fixture.Menu.AddAsync(item);
            var menuService = fixture.BuildMenuService();

            var result = await menuService.SetStockAsync(item.Id, 0);

            Assert.Equal(StockStatus.Out.ToString(), result.StockStatus);
        });

        runner.Add(suite, "SetStock rejects a negative count", async () =>
        {
            var fixture = new ServiceFixture();
            var item = new MenuItem { Name = "Salad", IsPublished = true, StockCount = 10 };
            await fixture.Menu.AddAsync(item);
            var menuService = fixture.BuildMenuService();

            await Assert.ThrowsAsync<MenuException>(() => menuService.SetStockAsync(item.Id, -1));
        });

        runner.Add(suite, "SetStock on a nonexistent item throws", async () =>
        {
            var fixture = new ServiceFixture();
            var menuService = fixture.BuildMenuService();

            await Assert.ThrowsAsync<MenuException>(() => menuService.SetStockAsync(Guid.NewGuid(), 5));
        });
    }
}
