using TapAndEat.Api.DTOs;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Services;

internal static class MenuMapper
{
    public static MenuItemDto ToDto(MenuItem item) => new(
        item.Id,
        item.Name,
        item.Description,
        item.Price,
        item.Category,
        item.DietaryTags,
        item.StockCount,
        item.GetStockStatus().ToString(),
        item.IsPublished,
        item.IsOverride,
        item.PrepTimeMinutes);
}
