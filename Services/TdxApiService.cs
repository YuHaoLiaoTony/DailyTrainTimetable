using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using DailyTrainTimetable.Models;

namespace DailyTrainTimetable.Services;

public sealed class TdxApiService : ITdxApiService
{
    private const string ApiBaseUrl = "https://tdx.transportdata.tw/api/basic/v3/Rail/TRA/DailyTrainTimetable/TrainDate";

    private readonly HttpClient _httpClient;
    private readonly ITdxAuthService _authService;

    public TdxApiService(HttpClient httpClient, ITdxAuthService authService)
    {
        _httpClient = httpClient;
        _authService = authService;
    }

    public async Task<DailyTrainData> FetchDailyDataAsync(
        DateOnly trainDate,
        DateTimeOffset updatedAt,
        IDictionary<string, Station> stationMap,
        CancellationToken ct = default)
    {
        var token = await _authService.GetAccessTokenAsync(ct);

        using var response = await SendWithRetryAsync(trainDate, token, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);

        using var document = JsonDocument.Parse(responseText);
        var sourceItems = GetTimetableItems(document.RootElement).ToList();
        var trainTimetables = new List<TrainTimetable>(sourceItems.Count);

        foreach (var item in sourceItems)
        {
            var trainInfo = item.TryGetProperty("TrainInfo", out var trainInfoElement)
                ? trainInfoElement
                : item;

            var stopTimes = ReadStopTimes(item, stationMap);

            trainTimetables.Add(new TrainTimetable(
                TrainNo: GetString(trainInfo, "TrainNo") ?? string.Empty,
                Direction: GetInt(trainInfo, "Direction"),
                TrainTypeId: GetString(trainInfo, "TrainTypeID", "TrainTypeId"),
                TrainTypeCode: GetString(trainInfo, "TrainTypeCode"),
                TrainTypeName: GetName(trainInfo, "TrainTypeName"),
                StartingStationId: GetString(trainInfo, "StartingStationID", "StartingStationId"),
                StartingStationName: GetName(trainInfo, "StartingStationName"),
                EndingStationId: GetString(trainInfo, "EndingStationID", "EndingStationId"),
                EndingStationName: GetName(trainInfo, "EndingStationName"),
                StopTimes: stopTimes));
        }

        return new DailyTrainData(
            TrainDate: trainDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Source: "TDX",
            UpdatedAt: updatedAt,
            TrainTimetables: trainTimetables);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(DateOnly trainDate, string token, CancellationToken ct)
    {
        var retryDelays = new[]
        {
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(120)
        };

        for (var attempt = 0; attempt <= retryDelays.Length; attempt++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/{trainDate:yyyy-MM-dd}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _httpClient.SendAsync(request, ct);
            request.Dispose();

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            Console.Error.WriteLine($"TDX request failed for {trainDate:yyyy-MM-dd}: HTTP {(int)response.StatusCode} {response.StatusCode}");

            if ((int)response.StatusCode != 429 || attempt >= retryDelays.Length)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                response.Dispose();

                if (!string.IsNullOrWhiteSpace(errorBody))
                {
                    throw new HttpRequestException($"TDX API returned HTTP {(int)response.StatusCode} {response.StatusCode}. Response: {errorBody}");
                }

                throw new HttpRequestException($"TDX API returned HTTP {(int)response.StatusCode} {response.StatusCode}.");
            }

            var delay = GetRetryDelay(response, retryDelays[attempt]);
            response.Dispose();

            Console.Error.WriteLine($"TDX returned 429 Too Many Requests for {trainDate:yyyy-MM-dd}. Retrying after {delay.TotalSeconds:0} seconds.");
            await Task.Delay(delay, ct);
        }

        throw new InvalidOperationException("Unexpected retry state.");
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, TimeSpan fallbackDelay)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is { } retryAt)
        {
            var delay = retryAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                return delay;
            }
        }

        return fallbackDelay;
    }

    private static IEnumerable<JsonElement> GetTimetableItems(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        foreach (var propertyName in new[] { "TrainTimetables", "DailyTrainTimetables", "data" })
        {
            if (root.TryGetProperty(propertyName, out var arrayElement) && arrayElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arrayElement.EnumerateArray())
                {
                    yield return item;
                }

                yield break;
            }
        }
    }

    private static List<StopTime> ReadStopTimes(JsonElement timetable, IDictionary<string, Station> stationMap)
    {
        var stopTimes = new List<StopTime>();

        if (!timetable.TryGetProperty("StopTimes", out var sourceStopTimes) || sourceStopTimes.ValueKind != JsonValueKind.Array)
        {
            return stopTimes;
        }

        foreach (var stopTime in sourceStopTimes.EnumerateArray())
        {
            var stationId = GetString(stopTime, "StationID", "StationId") ?? string.Empty;
            var stationName = GetName(stopTime, "StationName") ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(stationId) && !stationMap.ContainsKey(stationId))
            {
                stationMap[stationId] = new Station(stationId, stationName);
            }

            stopTimes.Add(new StopTime(
                StationId: stationId,
                StationName: stationName,
                ArrivalTime: GetString(stopTime, "ArrivalTime"),
                DepartureTime: GetString(stopTime, "DepartureTime"),
                StopSequence: GetInt(stopTime, "StopSequence")));
        }

        return stopTimes;
    }

    private static string? GetString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            };
        }

        return null;
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
        {
            return number;
        }

        return null;
    }

    private static string? GetName(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var nameProperty in new[] { "Zh_tw", "ZhTw", "zh_tw", "Name", "En" })
        {
            if (value.TryGetProperty(nameProperty, out var localized) && localized.ValueKind == JsonValueKind.String)
            {
                return localized.GetString();
            }
        }

        return null;
    }
}
