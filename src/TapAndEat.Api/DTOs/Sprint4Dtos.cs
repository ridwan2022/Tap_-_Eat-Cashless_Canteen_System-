namespace TapAndEat.Api.DTOs;

// ---- Story 10: Notifications ----
public record NotificationDto(
    Guid Id,
    string Type,
    string Channel,
    string Title,
    string Message,
    string? ReferenceId,
    bool IsRead,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReadAtUtc);

public record PushSubscriptionRequest(
    string Endpoint,
    string? P256dh = null,
    string? Auth = null);

public record UnreadCountDto(int Count);

// ---- Story 11: Order History & Subsidy ----
public record OrderHistoryPageDto(
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    decimal TotalSpent,
    IReadOnlyList<OrderDto> Items);

public record SubsidySummaryDto(
    Guid UserId,
    string Period,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    decimal TotalCredited,
    decimal TotalSpent,
    decimal CurrentBalance,
    int SubsidizedOrdersCount,
    IReadOnlyList<WalletTransactionDto> Transactions);

public record AdminSubsidyReportDto(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    decimal TotalCompanySubsidyCredited,
    decimal TotalCompanySubsidySpent,
    int TotalActiveUsersWithSubsidy,
    IReadOnlyList<SubsidySummaryDto> UserSummaries);

// ---- Story 12: Feedback ----
public record CreateFeedbackRequest(
    Guid OrderId,
    int Rating,
    string? Comment,
    Guid? MenuItemId = null);

public record OrderFeedbackDto(
    Guid Id,
    Guid OrderId,
    Guid UserId,
    string UserName,
    int Rating,
    string Comment,
    Guid? MenuItemId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public record MenuItemFeedbackRatingDto(
    Guid MenuItemId,
    string MenuItemName,
    double AverageRating,
    int ReviewCount);

public record FeedbackSummaryDto(
    double AverageRating,
    int TotalReviews,
    Dictionary<int, int> StarCounts,
    IReadOnlyList<MenuItemFeedbackRatingDto> ItemRatings,
    IReadOnlyList<OrderFeedbackDto> RecentReviews);
