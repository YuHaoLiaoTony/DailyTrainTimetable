using System.Globalization;
using System.Text.Json;

namespace DailyTrainTimetable.Services;

/// <summary>
/// TDX OAuth（client credentials）token 取得與快取。
///
/// 行為：
/// 1. 同一個 process 內只向 token endpoint 索取一次 token，之後重複使用；
///    只有在「快過期」時才重取（提前 <see cref="ExpiryMargin"/> 秒）。
/// 2. 併發呼叫者由 <see cref="SemaphoreSlim"/> 序列化，且進鎖後會再檢查一次快取，
///    因此多個日期平行抓取時也只會產生一個 token 請求。
/// 3. 呼叫端若發現 token 已失效（TDX API 回 401），可呼叫 <see cref="InvalidateTokenAsync"/>
///    作廢「就是失敗的那個」token，讓下一次 <see cref="GetAccessTokenAsync"/> 強制重取。
/// </summary>
public sealed class TdxAuthService : ITdxAuthService
{
    private const string TokenUrl = "https://tdx.transportdata.tw/auth/realms/TDXConnect/protocol/openid-connect/token";

    /// <summary>提前重取的寬限時間，避免拿到「剛好在傳輸途中過期」的 token。</summary>
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(60);

    /// <summary>回應沒有可用的 expires_in 時的保守假設壽命（寧可多取一次，也不要拿過期 token）。</summary>
    private static readonly TimeSpan MissingExpiryFallback = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    // 單一不可變物件，讀取端不需要鎖；寫入端一律在 _refreshLock 內。
    private volatile CachedToken? _cachedToken;

    public TdxAuthService(
        HttpClient httpClient,
        string clientId,
        string clientSecret,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (TryGetValidToken(out var cachedToken))
        {
            return cachedToken;
        }

        await _refreshLock.WaitAsync(ct);
        try
        {
            // 等鎖期間其他呼叫者可能已經取到新 token，這裡再檢查一次避免重複請求。
            if (TryGetValidToken(out cachedToken))
            {
                return cachedToken;
            }

            var token = await RequestTokenAsync(ct);
            return token.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task InvalidateTokenAsync(string? staleToken = null, CancellationToken ct = default)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            var cached = _cachedToken;
            if (cached == null)
            {
                return;
            }

            // 只有「快取中的 token 就是剛剛失敗的那個」才作廢，
            // 否則平行呼叫端會把別人剛取到的新 token 又清掉，反而放大請求量。
            if (staleToken != null && !string.Equals(cached.AccessToken, staleToken, StringComparison.Ordinal))
            {
                return;
            }

            _cachedToken = null;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool TryGetValidToken(out string accessToken)
    {
        var cached = _cachedToken;
        if (cached != null && _timeProvider.GetUtcNow() < cached.ExpiresAt)
        {
            accessToken = cached.AccessToken;
            return true;
        }

        accessToken = string.Empty;
        return false;
    }

    /// <summary>實際發出 token 請求；必須在 <see cref="_refreshLock"/> 內呼叫。</summary>
    private async Task<CachedToken> RequestTokenAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _clientId,
                ["client_secret"] = _clientSecret
            })
        };

        using var response = await _httpClient.SendAsync(request, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"TDX token endpoint returned HTTP {(int)response.StatusCode} {response.StatusCode}. Response: {responseText}");
        }

        using var document = JsonDocument.Parse(responseText);
        if (!document.RootElement.TryGetProperty("access_token", out var accessTokenElement))
        {
            throw new InvalidOperationException("TDX token response did not contain access_token.");
        }

        var accessToken = accessTokenElement.GetString()
            ?? throw new InvalidOperationException("TDX access_token was empty.");

        var lifetime = ReadLifetime(document.RootElement);
        var margin = lifetime > ExpiryMargin ? ExpiryMargin : TimeSpan.FromTicks(lifetime.Ticks / 2);
        var token = new CachedToken(accessToken, _timeProvider.GetUtcNow() + lifetime - margin);
        _cachedToken = token;
        return token;
    }

    private static TimeSpan ReadLifetime(JsonElement root)
    {
        if (root.TryGetProperty("expires_in", out var expiresInElement))
        {
            var seconds = expiresInElement.ValueKind switch
            {
                JsonValueKind.Number when expiresInElement.TryGetDouble(out var number) => number,
                JsonValueKind.String when double.TryParse(
                    expiresInElement.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed) => parsed,
                _ => 0d
            };

            if (seconds > 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }

        Console.Error.WriteLine(
            $"Warning: TDX token response did not contain a usable expires_in; assuming {MissingExpiryFallback.TotalMinutes:0} minute lifetime.");
        return MissingExpiryFallback;
    }

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt);
}
