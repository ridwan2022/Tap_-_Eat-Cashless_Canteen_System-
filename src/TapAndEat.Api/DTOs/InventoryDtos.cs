using System.ComponentModel.DataAnnotations;

namespace TapAndEat.Api.DTOs;

public record IngredientDto(Guid Id, string Name, string Unit, decimal StockQuantity, decimal ReorderThreshold, bool IsLowStock);
public record CreateIngredientRequest([Required] string Name, [Required] string Unit, decimal InitialStock = 0, decimal ReorderThreshold = 0);
public record AdjustStockRequest(decimal Amount, string? Note);
public record RecipeLineDto(Guid IngredientId, string IngredientName, string Unit, decimal QuantityPerItem);
public record SetRecipeLineRequest(Guid IngredientId, [Range(0.0001, 1000000)] decimal QuantityPerItem);
public record InventoryTransactionDto(Guid Id, decimal ChangeAmount, decimal BalanceAfter, string Reason, Guid? ReferenceOrderId, DateTimeOffset CreatedAtUtc);
