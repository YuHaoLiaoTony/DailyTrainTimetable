using System.Net;
using System.Text.Json;
using DailyTrainTimetable.Models;
using DailyTrainTimetable.Services;
using Xunit;

namespace DailyTrainTimetable.Tests;

public sealed class DataCacheServiceRealTests : IDisposable
{
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
    public async Task EvaluateAsync_should_skip_when_all_files_exist_and_no_pages_uri()
    {
        File.WriteAllText(System.IO.Path.Combine(_tempDir, "20260601.json"), "{}");
        File.WriteAllText(System.IO.Path.Combine(_tempDir, "20260602.json"), "{}");

        using var httpClient = new HttpClient();
        var service = new DataCacheService(httpClient, pagesLatestUri: null);

        var dates = new List<DateOnly> { new(2026, 6, 1), new(2026, 6, 2) };
        var result = await service.EvaluateAsync(_tempDir, dates);

        Assert.Equal(2, result.SkipDates.Count);
        Assert.Empty(result.FetchDates);
        Assert.Empty(result.ForceFetchDates);
    }

    [Fact]
    public async Task EvaluateAsync_should_fetch_when_files_missing()
    {
        File.WriteAllText(System.IO.Path.Combine(_tempDir, "20260601.json"), "{}");

        using var httpClient = new HttpClient();
        var service = new DataCacheService(httpClient, pagesLatestUri: null);

        var dates = new List<DateOnly> { new(2026, 6, 1), new(2026, 6, 2), new(2026, 6, 3) };
        var result = await service.EvaluateAsync(_tempDir, dates);

        Assert.Single(result.SkipDates);
        Assert.Equal(2, result.FetchDates.Count);
        Assert.Contains(new DateOnly(2026, 6, 2), result.FetchDates);
        Assert.Contains(new DateOnly(2026, 6, 3), result.FetchDates);
    }

    [Fact]
    public async Task EvaluateAsync_should_fetch_when_file_exists_but_not_in_pages()
    {
        File.WriteAllText(System.IO.Path.Combine(_tempDir, "20260601.json"), "{}");

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var pagesJson = JsonSerializer.Serialize(new LatestData(
            UpdatedAt: DateTimeOffset.Now,
            AvailableDates: new List<string> { "2026-06-02" },
            DataVersion: "1"), jsonOptions);

        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(pagesJson, System.Text.Encoding.UTF8, "application/json")
            });

        using var httpClient = new HttpClient(handler);
        var service = new DataCacheService(httpClient, new Uri("https://example.com/data/latest.json"));

        var dates = new List<DateOnly> { new(2026, 6, 1) };
        var result = await service.EvaluateAsync(_tempDir, dates);

        Assert.Empty(result.SkipDates);
        Assert.Single(result.FetchDates);
        Assert.Contains(new DateOnly(2026, 6, 1), result.FetchDates);
    }

    [Fact]
    public async Task EvaluateAsync_should_skip_when_file_exists_and_in_pages()
    {
        File.WriteAllText(System.IO.Path.Combine(_tempDir, "20260601.json"), "{}");

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var pagesJson = JsonSerializer.Serialize(new LatestData(
            UpdatedAt: DateTimeOffset.Now,
            AvailableDates: new List<string> { "2026-06-01" },
            DataVersion: "1"), jsonOptions);

        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(pagesJson, System.Text.Encoding.UTF8, "application/json")
            });

        using var httpClient = new HttpClient(handler);
        var service = new DataCacheService(httpClient, new Uri("https://example.com/data/latest.json"));

        var dates = new List<DateOnly> { new(2026, 6, 1) };
        var result = await service.EvaluateAsync(_tempDir, dates);

        Assert.Single(result.SkipDates);
        Assert.Empty(result.FetchDates);
    }

    [Fact]
    public async Task EvaluateAsync_should_fallback_to_file_only_when_pages_unreachable()
    {
        File.WriteAllText(System.IO.Path.Combine(_tempDir, "20260601.json"), "{}");

        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        using var httpClient = new HttpClient(handler);
        var service = new DataCacheService(httpClient, new Uri("https://example.com/data/latest.json"));

        var dates = new List<DateOnly> { new(2026, 6, 1) };
        var result = await service.EvaluateAsync(_tempDir, dates);

        Assert.Single(result.SkipDates);
        Assert.Empty(result.FetchDates);
    }

    [Fact]
    public async Task EvaluateAsync_should_handle_empty_requested_dates()
    {
        using var httpClient = new HttpClient();
        var service = new DataCacheService(httpClient, pagesLatestUri: null);

        var result = await service.EvaluateAsync(_tempDir, new List<DateOnly>());

        Assert.Empty(result.SkipDates);
        Assert.Empty(result.FetchDates);
        Assert.Empty(result.ForceFetchDates);
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
