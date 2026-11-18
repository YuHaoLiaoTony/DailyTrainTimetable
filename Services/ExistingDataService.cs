using System.Text.Json;
using DailyTrainTimetable.Models;

namespace DailyTrainTimetable.Services;

public sealed class ExistingDataService : IExistingDataService
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<LatestData?> TryReadLatestAsync(string outputDir, CancellationToken ct = default)
    {
        var path = Path.Combine(outputDir, "latest.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<LatestData>(stream, ReadOptions, ct);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Warning: could not read {path}: {ex.Message}");
            return null;
        }
    }

    public async Task<IReadOnlyList<Station>> TryReadStationsAsync(string outputDir, CancellationToken ct = default)
    {
        var path = Path.Combine(outputDir, "stations.json");
        if (!File.Exists(path))
        {
            return Array.Empty<Station>();
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var stations = await JsonSerializer.DeserializeAsync<List<Station>>(stream, ReadOptions, ct);
            return stations ?? (IReadOnlyList<Station>)Array.Empty<Station>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Warning: could not read {path}: {ex.Message}");
            return Array.Empty<Station>();
        }
    }

    public bool DateFileExists(string outputDir, DateOnly date)
        => File.Exists(Path.Combine(outputDir, $"{date:yyyyMMdd}.json"));
}
