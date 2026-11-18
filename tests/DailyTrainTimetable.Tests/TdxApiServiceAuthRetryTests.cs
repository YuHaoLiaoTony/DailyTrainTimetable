using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DailyTrainTimetable.Models;
using DailyTrainTimetable.Services;
using Xunit;

namespace DailyTrainTimetable.Tests;

/// <summary>
/// 401 後強制重取 token 的整合行為（真實 TdxAuthService + TdxApiService + fake HttpClient）。
/// </summary>
public sealed class TdxApiServiceAuthRetryTests
{
    private const string ValidBody = """
        [
          {
            "TrainNo": "123",
            "Direction": 0,
            "TrainTypeID": "1100",
            "TrainTypeName": { "Zh_tw": "自強" },
            "StartingStationID": "1000",
            "StartingStationName": { "Zh_tw": "臺北" },
            "EndingStationID": "1025",
            "EndingStationName": { "Zh_tw": "新竹" },
            "StopTimes": [
              {
                "StationID": "1000",
                "StationName": { "Zh_tw": "臺北" },
                "ArrivalTime": "08:00",
                "DepartureTime": "08:02",
                "StopSequence": 1
              }
            ]
          }
        ]
        """;

    [Fact]
    public async Task FetchDailyDataAsync_should_refresh_token_and_retry_once_on_401()
    {
        var handler = new FakeTdxHandler(staleTokens: new HashSet<string> { "token-1" });
        using var httpClient = new HttpClient(handler);
        var authService = new TdxAuthService(httpClient, "id", "secret", new FakeTimeProvider(DateTimeOffset.UtcNow));
        var apiService = new TdxApiService(httpClient, authService);

        var result = await apiService.FetchDailyDataAsync(
            new DateOnly(2026, 6, 1), DateTimeOffset.Now, new Dictionary<string, Station>());

        Assert.Single(result.TrainTimetables);
        Assert.Equal("123", result.TrainTimetables[0].TrainNo);
        Assert.Equal(2, handler.TokenRequestCount);
        Assert.Equal(2, handler.ApiRequestCount);
    }

    [Fact]
    public async Task FetchDailyDataAsync_should_reuse_cached_token_across_multiple_dates()
    {
        var handler = new FakeTdxHandler(staleTokens: new HashSet<string>());
        using var httpClient = new HttpClient(handler);
        var authService = new TdxAuthService(httpClient, "id", "secret", new FakeTimeProvider(DateTimeOffset.UtcNow));
        var apiService = new TdxApiService(httpClient, authService);
        var stationMap = new Dictionary<string, Station>();

        for (var offset = 0; offset < 3; offset++)
        {
            await apiService.FetchDailyDataAsync(
                new DateOnly(2026, 6, 1).AddDays(offset), DateTimeOffset.Now, stationMap);
        }

        Assert.Equal(1, handler.TokenRequestCount);
        Assert.Equal(3, handler.ApiRequestCount);
    }

    [Fact]
    public async Task FetchDailyDataAsync_should_throw_when_still_unauthorized_after_refresh()
    {
        var handler = new FakeTdxHandler(staleTokens: new HashSet<string> { "token-1", "token-2" });
        using var httpClient = new HttpClient(handler);
        var authService = new TdxAuthService(httpClient, "id", "secret", new FakeTimeProvider(DateTimeOffset.UtcNow));
        var apiService = new TdxApiService(httpClient, authService);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => apiService.FetchDailyDataAsync(
            new DateOnly(2026, 6, 1), DateTimeOffset.Now, new Dictionary<string, Station>()));

        Assert.Contains("401", ex.Message);
        // 只重取一次 token、只重試一次，不無限放大請求量。
        Assert.Equal(2, handler.TokenRequestCount);
        Assert.Equal(2, handler.ApiRequestCount);
    }

    private sealed class FakeTdxHandler : HttpMessageHandler
    {
        private readonly HashSet<string> _staleTokens;
        private int _tokenRequestCount;
        private int _apiRequestCount;

        public FakeTdxHandler(HashSet<string> staleTokens)
        {
            _staleTokens = staleTokens;
        }

        public int TokenRequestCount => Volatile.Read(ref _tokenRequestCount);

        public int ApiRequestCount => Volatile.Read(ref _apiRequestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post)
            {
                var attempt = Interlocked.Increment(ref _tokenRequestCount);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{{\"access_token\":\"token-{attempt}\",\"expires_in\":3600,\"token_type\":\"Bearer\"}}",
                        Encoding.UTF8,
                        "application/json")
                });
            }

            Interlocked.Increment(ref _apiRequestCount);
            var bearer = request.Headers.Authorization?.Parameter ?? string.Empty;
            if (request.Headers.Authorization?.Scheme != "Bearer" || _staleTokens.Contains(bearer))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("{\"error\":\"invalid_token\"}", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ValidBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
