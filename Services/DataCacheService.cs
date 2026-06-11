using System.Text.Json;
using DailyTrainTimetable.Models;

namespace DailyTrainTimetable.Services;

public sealed class DataCacheService : IDataCacheService
{
    private readonly HttpClient _httpClient;
    private readonly Uri _pagesLatestUri;

    public DataCacheService(HttpClient httpClient, Uri? pagesLatestUri = null)
    {
        _httpClient = httpClient;
        _pagesLatestUri = pagesLatestUri ?? new Uri("https://example.com/data/latest.json");
    }

    public async Task<CacheResult> EvaluateAsync(
        string outputDir,
        IReadOnlyList<DateOnly> requestedDates,
        CancellationToken ct = default)
    {
        var skipDates = new HashSet<DateOnly>();
        var fetchDates = new HashSet<DateOnly>();
        var forceFetchDates = new HashSet<DateOnly>();

        var pagesLatest = await TryFetchPagesLatestAsync(ct);

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
