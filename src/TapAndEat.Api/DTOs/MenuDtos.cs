using System.ComponentModel.DataAnnotations;

namespace TapAndEat.Api.DTOs;

public record MenuItemDto(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    string Category,
    List<string> DietaryTags,
    int StockCount,
    string StockStatus,
    bool IsPublished,
    bool IsOverride);

public record CreateMenuItemRequest(
    [Required, StringLength(100)] string Name,
    string Description,
    [Range(0, 100000)] decimal Price,
    string Category,
    List<string>? DietaryTags,
    [Range(0, 100000)] int StockCount,
    int LowStockThreshold = 5);

public record UpdateMenuItemRequest(
    [Required, StringLength(100)] string Name,
    string Description,
    [Range(0, 100000)] decimal Price,
    string Category,
    List<string>? DietaryTags,
    [Range(0, 100000)] int StockCount,
    int LowStockThreshold = 5);

public record OverrideMenuItemRequest(
    [Required, StringLength(100)] string Name,
    string Description,
    [Range(0, 100000)] decimal Price,
    string Category,
    List<string>? DietaryTags,
    [Range(0, 100000)] int StockCount);

public record StockUpdateNotification(Guid MenuItemId, string Name, int StockCount, string StockStatus);
