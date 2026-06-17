using System.Text.Json;
using DailyTrainTimetable.Models;

namespace DailyTrainTimetable.Services;

public sealed class DataCacheService : IDataCacheService
{
    private readonly HttpClient _httpClient;
    private readonly Uri? _pagesLatestUri;

    public DataCacheService(HttpClient httpClient, Uri? pagesLatestUri = null)
    {
        _httpClient = httpClient;
        _pagesLatestUri = pagesLatestUri;
    }

    public async Task<CacheResult> EvaluateAsync(
        string outputDir,
        IReadOnlyList<DateOnly> requestedDates,
        CancellationToken ct = default)
    {
        var skipDates = new HashSet<DateOnly>();
        var fetchDates = new HashSet<DateOnly>();
        var forceFetchDates = new HashSet<DateOnly>();

        LatestData? pagesLatest = null;
        if (_pagesLatestUri != null)
        {
            pagesLatest = await TryFetchPagesLatestAsync(ct);
            if (pagesLatest == null)
            {
                Console.Error.WriteLine($"Warning: Could not fetch latest.json from {_pagesLatestUri}, falling back to file-only cache check.");
            }
        }

        foreach (var date in requestedDates)
        {
            var fileName = $"{date:yyyyMMdd}.json";
            var filePath = Path.Combine(outputDir, fileName);
            var fileExists = File.Exists(filePath);

            if (!fileExists)
            {
                fetchDates.Add(date);
                continue;
            }

            if (pagesLatest != null)
            {
                var dateStr = date.ToString("yyyy-MM-dd");
                var pagesDateEntry = pagesLatest.AvailableDates.Contains(dateStr);

                if (!pagesDateEntry)
                {
                    Console.WriteLine($"Date {dateStr} exists locally but not in Pages latest.json, re-fetching.");
                    fetchDates.Add(date);
                }
                else
                {
                    skipDates.Add(date);
                }
            }
            else
            {
                skipDates.Add(date);
            }
        }

        return new CacheResult(skipDates, fetchDates, forceFetchDates);
    }

    private async Task<LatestData?> TryFetchPagesLatestAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.GetAsync(_pagesLatestUri, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<LatestData>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch
        {
            return null;
        }
    }
}
