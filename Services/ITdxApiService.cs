using DailyTrainTimetable.Models;

namespace DailyTrainTimetable.Services;

public interface ITdxApiService
{
    Task<DailyTrainData> FetchDailyDataAsync(
        DateOnly trainDate,
        DateTimeOffset updatedAt,
        IDictionary<string, Station> stationMap,
        CancellationToken ct = default);
}
