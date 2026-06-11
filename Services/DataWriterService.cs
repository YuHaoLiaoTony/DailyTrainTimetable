using System.Text.Json;
using System.Text.Json.Serialization;
using DailyTrainTimetable.Models;

namespace DailyTrainTimetable.Services;

public sealed class DataWriterService : IDataWriterService
{
    private readonly JsonSerializerOptions _options;

    public DataWriterService(JsonSerializerOptions? options = null)
    {
        _options = options ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task WriteDailyDataAsync(DailyTrainData data, string outputDir, CancellationToken ct = default)
    {
        var fileName = data.TrainDate.Replace("-", "") + ".json";
        var path = Path.Combine(outputDir, fileName);
        await WriteJsonAtomicallyAsync(path, data, ct);
    }

    public async Task WriteLatestAsync(LatestData latest, string outputDir, CancellationToken ct = default)
    {
        var path = Path.Combine(outputDir, "latest.json");
        await WriteJsonAtomicallyAsync(path, latest, ct);
    }

    public async Task WriteStationsAsync(IReadOnlyList<Station> stations, string outputDir, CancellationToken ct = default)
    {
        var path = Path.Combine(outputDir, "stations.json");
        await WriteJsonAtomicallyAsync(path, stations, ct);
    }

    private async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, value, _options, ct);
            await stream.WriteAsync("\n"u8.ToArray(), ct);
        }

        File.Move(tempPath, path, overwrite: true);
    }
}
