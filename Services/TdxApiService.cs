using DailyTrainTimetable.Models;

namespace DailyTrainTimetable.Services;

public sealed class TdxApiService : ITdxApiService
{
    public Task<DailyTrainData> FetchDailyDataAsync(
        DateOnly trainDate,
        DateTimeOffset updatedAt,
        IDictionary<string, Station> stationMap,
        CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}
