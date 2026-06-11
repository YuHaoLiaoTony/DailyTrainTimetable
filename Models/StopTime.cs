namespace DailyTrainTimetable.Models;

public sealed record StopTime(
    string StationId,
    string StationName,
    string? ArrivalTime,
    string? DepartureTime,
    int? StopSequence);
