using System.ComponentModel.DataAnnotations;

namespace TapAndEat.Api.DTOs;

public record OrderLineSummary(string Name, int Quantity);

/// <summary>
/// EstimatedPrepMinutes is the estimate made when the token was issued;
/// RemainingMinutes is recomputed from the live queue every time it is read.
/// </summary>
public record QueueTokenDto(
    Guid Id,
    Guid OrderId,
    int TokenNumber,
    string Label,
    string Status,
    int EstimatedPrepMinutes,
    int RemainingMinutes,
    DateTimeOffset CreatedAtUtc,
    List<OrderLineSummary> Items);

public record KitchenBoardDto(DateTimeOffset GeneratedAtUtc, int Stations, List<QueueTokenDto> Tokens);

public record SetTokenStatusRequest([Required] string Status);
