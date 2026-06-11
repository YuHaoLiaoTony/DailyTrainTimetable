namespace DailyTrainTimetable.Models;

public sealed record CacheResult(
    IReadOnlySet<DateOnly> SkipDates,
    IReadOnlySet<DateOnly> FetchDates,
    IReadOnlySet<DateOnly> ForceFetchDates);
