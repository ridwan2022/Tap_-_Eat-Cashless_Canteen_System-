using TapAndEat.Api.DTOs;

namespace TapAndEat.Api.Services;

public interface ICounterService
{
    /// <summary>
    /// Tasks 6.2/6.3 — an RFID tap at the meal-collection counter. Releases the
    /// card holder's oldest paid-and-ready order, or rejects with a clear
    /// reason (unknown card, unpaid, not ready, already collected).
    /// </summary>
    Task<TapResultDto> TapAsync(string cardUid, Guid staffUserId);

    /// <summary>Fallback for customers without a card: look up by today's token number or the payment token.</summary>
    Task<TapResultDto> VerifyManuallyAsync(ManualVerifyRequest request, Guid staffUserId);

    /// <summary>"Instant offline checkout": tap the card to pay the holder's pending order from their wallet.</summary>
    Task<TapResultDto> TapToPayAsync(string cardUid, Guid? orderId, Guid staffUserId);

    Task<IReadOnlyList<TapResultDto>> GetRecentAsync(int count);
}
