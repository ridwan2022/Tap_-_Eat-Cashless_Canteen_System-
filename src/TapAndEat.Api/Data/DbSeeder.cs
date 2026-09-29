using Microsoft.AspNetCore.Identity;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;

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
                    IsPublished = false
                }
            };

            foreach (var item in sample)
            {
                await menu.AddAsync(item);
            }
        }
    }
}
