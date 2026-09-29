using Microsoft.AspNetCore.Identity;
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
        }
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
