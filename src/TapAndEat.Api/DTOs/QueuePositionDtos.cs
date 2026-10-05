using System.ComponentModel.DataAnnotations;

namespace TapAndEat.Api.DTOs;

/// <summary>Task 8.2/8.5 — "my queue position" response.</summary>
public record QueuePositionDto(
    Guid OrderId,
    Guid TokenId,
    int TokenNumber,
    string Label,
    string Status,
    int Position,
    int TotalActive,
    int EstimatedPrepMinutes,
    int RemainingMinutes);

/// <summary>Task 8.3 — one row of the public, PII-free token display board.</summary>
public record BoardTokenDto(int TokenNumber, string Label, string Status);
public record TokenBoardDto(DateTimeOffset GeneratedAtUtc, List<BoardTokenDto> Tokens);
