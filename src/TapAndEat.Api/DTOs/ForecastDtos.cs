namespace TapAndEat.Api.DTOs;

public record ForecastDto(
    Guid MenuItemId,
    string MenuItemName,
    string ForecastDate,
    decimal PredictedQuantity,
    string Model,
    int SampleDays,
    DateTimeOffset GeneratedAtUtc,
    List<DailyActualDto> RecentActuals);

public record DailyActualDto(string Date, int Quantity);
