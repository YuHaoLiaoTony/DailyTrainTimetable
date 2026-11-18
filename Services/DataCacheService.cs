using System.Text.Json;
using DailyTrainTimetable.Models;

namespace DailyTrainTimetable.Services;

public sealed class DataCacheService : IDataCacheService
{
    private readonly HttpClient _httpClient;
    private readonly Uri? _pagesLatestUri;
    private readonly IExistingDataService _existingDataService;

    public DataCacheService(
        HttpClient httpClient,
        Uri? pagesLatestUri = null,
        IExistingDataService? existingDataService = null)
    {
        _httpClient = httpClient;
        _pagesLatestUri = pagesLatestUri;
        _existingDataService = existingDataService ?? new ExistingDataService();
    }

    public async Task<CacheResult> EvaluateAsync(
        string outputDir,
        IReadOnlyList<DateOnly> requestedDates,
        CacheEvaluationOptions? options = null,
        CancellationToken ct = default)
    {
        var skipDates = new HashSet<DateOnly>();
        var fetchDates = new HashSet<DateOnly>();
        var forceFetchDates = new HashSet<DateOnly>();

        var currentVersion = options?.CurrentDataVersion;
        var forceRequested = options?.ForceFetchDates ?? (IReadOnlySet<DateOnly>)new HashSet<DateOnly>();

        LatestData? pagesLatest = null;
        if (_pagesLatestUri != null)
        {
            pagesLatest = await TryFetchPagesLatestAsync(ct);
            if (pagesLatest == null)
            {
                Console.Error.WriteLine($"Warning: Could not fetch latest.json from {_pagesLatestUri}, falling back to local file-only cache check.");
            }
        }

        // 日期檔本身沒有版本欄位，故以資料集索引 latest.json（本地優先，其次 Pages）的
        // dataVersion 作為整批日期檔的版本權威；版本不符或兩者都沒有版本資訊時，
        // 既有日期檔一律視為過期，重抓後覆寫。
        var localLatest = await _existingDataService.TryReadLatestAsync(outputDir, ct);
        var versionCheckEnabled = !string.IsNullOrWhiteSpace(currentVersion);
        var versionIsCurrent = IsVersionCurrent(currentVersion, localLatest?.DataVersion)
            || IsVersionCurrent(currentVersion, pagesLatest?.DataVersion);
        var versionStale = versionCheckEnabled && !versionIsCurrent;

        if (versionStale)
        {
            Console.WriteLine(
                $"Cache version mismatch: cached dataVersion='{DescribeVersion(localLatest?.DataVersion)}', " +
                $"Pages dataVersion='{DescribeVersion(pagesLatest?.DataVersion)}', current='{currentVersion}'. " +
                "Existing date files will be re-fetched.");
        }

        foreach (var date in requestedDates)
        {
            var dateStr = date.ToString("yyyy-MM-dd");
            var fileExists = _existingDataService.DateFileExists(outputDir, date);

            if (forceRequested.Contains(date))
            {
                forceFetchDates.Add(date);
                continue;
            }

            if (!fileExists)
            {
                fetchDates.Add(date);
                continue;
            }

            if (versionStale)
            {
                forceFetchDates.Add(date);
                continue;
            }

            if (pagesLatest?.AvailableDates != null && !pagesLatest.AvailableDates.Contains(dateStr))
            {
                Console.WriteLine($"Date {dateStr} exists locally but not in Pages latest.json, re-fetching.");
                fetchDates.Add(date);
                continue;
            }

            skipDates.Add(date);
        }

        return new CacheResult(skipDates, fetchDates, forceFetchDates);
    }

    private static bool IsVersionCurrent(string? currentVersion, string? cachedVersion)
    {
        if (string.IsNullOrWhiteSpace(currentVersion) || string.IsNullOrWhiteSpace(cachedVersion))
        {
            return false;
        }

        return string.Equals(currentVersion, cachedVersion, StringComparison.Ordinal);
    }

    private static string DescribeVersion(string? version)
        => string.IsNullOrWhiteSpace(version) ? "(none)" : version;

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
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }
}
