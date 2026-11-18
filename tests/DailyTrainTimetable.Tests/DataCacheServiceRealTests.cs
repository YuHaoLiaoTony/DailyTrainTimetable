using System.Net;
using System.Text.Json;
using DailyTrainTimetable.Models;
using DailyTrainTimetable.Services;
using Xunit;

namespace DailyTrainTimetable.Tests;

/// <summary>
/// DataCacheService 的行為測試（真實檔案系統 + fake HttpClient，不打外部 API）。
/// 版本比對規則：日期檔本身沒有版本欄位，因此以資料集索引 latest.json（本地優先、其次 Pages）
/// 的 dataVersion 作為整批日期檔的版本權威；版本不符或完全沒有版本資訊 → 視為過期重抓。
/// </summary>
public sealed class DataCacheServiceRealTests : IDisposable
{
    private const string Version = "1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _tempDir;

    public DataCacheServiceRealTests()
    {
        _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task EvaluateAsync_should_skip_when_version_matches_and_no_pages_uri()
    {
        SeedDateFile("20260601");
        SeedDateFile("20260602");
        await SeedLatestAsync(Version);

        using var httpClient = new HttpClient();
        var service = new DataCacheService(httpClient, pagesLatestUri: null);

        var dates = new List<DateOnly> { new(2026, 6, 1), new(2026, 6, 2) };
        var result = await service.EvaluateAsync(_tempDir, dates, new CacheEvaluationOptions(Version));

        Assert.Equal(2, result.SkipDates.Count);
        Assert.Empty(result.FetchDates);
        Assert.Empty(result.ForceFetchDates);
    }

    [Fact]
    public async Task EvaluateAsync_should_refetch_when_no_version_info_available()
    {
        SeedDateFile("20260601");

        using var httpClient = new HttpClient();
        var service = new DataCacheService(httpClient, pagesLatestUri: null);

        var dates = new List<DateOnly> { new(2026, 6, 1) };
        var result = await service.EvaluateAsync(_tempDir, dates, new CacheEvaluationOptions(Version));

        // 沒有任何版本資訊可用（缺 latest.json、無 Pages）→ 版本無法確認，視為過期。
        Assert.Empty(result.SkipDates);
        Assert.Empty(result.FetchDates);
        Assert.Contains(new DateOnly(2026, 6, 1), result.ForceFetchDates);
    }

    [Fact]
    public async Task EvaluateAsync_should_force_refetch_when_local_version_differs()
    {
        SeedDateFile("20260601");
        SeedDateFile("20260602");
        await SeedLatestAsync("0");

        using var httpClient = new HttpClient();
        var service = new DataCacheService(httpClient, pagesLatestUri: null);

        var dates = new List<DateOnly> { new(2026, 6, 1), new(2026, 6, 2) };
        var result = await service.EvaluateAsync(_tempDir, dates, new CacheEvaluationOptions(Version));

        Assert.Empty(result.SkipDates);
        Assert.Empty(result.FetchDates);
        Assert.Equal(2, result.ForceFetchDates.Count);
        Assert.Contains(new DateOnly(2026, 6, 1), result.ForceFetchDates);
        Assert.Contains(new DateOnly(2026, 6, 2), result.ForceFetchDates);
    }

    [Fact]
    public async Task EvaluateAsync_should_refetch_when_latest_json_lacks_version()
    {
        SeedDateFile("20260601");
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(_tempDir, "latest.json"),
            "{\"updatedAt\":\"2026-06-01T09:00:00+08:00\",\"availableDates\":[\"2026-06-01\"]}");

        using var httpClient = new HttpClient();
        var service = new DataCacheService(httpClient, pagesLatestUri: null);

        var dates = new List<DateOnly> { new(2026, 6, 1) };
        var result = await service.EvaluateAsync(_tempDir, dates, new CacheEvaluationOptions(Version));

        Assert.Empty(result.SkipDates);
        Assert.Contains(new DateOnly(2026, 6, 1), result.ForceFetchDates);
    }

    [Fact]
    public async Task EvaluateAsync_should_force_refetch_dates_requested_by_cli()
    {
        SeedDateFile("20260601");
        SeedDateFile("20260602");
        await SeedLatestAsync(Version);

        using var httpClient = new HttpClient();
        var service = new DataCacheService(httpClient, pagesLatestUri: null);

        var dates = new List<DateOnly> { new(2026, 6, 1), new(2026, 6, 2) };
        var forced = new HashSet<DateOnly> { new(2026, 6, 1) };
        var result = await service.EvaluateAsync(
            _tempDir, dates, new CacheEvaluationOptions(Version, forced));

        Assert.Contains(new DateOnly(2026, 6, 1), result.ForceFetchDates);
        Assert.DoesNotContain(new DateOnly(2026, 6, 1), result.SkipDates);
        Assert.Contains(new DateOnly(2026, 6, 2), result.SkipDates);
        Assert.Single(result.SkipDates);
    }

    [Fact]
    public async Task EvaluateAsync_should_force_refetch_forced_dates_even_without_file()
    {
        await SeedLatestAsync(Version);

        using var httpClient = new HttpClient();
        var service = new DataCacheService(httpClient, pagesLatestUri: null);

        var dates = new List<DateOnly> { new(2026, 6, 1) };
        var forced = new HashSet<DateOnly> { new(2026, 6, 1) };
        var result = await service.EvaluateAsync(
            _tempDir, dates, new CacheEvaluationOptions(Version, forced));

        Assert.Contains(new DateOnly(2026, 6, 1), result.ForceFetchDates);
        Assert.Empty(result.FetchDates);
    }

