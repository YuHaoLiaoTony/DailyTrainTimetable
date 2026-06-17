using System.Net;
using System.Text.Json;
using DailyTrainTimetable.Models;
using DailyTrainTimetable.Services;
using NSubstitute;
using Xunit;

namespace DailyTrainTimetable.Tests;

public sealed class TdxApiServiceRealTests
{
    [Fact]
    public async Task FetchDailyDataAsync_should_succeed_after_429_retries()
    {
        var attemptCount = 0;
        var handler = new FakeHttpMessageHandler(request =>
        {
            attemptCount++;

            if (attemptCount <= 2)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                    TimeSpan.FromMilliseconds(1));
                return response;
            }

            var body = JsonSerializer.Serialize(new
            {
                TrainNo = "123",
                Direction = 0,
                TrainTypeID = "1100",
                TrainTypeCode = "1",
                TrainTypeName = new { Zh_tw = "自強" },
                StartingStationID = "1000",
                StartingStationName = new { Zh_tw = "臺北" },
                EndingStationID = "1025",
                EndingStationName = new { Zh_tw = "新竹" },
                StopTimes = new[]
                {
                    new
                    {
                        StationID = "1000",
                        StationName = new { Zh_tw = "臺北" },
                        ArrivalTime = "08:00",
                        DepartureTime = "08:02",
                        StopSequence = 1
                    }
                }
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        var authService = Substitute.For<ITdxAuthService>();
        authService.GetAccessTokenAsync(default).ReturnsForAnyArgs("fake-token");

        var apiService = new TdxApiService(httpClient, authService);
        var result = await apiService.FetchDailyDataAsync(
            new DateOnly(2026, 6, 1),
            DateTimeOffset.Now,
            new Dictionary<string, Station>());

        Assert.NotNull(result);
        Assert.Equal("2026-06-01", result.TrainDate);
        Assert.Equal(3, attemptCount);
    }

    [Fact]
    public async Task FetchDailyDataAsync_should_throw_after_exhausting_429_retries()
    {
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                TimeSpan.FromMilliseconds(1));
            return response;
        });

        using var httpClient = new HttpClient(handler);
        var authService = Substitute.For<ITdxAuthService>();
        authService.GetAccessTokenAsync(default).ReturnsForAnyArgs("fake-token");

        var apiService = new TdxApiService(httpClient, authService);
        var stationMap = new Dictionary<string, Station>();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            apiService.FetchDailyDataAsync(new DateOnly(2026, 6, 1), DateTimeOffset.Now, stationMap));

        Assert.StartsWith("TDX API returned HTTP 429", ex.Message);
    }

    [Fact]
    public async Task FetchDailyDataAsync_should_throw_immediately_on_non_429_error()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"bad request\"}", System.Text.Encoding.UTF8, "application/json")
            });

        using var httpClient = new HttpClient(handler);
        var authService = Substitute.For<ITdxAuthService>();
        authService.GetAccessTokenAsync(default).ReturnsForAnyArgs("fake-token");

        var apiService = new TdxApiService(httpClient, authService);
        var stationMap = new Dictionary<string, Station>();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            apiService.FetchDailyDataAsync(new DateOnly(2026, 6, 1), DateTimeOffset.Now, stationMap));

        Assert.Contains("bad request", ex.Message);
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
