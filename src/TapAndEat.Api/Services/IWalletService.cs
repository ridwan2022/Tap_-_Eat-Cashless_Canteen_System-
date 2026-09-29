using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Services;

public class WalletException : AppException
{
    public WalletException(ErrorKind kind, string message) : base(kind, message) { }
}

/// <summary>Created = false means the idempotency key had already been used and nothing new was posted.</summary>
public record WalletPosting(WalletTransactionDto Transaction, bool Created);

public interface IWalletService
{
    /// <summary>Task 5.2 — balance (creates the wallet on first use).</summary>
    Task<WalletDto> GetWalletAsync(Guid userId);

    /// <summary>Task 5.5 — newest first.</summary>
    Task<IReadOnlyList<WalletTransactionDto>> GetTransactionsAsync(Guid userId);

    /// <summary>Adds money. Amount must be positive with at most 2 decimals. Idempotent per key.</summary>
    Task<WalletPosting> CreditAsync(Guid userId, decimal amount, WalletTransactionType type, string description, string idempotencyKey);

    /// <summary>Spends money; fails (and changes nothing) if the balance is too low. Idempotent per key.</summary>
    Task<WalletPosting> DebitAsync(Guid userId, decimal amount, WalletTransactionType type, string description, string idempotencyKey);

    /// <summary>Task 5.3 — links a card to the user's wallet; a card can belong to only one wallet.</summary>
    Task<WalletDto> LinkCardAsync(Guid userId, string cardUid);
    Task<WalletDto> UnlinkCardAsync(Guid userId);

    /// <summary>The user who owns this card, or null.</summary>
    Task<Guid?> FindUserByCardAsync(string cardUid);

    /// <summary>Admin view of every user's wallet, card and subsidy.</summary>
    Task<IReadOnlyList<AdminWalletDto>> GetAllForAdminAsync();
}
