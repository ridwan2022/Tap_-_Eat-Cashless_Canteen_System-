using Microsoft.AspNetCore.Identity;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;
using TapAndEat.Api.Services;

namespace TapAndEat.Api.Data;

/// <summary>
/// Sprint 1 has no persistent database, so we seed a default admin account
/// and a small sample menu on startup — otherwise there would be no way to
/// reach the Admin-only endpoints on a fresh run. Change the seeded admin
/// password before any real deployment.
/// </summary>
public static class DbSeeder
{
    public const string DefaultAdminEmail = "admin@tapandeat.local";
    public const string DefaultAdminPassword = "Admin@12345";

    // Sprint 2 demo accounts.
    public const string DemoKitchenEmail = "kitchen@tapandeat.local";
    public const string DemoKitchenPassword = "Kitchen@12345";
    public const string DemoEmployeeEmail = "employee@tapandeat.local";
    public const string DemoEmployeePassword = "Employee@12345";
    public const string DemoEmployeeCardUid = "04A1B2C3";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var users = services.GetRequiredService<IUserRepository>();
        var menu = services.GetRequiredService<IMenuRepository>();
        var passwordHasher = services.GetRequiredService<IPasswordHasher<User>>();

        if (!await users.EmailExistsAsync(DefaultAdminEmail))
        {
            var admin = new User
            {
                FullName = "System Administrator",
                Email = DefaultAdminEmail,
                Role = UserRole.Admin
            };
            admin.PasswordHash = passwordHasher.HashPassword(admin, DefaultAdminPassword);
            await users.AddAsync(admin);
        }

        await SeedDemoStaffAndEmployeeAsync(services, users, passwordHasher);

        var existingMenu = await menu.GetAllAsync();
        if (existingMenu.Count == 0)
        {
            var sample = new[]
            {
                new MenuItem
                {
                    Name = "Chicken Biryani",
                    Description = "Classic Dhaka-style chicken biryani.",
                    Price = 150m,
                    Category = "Main Course",
                    DietaryTags = new List<string> { "Halal", "Contains Dairy" },
                    StockCount = 40,
                    LowStockThreshold = 10,
                    PrepTimeMinutes = 8,
                    IsPublished = true
                },
                new MenuItem
                {
                    Name = "Vegetable Khichuri",
                    Description = "Lentil and rice khichuri with mixed vegetables.",
                    Price = 90m,
                    Category = "Main Course",
                    DietaryTags = new List<string> { "Vegetarian", "Vegan" },
                    StockCount = 25,
                    LowStockThreshold = 8,
                    PrepTimeMinutes = 6,
                    IsPublished = true
                },
                new MenuItem
                {
                    Name = "Grilled Sandwich",
                    Description = "Toasted sandwich with cheese and vegetables.",
                    Price = 70m,
                    Category = "Snacks",
                    DietaryTags = new List<string> { "Vegetarian" },
                    StockCount = 4,
                    LowStockThreshold = 5,
                    PrepTimeMinutes = 4,
                    IsPublished = true
                },
                new MenuItem
                {
                    Name = "Beef Tehari",
                    Description = "Not yet published — draft item pending review.",
                    Price = 160m,
                    Category = "Main Course",
                    DietaryTags = new List<string> { "Halal" },
                    StockCount = 0,
                    LowStockThreshold = 10,
                    PrepTimeMinutes = 8,
                    IsPublished = false
                }
            };

            foreach (var item in sample)
            {
                await menu.AddAsync(item);
            }

            await SeedIngredientsAndRecipesAsync(services, sample);
        }
    }

    /// <summary>
    /// Sprint 3 (Tasks 10.1/10.2) — a small ingredient set with recipes for
    /// the seeded menu, including one ingredient seeded already at/under its
    /// reorder threshold so the low-stock alert has something to show on a
    /// fresh run.
    /// </summary>
    private static async Task SeedIngredientsAndRecipesAsync(IServiceProvider services, MenuItem[] sample)
    {
        var inventory = services.GetRequiredService<IInventoryService>();

        var chicken = await inventory.CreateIngredientAsync(new CreateIngredientRequest("Chicken", "g", 20000, 3000));
        var rice = await inventory.CreateIngredientAsync(new CreateIngredientRequest("Rice", "g", 30000, 4000));
        var lentils = await inventory.CreateIngredientAsync(new CreateIngredientRequest("Lentils", "g", 1500, 2000)); // seeded low
        var vegetables = await inventory.CreateIngredientAsync(new CreateIngredientRequest("Mixed Vegetables", "g", 10000, 1500));
        var bread = await inventory.CreateIngredientAsync(new CreateIngredientRequest("Bread", "slices", 200, 40));
        var cheese = await inventory.CreateIngredientAsync(new CreateIngredientRequest("Cheese", "g", 4000, 500));

        var byName = sample.ToDictionary(i => i.Name);
        async Task Recipe(string itemName, Guid ingredientId, decimal perItem) =>
            await inventory.SetRecipeLineAsync(byName[itemName].Id, new SetRecipeLineRequest(ingredientId, perItem));

        await Recipe("Chicken Biryani", chicken.Id, 180);
        await Recipe("Chicken Biryani", rice.Id, 220);
        await Recipe("Vegetable Khichuri", rice.Id, 180);
        await Recipe("Vegetable Khichuri", lentils.Id, 90);
        await Recipe("Vegetable Khichuri", vegetables.Id, 120);
        await Recipe("Grilled Sandwich", bread.Id, 2);
        await Recipe("Grilled Sandwich", cheese.Id, 40);
        await Recipe("Grilled Sandwich", vegetables.Id, 60);
    }

    /// <summary>
    /// Sprint 2 — a kitchen-staff login for the kitchen board/counter, and an
    /// "employee" with a funded wallet, a linked RFID card and a daily
    /// subsidy, so every Sprint 2 screen can be tried straight away.
    /// </summary>
    private static async Task SeedDemoStaffAndEmployeeAsync(
        IServiceProvider services, IUserRepository users, IPasswordHasher<User> passwordHasher)
    {
        if (!await users.EmailExistsAsync(DemoKitchenEmail))
        {
            var kitchen = new User { FullName = "Kitchen Staff", Email = DemoKitchenEmail, Role = UserRole.KitchenStaff };
            kitchen.PasswordHash = passwordHasher.HashPassword(kitchen, DemoKitchenPassword);
            await users.AddAsync(kitchen);
        }

        if (await users.EmailExistsAsync(DemoEmployeeEmail)) return;

        var employee = new User { FullName = "Demo Employee", Email = DemoEmployeeEmail, Role = UserRole.Customer };
        employee.PasswordHash = passwordHasher.HashPassword(employee, DemoEmployeePassword);
        await users.AddAsync(employee);

        var wallets = services.GetRequiredService<IWalletService>();
        await wallets.CreditAsync(employee.Id, 500m, WalletTransactionType.TopUp, "Opening balance (demo)", $"seed:opening:{employee.Id:N}");
        await wallets.LinkCardAsync(employee.Id, DemoEmployeeCardUid);
        await services.GetRequiredService<ISubsidyService>()
            .SetScheduleAsync(employee.Id, 100m, SubsidyFrequency.Daily, isActive: true);
    }
}
