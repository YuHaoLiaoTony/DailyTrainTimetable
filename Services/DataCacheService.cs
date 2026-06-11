using DailyTrainTimetable.Models;

namespace DailyTrainTimetable.Services;

public sealed class DataCacheService : IDataCacheService
{
    public Task<CacheResult> EvaluateAsync(
        string outputDir,
        IReadOnlyList<DateOnly> requestedDates,
        CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}
