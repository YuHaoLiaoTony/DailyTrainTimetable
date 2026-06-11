namespace DailyTrainTimetable.Models;

public sealed record LatestData(
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> AvailableDates,
    string DataVersion);
