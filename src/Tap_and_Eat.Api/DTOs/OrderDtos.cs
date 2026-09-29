using System.ComponentModel.DataAnnotations;

namespace TapAndEat.Api.DTOs;

public record OrderItemRequest(Guid MenuItemId, [Range(1, 20)] int Quantity);

public record CreateOrderRequest([Required, MinLength(1)] List<OrderItemRequest> Items);

public record OrderLineDto(Guid MenuItemId, string Name, decimal UnitPrice, int Quantity, decimal LineTotal);

public record OrderDto(
    Guid Id,
    decimal Total,
    string Status,
    List<OrderLineDto> Lines,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? PaidAtUtc,
    string? PaymentToken,
    QueueTokenDto? Token);
