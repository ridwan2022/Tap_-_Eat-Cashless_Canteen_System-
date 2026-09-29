using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;

namespace TapAndEat.Api.Services;

/// <summary>Tasks 5.2, 5.3 — wallet balance, ledger and RFID card linking.</summary>
public class WalletService : IWalletService
{
    private readonly IWalletRepository _wallets;
    private readonly IUserRepository _users;
    private readonly TimeProvider _clock;

    public WalletService(IWalletRepository wallets, IUserRepository users, TimeProvider clock)
    {
        _wallets = wallets;
        _users = users;
        _clock = clock;
    }

    public async Task<WalletDto> GetWalletAsync(Guid userId)
    {
        var wallet = await _wallets.GetOrCreateAsync(userId);
        var schedule = await _wallets.GetScheduleAsync(userId);
        return ToDto(wallet, schedule);
    }

    public async Task<IReadOnlyList<WalletTransactionDto>> GetTransactionsAsync(Guid userId)
    {
        var wallet = await _wallets.GetOrCreateAsync(userId);
        var txs = await _wallets.GetTransactionsAsync(wallet.Id);
        return txs.Select(ToDto).ToList();
    }

    public Task<WalletPosting> CreditAsync(Guid userId, decimal amount, WalletTransactionType type, string description, string idempotencyKey) =>
        PostAsync(userId, ValidateAmount(amount), type, description, idempotencyKey);

    public Task<WalletPosting> DebitAsync(Guid userId, decimal amount, WalletTransactionType type, string description, string idempotencyKey) =>
        PostAsync(userId, -ValidateAmount(amount), type, description, idempotencyKey);

    public async Task<WalletDto> LinkCardAsync(Guid userId, string cardUid)
    {
        if (!CardUid.TryNormalize(cardUid, out var uid))
        {
            throw new WalletException(ErrorKind.Invalid, "Card ID must be 4–32 hexadecimal characters (e.g. 04A1B2C3).");
        }
        if (await _users.GetByIdAsync(userId) is null)
        {
            throw new WalletException(ErrorKind.NotFound, "User not found.");
        }

        using var _ = await KeyedLock.AcquireAsync($"wallet:{userId}");
        var wallet = await _wallets.GetOrCreateAsync(userId);

        if (string.Equals(wallet.RfidCardUid, uid, StringComparison.OrdinalIgnoreCase))
        {
            return ToDto(wallet, await _wallets.GetScheduleAsync(userId)); // already linked here — idempotent
        }
        if (wallet.RfidCardUid is not null)
        {
            throw new WalletException(ErrorKind.Conflict, "This wallet already has a card linked. Unlink it first.");
        }
        if (!await _wallets.TryLinkCardAsync(wallet.Id, uid))
        {
            throw new WalletException(ErrorKind.Conflict, "This card is already linked to another wallet.");
        }

        return ToDto(wallet, await _wallets.GetScheduleAsync(userId));
    }

    public async Task<WalletDto> UnlinkCardAsync(Guid userId)
    {
        using var _ = await KeyedLock.AcquireAsync($"wallet:{userId}");
        var wallet = await _wallets.GetOrCreateAsync(userId);
        await _wallets.UnlinkCardAsync(wallet.Id);
        return ToDto(wallet, await _wallets.GetScheduleAsync(userId));
    }

    public async Task<Guid?> FindUserByCardAsync(string cardUid)
    {
        if (!CardUid.TryNormalize(cardUid, out var uid)) return null;
        var wallet = await _wallets.GetByCardUidAsync(uid);
        return wallet?.UserId;
    }

    public async Task<IReadOnlyList<AdminWalletDto>> GetAllForAdminAsync()
    {
        var users = await _users.GetAllAsync();
        var result = new List<AdminWalletDto>();
        foreach (var user in users)
        {
            var wallet = await _wallets.GetByUserIdAsync(user.Id);
            var schedule = await _wallets.GetScheduleAsync(user.Id);
            result.Add(new AdminWalletDto(
                user.Id, user.FullName, user.Email,
                wallet?.Balance ?? 0m, wallet?.RfidCardUid,
                schedule is null ? null : ToDto(schedule)));
        }
        return result;
    }

    // ---- internals ----

    private async Task<WalletPosting> PostAsync(Guid userId, decimal signedAmount, WalletTransactionType type, string description, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new WalletException(ErrorKind.Invalid, "A wallet posting needs an idempotency key.");
        }

        using var _ = await KeyedLock.AcquireAsync($"wallet:{userId}");

        var existing = await _wallets.GetTransactionByKeyAsync(key);
        if (existing is not null)
        {
            return new WalletPosting(ToDto(existing), false);
        }

        var wallet = await _wallets.GetOrCreateAsync(userId);
        if (signedAmount < 0 && wallet.Balance + signedAmount < 0)
        {
            throw new WalletException(ErrorKind.Invalid,
                $"Insufficient wallet balance (৳{wallet.Balance:0.00} available, ৳{-signedAmount:0.00} needed).");
        }

        wallet.Balance += signedAmount;
        await _wallets.UpdateAsync(wallet);

        var tx = new WalletTransaction
        {
            WalletId = wallet.Id,
            Type = type,
            Amount = signedAmount,
            BalanceAfter = wallet.Balance,
            Description = description,
            IdempotencyKey = key,
            CreatedAtUtc = _clock.GetUtcNow()
        };
        await _wallets.AddTransactionAsync(tx);
        return new WalletPosting(ToDto(tx), true);
    }

    private static decimal ValidateAmount(decimal amount)
    {
        if (amount <= 0)
        {
            throw new WalletException(ErrorKind.Invalid, "Amount must be greater than zero.");
        }
        if (decimal.Round(amount, 2) != amount)
        {
            throw new WalletException(ErrorKind.Invalid, "Amount can have at most 2 decimal places.");
        }
        return amount;
    }

    private static WalletDto ToDto(Wallet w, SubsidySchedule? s) =>
        new(w.Id, w.Balance, w.RfidCardUid, s is null ? null : ToDto(s));

    internal static SubsidyScheduleDto ToDto(SubsidySchedule s) =>
        new(s.Amount, s.Frequency.ToString(), s.IsActive, s.LastCreditedPeriodKey);

    private static WalletTransactionDto ToDto(WalletTransaction t) =>
        new(t.Id, t.Type.ToString(), t.Amount, t.BalanceAfter, t.Description, t.CreatedAtUtc);
}
