using DailyTrainTimetable.Models;

namespace DailyTrainTimetable.Services;

public interface IDataCacheService
{
    Task<CacheResult> EvaluateAsync(
        string outputDir,
        IReadOnlyList<DateOnly> requestedDates,
        CancellationToken ct = default);
}
