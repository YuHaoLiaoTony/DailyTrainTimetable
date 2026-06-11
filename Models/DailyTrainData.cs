namespace DailyTrainTimetable.Models;

public sealed record DailyTrainData(
    string TrainDate,
    string Source,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TrainTimetable> TrainTimetables);
