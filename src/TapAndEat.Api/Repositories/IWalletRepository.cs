using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public interface IWalletRepository
{
    Task<Wallet> GetOrCreateAsync(Guid userId);
    Task<Wallet?> GetByUserIdAsync(Guid userId);
    Task<Wallet?> GetByCardUidAsync(string cardUid);
    Task<IReadOnlyList<Wallet>> GetAllAsync();
    Task UpdateAsync(Wallet wallet);

    /// <summary>
    /// Task 5.3 — atomically links a card to a wallet. Returns false (and links
    /// nothing) if the card is already linked to a different wallet.
    /// </summary>
    Task<bool> TryLinkCardAsync(Guid walletId, string cardUid);
    Task UnlinkCardAsync(Guid walletId);

    Task<WalletTransaction?> GetTransactionByKeyAsync(string idempotencyKey);
    Task AddTransactionAsync(WalletTransaction transaction);
    Task<IReadOnlyList<WalletTransaction>> GetTransactionsAsync(Guid walletId);

    Task<SubsidySchedule?> GetScheduleAsync(Guid userId);
    Task UpsertScheduleAsync(SubsidySchedule schedule);
    Task<IReadOnlyList<SubsidySchedule>> GetActiveSchedulesAsync();
}
