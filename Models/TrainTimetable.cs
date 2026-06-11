namespace DailyTrainTimetable.Models;

public sealed record TrainTimetable(
    string TrainNo,
    int? Direction,
    string? TrainTypeId,
    string? TrainTypeCode,
    string? TrainTypeName,
    string? StartingStationId,
    string? StartingStationName,
    string? EndingStationId,
    string? EndingStationName,
    IReadOnlyList<StopTime> StopTimes);
