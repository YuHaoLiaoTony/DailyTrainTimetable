using System.Net;
using System.Text;
using DailyTrainTimetable.Services;
using Xunit;

namespace DailyTrainTimetable.Tests;

/// <summary>
/// token 快取行為（真實 TdxAuthService + fake HttpClient，不呼叫任何真實 TDX API）。
/// </summary>
public sealed class TdxAuthServiceCacheTests
{
    [Fact]
    public async Task GetAccessTokenAsync_should_request_token_only_once_within_lifetime()
    {
        var handler = new TokenEndpointHandler(expiresInSeconds: 3600);
        using var httpClient = new HttpClient(handler);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var sut = new TdxAuthService(httpClient, "id", "secret", time);

        var first = await sut.GetAccessTokenAsync();
        var second = await sut.GetAccessTokenAsync();
        var third = await sut.GetAccessTokenAsync();

        Assert.Equal("token-1", first);
        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetAccessTokenAsync_should_reuse_token_before_expiry_margin()
    {
        var handler = new TokenEndpointHandler(expiresInSeconds: 3600);
        using var httpClient = new HttpClient(handler);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var sut = new TdxAuthService(httpClient, "id", "secret", time);

        await sut.GetAccessTokenAsync();
        // 剩 10 分鐘，仍在 60 秒提前重取窗之外 → 應重用。
        time.Advance(TimeSpan.FromSeconds(3600 - 600));

        var second = await sut.GetAccessTokenAsync();

        Assert.Equal("token-1", second);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetAccessTokenAsync_should_refresh_when_token_is_close_to_expiry()
    {
        var handler = new TokenEndpointHandler(expiresInSeconds: 3600);
        using var httpClient = new HttpClient(handler);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var sut = new TdxAuthService(httpClient, "id", "secret", time);

        var first = await sut.GetAccessTokenAsync();
        // 剩 30 秒 → 已進入 60 秒提前重取窗，必須重取。
        time.Advance(TimeSpan.FromSeconds(3600 - 30));

        var second = await sut.GetAccessTokenAsync();

        Assert.Equal("token-1", first);
        Assert.Equal("token-2", second);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task GetAccessTokenAsync_should_share_single_token_request_across_parallel_callers()
    {
        var handler = new TokenEndpointHandler(expiresInSeconds: 3600, delay: TimeSpan.FromMilliseconds(50));
        using var httpClient = new HttpClient(handler);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var sut = new TdxAuthService(httpClient, "id", "secret", time);

        var tokens = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => sut.GetAccessTokenAsync()));

        Assert.All(tokens, token => Assert.Equal("token-1", token));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task InvalidateTokenAsync_should_force_new_token()
    {
        var handler = new TokenEndpointHandler(expiresInSeconds: 3600);
        using var httpClient = new HttpClient(handler);
        var sut = new TdxAuthService(httpClient, "id", "secret", FakeClock());

        var first = await sut.GetAccessTokenAsync();
        await sut.InvalidateTokenAsync(first);
        var second = await sut.GetAccessTokenAsync();

        Assert.Equal("token-1", first);
        Assert.Equal("token-2", second);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task InvalidateTokenAsync_should_keep_fresh_token_when_stale_token_does_not_match()
    {
        var handler = new TokenEndpointHandler(expiresInSeconds: 3600);
        using var httpClient = new HttpClient(handler);
        var sut = new TdxAuthService(httpClient, "id", "secret", FakeClock());

        await sut.GetAccessTokenAsync();
        // 別的請求端拿到的舊 token 失效，不應作廢目前快取中的新 token。
        await sut.InvalidateTokenAsync("token-from-another-request");

        var second = await sut.GetAccessTokenAsync();

        Assert.Equal("token-1", second);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetAccessTokenAsync_should_throw_when_response_missing_access_token()
    {
        var handler = new TokenEndpointHandler(expiresInSeconds: 3600, includeAccessToken: false);
        using var httpClient = new HttpClient(handler);
        var sut = new TdxAuthService(httpClient, "id", "secret", FakeClock());

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetAccessTokenAsync());
    }

    [Fact]
    public async Task GetAccessTokenAsync_should_throw_when_token_endpoint_fails()
    {
        var handler = new TokenEndpointHandler(expiresInSeconds: 3600, statusCode: HttpStatusCode.Unauthorized);
        using var httpClient = new HttpClient(handler);
        var sut = new TdxAuthService(httpClient, "id", "secret", FakeClock());

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.GetAccessTokenAsync());
    }

    [Fact]
    public async Task GetAccessTokenAsync_should_use_conservative_lifetime_when_expires_in_missing()
    {
        var handler = new TokenEndpointHandler(expiresInSeconds: null);
        using var httpClient = new HttpClient(handler);
        var time = FakeClock();
        var sut = new TdxAuthService(httpClient, "id", "secret", time);

        await sut.GetAccessTokenAsync();
        time.Advance(TimeSpan.FromMinutes(1));
        var reused = await sut.GetAccessTokenAsync();
        time.Advance(TimeSpan.FromMinutes(10));
        var refreshed = await sut.GetAccessTokenAsync();

        Assert.Equal("token-1", reused);
        Assert.Equal("token-2", refreshed);
        Assert.Equal(2, handler.RequestCount);
    }

    private static FakeTimeProvider FakeClock()
        => new(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

    /// <summary>假的 token endpoint：每次請求回傳 token-{n}。</summary>
    private sealed class TokenEndpointHandler : HttpMessageHandler
    {
        private readonly int? _expiresInSeconds;
        private readonly bool _includeAccessToken;
        private readonly HttpStatusCode _statusCode;
        private readonly TimeSpan _delay;
        private int _requestCount;

        public TokenEndpointHandler(
            int? expiresInSeconds,
            bool includeAccessToken = true,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            TimeSpan delay = default)
        {
            _expiresInSeconds = expiresInSeconds;
            _includeAccessToken = includeAccessToken;
            _statusCode = statusCode;
            _delay = delay;
        }

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var attempt = Interlocked.Increment(ref _requestCount);

            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, ct);
            }

            if (_statusCode != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent("{\"error\":\"unauthorized_credentials\"}", Encoding.UTF8, "application/json")
                };
            }

            var payload = new StringBuilder("{");
            if (_includeAccessToken)
            {
                payload.Append($"\"access_token\":\"token-{attempt}\",");
            }

            if (_expiresInSeconds.HasValue)
            {
                payload.Append($"\"expires_in\":{_expiresInSeconds.Value},");
            }

            payload.Append("\"token_type\":\"Bearer\"}");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json")
            };
        }
    }
}
