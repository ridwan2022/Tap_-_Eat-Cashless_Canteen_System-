using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public interface IPasswordResetRepository
{
    Task<PasswordResetToken> AddAsync(PasswordResetToken token);
    Task<PasswordResetToken?> GetLatestValidForUserAsync(Guid userId, string tokenHash);
    Task MarkUsedAsync(Guid id);
}
