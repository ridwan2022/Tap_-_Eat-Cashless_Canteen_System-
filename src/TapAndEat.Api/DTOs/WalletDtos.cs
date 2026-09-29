using System.ComponentModel.DataAnnotations;

namespace TapAndEat.Api.DTOs;

public record SubsidyScheduleDto(decimal Amount, string Frequency, bool IsActive, string? LastCreditedPeriodKey);

public record WalletDto(Guid Id, decimal Balance, string? RfidCardUid, SubsidyScheduleDto? Subsidy);

public record WalletTransactionDto(
    Guid Id,
    string Type,
    decimal Amount,
    decimal BalanceAfter,
    string Description,
    DateTimeOffset CreatedAtUtc);

public record LinkCardRequest([Required] string CardUid);

public record SetSubsidyRequest(decimal Amount, [Required] string Frequency, bool IsActive = true);

public record AdminWalletDto(
    Guid UserId,
    string FullName,
    string Email,
    decimal Balance,
    string? RfidCardUid,
    SubsidyScheduleDto? Subsidy);

public record SubsidyRunResultDto(int Credited, int AlreadyCredited);
