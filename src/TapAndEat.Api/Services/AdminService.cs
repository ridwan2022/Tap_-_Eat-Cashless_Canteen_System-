using Microsoft.AspNetCore.Identity;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;

namespace TapAndEat.Api.Services;

/// <summary>
/// Tasks 2.2, 2.3 — admin-only user/role management and menu
/// publishing/override CRUD. Every public method here is only reachable
/// through AdminController, which is guarded by [Authorize(Roles = "Admin")]
/// (Task 2.2 acceptance criteria: "only admin-role tokens can call these
/// endpoints").
/// </summary>
public class AdminService : IAdminService
{
    private readonly IUserRepository _users;
    private readonly IMenuRepository _menu;
    private readonly ITokenService _tokenService;
    private readonly IPasswordHasher<User> _passwordHasher;

    public AdminService(
        IUserRepository users,
        IMenuRepository menu,
        ITokenService tokenService,
        IPasswordHasher<User> passwordHasher)
    {
        _users = users;
        _menu = menu;
        _tokenService = tokenService;
        _passwordHasher = passwordHasher;
    }

    public async Task<IReadOnlyList<UserSummary>> GetUsersAsync()
    {
        var users = await _users.GetAllAsync();
        return users.Select(ToSummary).ToList();
    }

    public async Task<UserSummary> CreateUserAsync(CreateUserByAdminRequest request)
    {
        if (await _users.EmailExistsAsync(request.Email))
        {
            throw new AdminException("An account with this email already exists.");
        }

        var role = ParseRole(request.Role);
        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            Role = role
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
        await _users.AddAsync(user);
        return ToSummary(user);
    }

    public async Task<UserSummary> AssignRoleAsync(Guid userId, string role)
    {
        var user = await _users.GetByIdAsync(userId)
            ?? throw new AdminException("User not found.");

        user.Role = ParseRole(role);
        user.SecurityStamp++; // force re-login so the new role takes effect immediately
        await _users.UpdateAsync(user);
        _tokenService.RevokeAllForUser(user.Id);
        return ToSummary(user);
    }

    public async Task<UserSummary> SetActiveAsync(Guid userId, bool isActive)
    {
        var user = await _users.GetByIdAsync(userId)
            ?? throw new AdminException("User not found.");

        user.IsActive = isActive;
        await _users.UpdateAsync(user);
        if (!isActive)
        {
            _tokenService.RevokeAllForUser(user.Id);
        }
        return ToSummary(user);
    }

    public async Task<IReadOnlyList<MenuItemDto>> GetAllMenuItemsAsync()
    {
        var items = await _menu.GetAllAsync();
        return items.Select(MenuMapper.ToDto).ToList();
    }

    public async Task<MenuItemDto> CreateMenuItemAsync(CreateMenuItemRequest request)
    {
        var item = new MenuItem
        {
            Name = request.Name.Trim(),
            Description = request.Description ?? string.Empty,
            Price = request.Price,
            Category = string.IsNullOrWhiteSpace(request.Category) ? "General" : request.Category.Trim(),
            DietaryTags = NormalizeTags(request.DietaryTags),
            StockCount = request.StockCount,
            LowStockThreshold = request.LowStockThreshold,
            PrepTimeMinutes = request.PrepTimeMinutes ?? 5,
            IsPublished = false
        };
        await _menu.AddAsync(item);
        return MenuMapper.ToDto(item);
    }

    public async Task<MenuItemDto> UpdateMenuItemAsync(Guid id, UpdateMenuItemRequest request)
    {
        var item = await _menu.GetByIdAsync(id) ?? throw new AdminException("Menu item not found.");

        item.Name = request.Name.Trim();
        item.Description = request.Description ?? string.Empty;
        item.Price = request.Price;
        item.Category = string.IsNullOrWhiteSpace(request.Category) ? "General" : request.Category.Trim();
        item.DietaryTags = NormalizeTags(request.DietaryTags);
        item.StockCount = request.StockCount;
        item.LowStockThreshold = request.LowStockThreshold;
        if (request.PrepTimeMinutes is { } prep) item.PrepTimeMinutes = prep;

        await _menu.UpdateAsync(item);
        return MenuMapper.ToDto(item);
    }

    public async Task<MenuItemDto> SetPublishedAsync(Guid id, bool isPublished)
    {
        var item = await _menu.GetByIdAsync(id) ?? throw new AdminException("Menu item not found.");
        item.IsPublished = isPublished;
        await _menu.UpdateAsync(item);
        return MenuMapper.ToDto(item);
    }

    public async Task<MenuItemDto> ApplyOverrideAsync(Guid id, OverrideMenuItemRequest request)
    {
        // Task 2.3/2.4: a same-day override replaces the standard item's
        // details for today without deleting the underlying catalog entry.
        var item = await _menu.GetByIdAsync(id) ?? throw new AdminException("Menu item not found.");

        item.Name = request.Name.Trim();
        item.Description = request.Description ?? string.Empty;
        item.Price = request.Price;
        item.Category = string.IsNullOrWhiteSpace(request.Category) ? item.Category : request.Category.Trim();
        item.DietaryTags = NormalizeTags(request.DietaryTags);
        item.StockCount = request.StockCount;
        if (request.PrepTimeMinutes is { } overridePrep) item.PrepTimeMinutes = overridePrep;
        item.IsOverride = true;
        item.IsPublished = true;

        await _menu.UpdateAsync(item);
        return MenuMapper.ToDto(item);
    }

    public async Task DeleteMenuItemAsync(Guid id)
    {
        var deleted = await _menu.DeleteAsync(id);
        if (!deleted)
        {
            throw new AdminException("Menu item not found.");
        }
    }

    private static UserRole ParseRole(string role)
    {
        if (!Enum.TryParse<UserRole>(role, ignoreCase: true, out var parsed))
        {
            throw new AdminException(
                $"'{role}' is not a valid role. Valid roles: {string.Join(", ", Enum.GetNames<UserRole>())}.");
        }
        return parsed;
    }

    private static List<string> NormalizeTags(List<string>? tags) =>
        (tags ?? new List<string>())
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static UserSummary ToSummary(User user) =>
        new(user.Id, user.FullName, user.Email, user.Role.ToString(), user.IsActive);
}