    [Fact]
    public async Task EvaluateAsync_should_fetch_when_files_missing()
    {
        SeedDateFile("20260601");
        await SeedLatestAsync(Version);

        using var httpClient = new HttpClient();
        var service = new DataCacheService(httpClient, pagesLatestUri: null);

        var dates = new List<DateOnly> { new(2026, 6, 1), new(2026, 6, 2), new(2026, 6, 3) };
        var result = await service.EvaluateAsync(_tempDir, dates, new CacheEvaluationOptions(Version));

        Assert.Single(result.SkipDates);
        Assert.Equal(2, result.FetchDates.Count);
        Assert.Contains(new DateOnly(2026, 6, 2), result.FetchDates);
        Assert.Contains(new DateOnly(2026, 6, 3), result.FetchDates);
        Assert.Empty(result.ForceFetchDates);
    }

    [Fact]
    public async Task EvaluateAsync_should_fetch_when_file_exists_but_not_in_pages()
    {
        SeedDateFile("20260601");

        using var httpClient = new HttpClient(CreatePagesHandler(new LatestData(
            DateTimeOffset.Now, new List<string> { "2026-06-02" }, Version)));
        var service = new DataCacheService(httpClient, PagesUri);

        var dates = new List<DateOnly> { new(2026, 6, 1) };
        var result = await service.EvaluateAsync(_tempDir, dates, new CacheEvaluationOptions(Version));

        Assert.Empty(result.SkipDates);
        Assert.Single(result.FetchDates);
        Assert.Contains(new DateOnly(2026, 6, 1), result.FetchDates);
    }

    [Fact]
    public async Task EvaluateAsync_should_skip_when_file_exists_and_in_pages()
    {
        SeedDateFile("20260601");

        using var httpClient = new HttpClient(CreatePagesHandler(new LatestData(
            DateTimeOffset.Now, new List<string> { "2026-06-01" }, Version)));
        var service = new DataCacheService(httpClient, PagesUri);

        var dates = new List<DateOnly> { new(2026, 6, 1) };
        var result = await service.EvaluateAsync(_tempDir, dates, new CacheEvaluationOptions(Version));

        Assert.Single(result.SkipDates);
        Assert.Empty(result.FetchDates);
        Assert.Empty(result.ForceFetchDates);
    }

    [Fact]
    public async Task EvaluateAsync_should_force_refetch_when_pages_version_differs()
    {
        SeedDateFile("20260601");

        using var httpClient = new HttpClient(CreatePagesHandler(new LatestData(
            DateTimeOffset.Now, new List<string> { "2026-06-01" }, "0")));
        var service = new DataCacheService(httpClient, PagesUri);

        var dates = new List<DateOnly> { new(2026, 6, 1) };
        var result = await service.EvaluateAsync(_tempDir, dates, new CacheEvaluationOptions(Version));

        Assert.Empty(result.SkipDates);
        Assert.Contains(new DateOnly(2026, 6, 1), result.ForceFetchDates);
    }

    [Fact]
    public async Task EvaluateAsync_should_use_local_version_when_pages_unreachable()
    {
        SeedDateFile("20260601");
        await SeedLatestAsync(Version);

        using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var service = new DataCacheService(httpClient, PagesUri);

        var dates = new List<DateOnly> { new(2026, 6, 1) };
        var result = await service.EvaluateAsync(_tempDir, dates, new CacheEvaluationOptions(Version));

        Assert.Single(result.SkipDates);
        Assert.Empty(result.FetchDates);
        Assert.Empty(result.ForceFetchDates);
    }

    [Fact]
    public async Task EvaluateAsync_should_handle_empty_requested_dates()
    {
        using var httpClient = new HttpClient();
        var service = new DataCacheService(httpClient, pagesLatestUri: null);

        var result = await service.EvaluateAsync(_tempDir, new List<DateOnly>(), new CacheEvaluationOptions(Version));

        Assert.Empty(result.SkipDates);
        Assert.Empty(result.FetchDates);
        Assert.Empty(result.ForceFetchDates);
    }

    private static Uri PagesUri => new("https://example.com/data/latest.json");

    private void SeedDateFile(string yyyyMMdd)
        => File.WriteAllText(System.IO.Path.Combine(_tempDir, $"{yyyyMMdd}.json"), "{}");

    private Task SeedLatestAsync(string dataVersion, params string[] availableDates)
    {
        var latest = new LatestData(
            UpdatedAt: DateTimeOffset.Now,
            AvailableDates: availableDates.Length == 0
                ? new List<string>()
                : availableDates.ToList(),
            DataVersion: dataVersion);

        return File.WriteAllTextAsync(
            System.IO.Path.Combine(_tempDir, "latest.json"),
            JsonSerializer.Serialize(latest, JsonOptions));
    }

    private static FakeHttpMessageHandler CreatePagesHandler(LatestData latest)
    {
        var json = JsonSerializer.Serialize(latest, JsonOptions);
        return new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _send;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
        {
            _send = send;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            return Task.FromResult(_send(request));
        }
    }
}
