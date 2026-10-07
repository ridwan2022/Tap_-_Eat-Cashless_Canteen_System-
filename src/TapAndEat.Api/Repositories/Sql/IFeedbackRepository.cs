using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public class FeedbackSummaryData
{
    public double AverageRating { get; set; }
    public int TotalReviews { get; set; }
    public Dictionary<int, int> StarCounts { get; set; } = new();
    public List<MenuItemFeedbackRating> ItemRatings { get; set; } = new();
    public List<OrderFeedback> RecentReviews { get; set; } = new();
}

public class MenuItemFeedbackRating
{
    public Guid MenuItemId { get; set; }
    public string MenuItemName { get; set; } = string.Empty;
    public double AverageRating { get; set; }
    public int ReviewCount { get; set; }
}

public interface IFeedbackRepository
{
    Task<OrderFeedback> UpsertAsync(OrderFeedback feedback);
    Task<OrderFeedback?> GetByOrderIdAsync(Guid orderId);
    Task<IReadOnlyList<OrderFeedback>> GetByUserIdAsync(Guid userId);
    Task<IReadOnlyList<OrderFeedback>> GetAllAsync(int limit = 100);
    Task<FeedbackSummaryData> GetSummaryAsync();
}
