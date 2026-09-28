using TapAndEat.Api.DTOs;

namespace TapAndEat.Api.Services;

public class AdminException : Exception
{
    public AdminException(string message) : base(message) { }
}

public interface IAdminService
{
    // Task 2.2 — user & role management
    Task<IReadOnlyList<UserSummary>> GetUsersAsync();
    Task<UserSummary> CreateUserAsync(CreateUserByAdminRequest request);
    Task<UserSummary> AssignRoleAsync(Guid userId, string role);
    Task<UserSummary> SetActiveAsync(Guid userId, bool isActive);

    // Task 2.3 — menu publishing & override CRUD
    Task<IReadOnlyList<MenuItemDto>> GetAllMenuItemsAsync();
    Task<MenuItemDto> CreateMenuItemAsync(CreateMenuItemRequest request);
    Task<MenuItemDto> UpdateMenuItemAsync(Guid id, UpdateMenuItemRequest request);
    Task<MenuItemDto> SetPublishedAsync(Guid id, bool isPublished);
    Task<MenuItemDto> ApplyOverrideAsync(Guid id, OverrideMenuItemRequest request);
    Task DeleteMenuItemAsync(Guid id);
}
