using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;

namespace TapAndEat.Api.Services;

public class OrderException : AppException
{
    public OrderException(ErrorKind kind, string message) : base(kind, message) { }
}

public interface IOrderService
{
    /// <summary>Validates the cart, reserves stock, and creates an order awaiting payment.</summary>
    Task<OrderDto> CreateOrderAsync(Guid userId, CreateOrderRequest request);
    Task<OrderDto> GetOrderAsync(Guid orderId, Guid requesterId, bool isStaff);
    Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(Guid userId);

    /// <summary>Cancels an unpaid order and gives its stock back.</summary>
    Task<OrderDto> CancelOrderAsync(Guid orderId, Guid userId);

    /// <summary>
    /// Called by the payment service once a payment is confirmed: marks the order
    /// paid, issues the verified payment token (4.4) and the queue token (7.2).
    /// Idempotent for the same payment.
    /// </summary>
    Task<OrderDto> MarkPaidAsync(Guid orderId, Guid paymentId);

    /// <summary>Called by the counter when the meal is handed over.</summary>
    Task MarkCollectedAsync(Guid orderId);

    /// <summary>Expires unpaid orders past their payment window and releases their stock. Returns how many.</summary>
    Task<int> ExpireStaleOrdersAsync();
}
