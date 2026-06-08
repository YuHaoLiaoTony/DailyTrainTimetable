using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

const string ApiBaseUrl = "https://tdx.transportdata.tw/api/basic/v3/Rail/TRA/DailyTrainTimetable/TrainDate";
const string TokenUrl = "https://tdx.transportdata.tw/auth/realms/TDXConnect/protocol/openid-connect/token";
const string DataVersion = "1";

var days = ParseDays(args);
var clientId = Environment.GetEnvironmentVariable("TDX_CLIENT_ID");
var clientSecret = Environment.GetEnvironmentVariable("TDX_CLIENT_SECRET");

if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
{
    Console.Error.WriteLine("Missing TDX credentials. Please set TDX_CLIENT_ID and TDX_CLIENT_SECRET.");
    return 1;
}

var taipeiTimeZone = GetTaipeiTimeZone();
var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, taipeiTimeZone);
var outputDir = Path.Combine("output", "data");
Directory.CreateDirectory(outputDir);

using var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DailyTrainTimetable/1.0");

var accessToken = await GetAccessTokenAsync(httpClient, clientId, clientSecret);
httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

var stationMap = new SortedDictionary<string, Station>(StringComparer.Ordinal);
var successfulDates = new List<string>();
var startDate = DateOnly.FromDateTime(now.DateTime);

for (var offset = 0; offset < days; offset++)
{
    var trainDate = startDate.AddDays(offset);
    var trainDateText = trainDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    try
    {
        Console.WriteLine($"Fetching {trainDateText}...");
        var dailyData = await FetchDailyDataAsync(httpClient, trainDateText, now, stationMap);

        if (dailyData.TrainTimetables.Count == 0)
        {
            Console.Error.WriteLine($"TDX returned empty timetable data for {trainDateText}.");
        }

        var fileName = trainDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".json";
        var outputPath = Path.Combine(outputDir, fileName);
        await WriteJsonAtomicallyAsync(outputPath, dailyData, options);
        successfulDates.Add(trainDateText);
        Console.WriteLine($"Generated {outputPath}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to generate data for {trainDateText}: {ex.Message}");
    }

    if (successfulDates.Count > 0 && successfulDates[^1] == trainDateText && offset < days - 1)
    {
        Console.WriteLine("Waiting 10 seconds before the next TDX request...");
        await Task.Delay(TimeSpan.FromSeconds(10));
    }
}

var latest = new LatestData(
    UpdatedAt: now,
    AvailableDates: successfulDates,
    DataVersion: DataVersion);

await WriteJsonAtomicallyAsync(Path.Combine(outputDir, "latest.json"), latest, options);
await WriteJsonAtomicallyAsync(Path.Combine(outputDir, "stations.json"), stationMap.Values.ToList(), options);

Console.WriteLine($"Done. Successful dates: {successfulDates.Count}/{days}");
if (successfulDates.Count < days)
{
    Console.Error.WriteLine($"Generated data is incomplete. Requested {days} days but only {successfulDates.Count} succeeded.");
    return 1;
}

return 0;

static int ParseDays(string[] args)
{
    const int defaultDays = 7;

    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] != "--days")
        {
            continue;
        }

        if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out var days) || days <= 0)
        {
            throw new ArgumentException("Usage: dotnet run -- --days 14");
        }

        return days;
    }

    return defaultDays;
}

static TimeZoneInfo GetTaipeiTimeZone()
{
    foreach (var id in new[] { "Asia/Taipei", "Taipei Standard Time" })
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }
    }

    throw new InvalidOperationException("Could not find Asia/Taipei time zone.");
}

static async Task<string> GetAccessTokenAsync(HttpClient httpClient, string clientId, string clientSecret)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
    {
        Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        })
    };

    using var response = await httpClient.SendAsync(request);
    var responseText = await response.Content.ReadAsStringAsync();
    response.EnsureSuccessStatusCode();

    using var document = JsonDocument.Parse(responseText);
    if (!document.RootElement.TryGetProperty("access_token", out var accessTokenElement))
    {
        throw new InvalidOperationException("TDX token response did not contain access_token.");
    }

    return accessTokenElement.GetString() ?? throw new InvalidOperationException("TDX access_token was empty.");
}

static async Task<DailyTrainData> FetchDailyDataAsync(
    HttpClient httpClient,
    string trainDate,
    DateTimeOffset updatedAt,
    IDictionary<string, Station> stationMap)
{
    using var response = await SendDailyTimetableRequestWithRetryAsync(httpClient, trainDate);
    var responseText = await response.Content.ReadAsStringAsync();

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
        TrainDate: trainDate,
        Source: "TDX",
        UpdatedAt: updatedAt,
        TrainTimetables: trainTimetables);
}

static async Task<HttpResponseMessage> SendDailyTimetableRequestWithRetryAsync(HttpClient httpClient, string trainDate)
{
    var retryDelays = new[]
    {
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(120)
    };

    for (var attempt = 0; attempt <= retryDelays.Length; attempt++)
    {
        var response = await httpClient.GetAsync($"{ApiBaseUrl}/{trainDate}");
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        Console.Error.WriteLine($"TDX request failed for {trainDate}: HTTP {(int)response.StatusCode} {response.StatusCode}");

        if ((int)response.StatusCode != 429 || attempt >= retryDelays.Length)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            response.Dispose();

            if (!string.IsNullOrWhiteSpace(errorBody))
            {
                throw new HttpRequestException($"TDX API returned HTTP {(int)response.StatusCode} {response.StatusCode}. Response: {errorBody}");
            }

            throw new HttpRequestException($"TDX API returned HTTP {(int)response.StatusCode} {response.StatusCode}.");
        }

        var delay = GetRetryDelay(response, retryDelays[attempt]);
        response.Dispose();

        Console.Error.WriteLine($"TDX returned 429 Too Many Requests for {trainDate}. Retrying after {delay.TotalSeconds:0} seconds.");
        await Task.Delay(delay);
    }

    throw new InvalidOperationException("Unexpected retry state.");
}

static TimeSpan GetRetryDelay(HttpResponseMessage response, TimeSpan fallbackDelay)
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

static IEnumerable<JsonElement> GetTimetableItems(JsonElement root)
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

static List<StopTime> ReadStopTimes(JsonElement timetable, IDictionary<string, Station> stationMap)
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

static string? GetString(JsonElement element, params string[] propertyNames)
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

static int? GetInt(JsonElement element, string propertyName)
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

static string? GetName(JsonElement element, string propertyName)
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

static async Task WriteJsonAtomicallyAsync<T>(string path, T value, JsonSerializerOptions options)
{
    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    var tempPath = path + ".tmp";
    await using (var stream = File.Create(tempPath))
    {
        await JsonSerializer.SerializeAsync(stream, value, options);
        await stream.WriteAsync("\n"u8.ToArray());
    }

    File.Move(tempPath, path, overwrite: true);
}

public sealed record DailyTrainData(
    string TrainDate,
    string Source,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TrainTimetable> TrainTimetables);

public sealed record TrainTimetable(
    string TrainNo,
    int? Direction,
    string? TrainTypeId,
    string? TrainTypeCode,
    string? TrainTypeName,
    string? StartingStationId,
    string? StartingStationName,
    string? EndingStationId,
    string? EndingStationName,
    IReadOnlyList<StopTime> StopTimes);

public sealed record StopTime(
    string StationId,
    string StationName,
    string? ArrivalTime,
    string? DepartureTime,
    int? StopSequence);

public sealed record Station(
    string StationId,
    string StationName);

public sealed record LatestData(
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> AvailableDates,
    string DataVersion);
