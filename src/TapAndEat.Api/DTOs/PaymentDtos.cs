using System.ComponentModel.DataAnnotations;

namespace TapAndEat.Api.DTOs;

/// <summary>Method is "bKash", "Nagad" or "Wallet".</summary>
public record PayOrderRequest(Guid OrderId, [Required] string Method);

public record TopUpRequest(decimal Amount, [Required] string Method);

public record PaymentDto(
    Guid Id,
    string Purpose,
    Guid? OrderId,
    decimal Amount,
    string Method,
    string Status,
    string? RedirectUrl,
    string? FailureReason,
    string? GatewayTransactionId,
    bool RefundedToWallet,
    string? PaymentToken,
    QueueTokenDto? Token,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);
