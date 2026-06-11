using System.Text.Json;

namespace DailyTrainTimetable.Services;

public sealed class TdxAuthService : ITdxAuthService
{
    private const string TokenUrl = "https://tdx.transportdata.tw/auth/realms/TDXConnect/protocol/openid-connect/token";

    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private readonly string _clientSecret;

    public TdxAuthService(HttpClient httpClient, string clientId, string clientSecret)
    {
        _httpClient = httpClient;
        _clientId = clientId;
        _clientSecret = clientSecret;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
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

        return accessTokenElement.GetString() ?? throw new InvalidOperationException("TDX access_token was empty.");
    }
}
