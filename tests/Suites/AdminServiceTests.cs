using TapAndEat.Api.DTOs;
using TapAndEat.Api.Services;
using TapAndEat.Tests.Framework;

namespace TapAndEat.Tests.Suites;

/// <summary>Task 2.6 — unit tests for admin user/role management and menu publishing/override CRUD.</summary>
public static class AdminServiceTests
{
    public static void Register(TestRunner runner)
    {
        const string suite = "AdminService (Task 2.6)";

        runner.Add(suite, "CreateUser assigns the requested role", async () =>
        {
            var fixture = new ServiceFixture();
            var admin = fixture.BuildAdminService();

            var user = await admin.CreateUserAsync(new CreateUserByAdminRequest("Kitchen Staffer", "kitchen@example.com", "Password123", "KitchenStaff"));

            Assert.Equal("KitchenStaff", user.Role);
        });

        runner.Add(suite, "CreateUser with a duplicate email throws AdminException", async () =>
        {
            var fixture = new ServiceFixture();
            var admin = fixture.BuildAdminService();
            await admin.CreateUserAsync(new CreateUserByAdminRequest("First", "dupe@example.com", "Password123", "Customer"));

            await Assert.ThrowsAsync<AdminException>(() =>
                admin.CreateUserAsync(new CreateUserByAdminRequest("Second", "dupe@example.com", "Password123", "Customer")));
        });

        runner.Add(suite, "CreateUser with an invalid role name throws AdminException", async () =>
        {
            var fixture = new ServiceFixture();
            var admin = fixture.BuildAdminService();

            await Assert.ThrowsAsync<AdminException>(() =>
                admin.CreateUserAsync(new CreateUserByAdminRequest("Bad Role", "badrole@example.com", "Password123", "SuperUser")));
        });

        runner.Add(suite, "AssignRole changes the user's role and revokes their existing sessions", async () =>
        {
            var fixture = new ServiceFixture();
            var authService = fixture.BuildAuthService();
            var adminService = fixture.BuildAdminService();

            var registered = await authService.RegisterAsync(new RegisterRequest("Promote Me", "promote@example.com", "Password123"));
            var sessionBeforePromotion = registered.Token;

            var updated = await adminService.AssignRoleAsync(registered.User.Id, "Admin");

            Assert.Equal("Admin", updated.Role);
            Assert.Null(fixture.TokenService.ValidateToken(sessionBeforePromotion));
        });

        runner.Add(suite, "AssignRole with an unknown role name throws", async () =>
        {
            var fixture = new ServiceFixture();
            var authService = fixture.BuildAuthService();
            var adminService = fixture.BuildAdminService();
            var registered = await authService.RegisterAsync(new RegisterRequest("X", "rolex@example.com", "Password123"));

            await Assert.ThrowsAsync<AdminException>(() => adminService.AssignRoleAsync(registered.User.Id, "NotARole"));
        });

        runner.Add(suite, "Deactivating a user revokes their existing sessions", async () =>
        {
            var fixture = new ServiceFixture();
            var authService = fixture.BuildAuthService();
            var adminService = fixture.BuildAdminService();
            var registered = await authService.RegisterAsync(new RegisterRequest("Will Deactivate", "deactivate@example.com", "Password123"));

            await adminService.SetActiveAsync(registered.User.Id, false);

            Assert.Null(fixture.TokenService.ValidateToken(registered.Token));
            await Assert.ThrowsAsync<AuthException>(() =>
                authService.LoginAsync(new LoginRequest("deactivate@example.com", "Password123")));
        });

        runner.Add(suite, "New menu items start unpublished (draft)", async () =>
        {
            var fixture = new ServiceFixture();
            var admin = fixture.BuildAdminService();

            var item = await admin.CreateMenuItemAsync(new CreateMenuItemRequest("Fuchka", "Tangy street snack", 50, "Snacks",
                new List<string> { "Vegetarian" }, StockCount: 20));

            Assert.False(item.IsPublished, "New items should not be publicly visible until an admin publishes them.");
        });

        runner.Add(suite, "Publishing a draft item makes it visible on the public menu", async () =>
        {
            var fixture = new ServiceFixture();
            var admin = fixture.BuildAdminService();
            var menu = fixture.BuildMenuService();
            var item = await admin.CreateMenuItemAsync(new CreateMenuItemRequest("Jhalmuri", "Spicy puffed rice", 30, "Snacks", null, 15));

            await admin.SetPublishedAsync(item.Id, true);

            var publicMenu = await menu.GetPublishedMenuAsync(tag: null);
            Assert.True(publicMenu.Any(i => i.Id == item.Id), "Published item should appear in the public menu.");
        });

        runner.Add(suite, "Unpublishing an item removes it from the public menu", async () =>
        {
            var fixture = new ServiceFixture();
            var admin = fixture.BuildAdminService();
            var menu = fixture.BuildMenuService();
            var item = await admin.CreateMenuItemAsync(new CreateMenuItemRequest("Singara", "Fried pastry", 20, "Snacks", null, 15));
            await admin.SetPublishedAsync(item.Id, true);

            await admin.SetPublishedAsync(item.Id, false);

            var publicMenu = await menu.GetPublishedMenuAsync(tag: null);
            Assert.False(publicMenu.Any(i => i.Id == item.Id), "Unpublished item should not appear in the public menu.");
        });

        runner.Add(suite, "Applying an override replaces the item's details and publishes it", async () =>
        {
            var fixture = new ServiceFixture();
            var admin = fixture.BuildAdminService();
            var item = await admin.CreateMenuItemAsync(new CreateMenuItemRequest("Regular Lunch", "Standard set meal", 100, "Main Course", null, 30));

            var overridden = await admin.ApplyOverrideAsync(item.Id,
                new OverrideMenuItemRequest("Eid Special Biryani", "Special today only", 200, "Main Course",
                    new List<string> { "Halal" }, StockCount: 50));

            Assert.True(overridden.IsOverride);
            Assert.True(overridden.IsPublished);
            Assert.Equal("Eid Special Biryani", overridden.Name);
            Assert.Equal(50, overridden.StockCount);
        });

        runner.Add(suite, "Deleting a menu item removes it entirely", async () =>
        {
            var fixture = new ServiceFixture();
            var admin = fixture.BuildAdminService();
            var item = await admin.CreateMenuItemAsync(new CreateMenuItemRequest("Temporary", "", 10, "Snacks", null, 5));

            await admin.DeleteMenuItemAsync(item.Id);

            var all = await admin.GetAllMenuItemsAsync();
            Assert.False(all.Any(i => i.Id == item.Id));
        });

        runner.Add(suite, "Deleting a nonexistent menu item throws", async () =>
        {
            var fixture = new ServiceFixture();
            var admin = fixture.BuildAdminService();

            await Assert.ThrowsAsync<AdminException>(() => admin.DeleteMenuItemAsync(Guid.NewGuid()));
        });

        runner.Add(suite, "GetUsers returns every created account", async () =>
        {
            var fixture = new ServiceFixture();
            var admin = fixture.BuildAdminService();
            await admin.CreateUserAsync(new CreateUserByAdminRequest("One", "one@example.com", "Password123", "Customer"));
            await admin.CreateUserAsync(new CreateUserByAdminRequest("Two", "two@example.com", "Password123", "KitchenStaff"));

            var users = await admin.GetUsersAsync();

            Assert.Equal(2, users.Count);
        });
    }
}
