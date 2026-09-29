using System.ComponentModel.DataAnnotations;

namespace TapAndEat.Api.DTOs;

public record TapRequest([Required] string CardUid);

/// <summary>Pays the card holder's pending order from their wallet. OrderId is optional (defaults to their latest pending order).</summary>
public record TapToPayRequest([Required] string CardUid, Guid? OrderId);

/// <summary>Fallback for customers without a linked card: look up by queue token number or the payment token string.</summary>
public record ManualVerifyRequest(int? TokenNumber, string? PaymentToken);

public record TapResultDto(
    Guid TapId,
    string Outcome,
    string Reason,
    string Message,
    string? CustomerName,
    int? TokenNumber,
    string? TokenLabel,
    Guid? OrderId,
    List<OrderLineSummary>? Items,
    DateTimeOffset TappedAtUtc,
    string Source);
