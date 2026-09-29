using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public interface IRfidTapRepository
{
    Task AddAsync(RfidTapEvent tap);
    Task<IReadOnlyList<RfidTapEvent>> GetRecentAsync(int count);
}
