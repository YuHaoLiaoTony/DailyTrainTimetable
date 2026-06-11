using DailyTrainTimetable.Models;

namespace DailyTrainTimetable.Services;

public sealed class DataWriterService : IDataWriterService
{
    public Task WriteDailyDataAsync(DailyTrainData data, string outputDir, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task WriteLatestAsync(LatestData latest, string outputDir, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task WriteStationsAsync(IReadOnlyList<Station> stations, string outputDir, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}
