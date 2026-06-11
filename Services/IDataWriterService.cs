using DailyTrainTimetable.Models;

namespace DailyTrainTimetable.Services;

public interface IDataWriterService
{
    Task WriteDailyDataAsync(DailyTrainData data, string outputDir, CancellationToken ct = default);
    Task WriteLatestAsync(LatestData latest, string outputDir, CancellationToken ct = default);
    Task WriteStationsAsync(IReadOnlyList<Station> stations, string outputDir, CancellationToken ct = default);
}
