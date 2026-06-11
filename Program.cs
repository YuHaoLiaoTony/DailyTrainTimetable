using System.Globalization;
using DailyTrainTimetable.Models;
using DailyTrainTimetable.Services;

namespace DailyTrainTimetable;

public static class Program
{
    private const string DataVersion = "1";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAsync(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unhandled exception: {ex}");
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
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

        var authService = new TdxAuthService(httpClient, clientId, clientSecret);
        var apiService = new TdxApiService(httpClient, authService);

        var pagesUri = GetPagesLatestUri();
        var cacheService = new DataCacheService(httpClient, pagesUri);
        var writerService = new DataWriterService();

        var stationMap = new SortedDictionary<string, Station>(StringComparer.Ordinal);
        var successfulDates = new List<DateOnly>();
        var startDate = DateOnly.FromDateTime(now.DateTime);
        var requestedDates = Enumerable.Range(0, days).Select(offset => startDate.AddDays(offset)).ToList();

        var cacheResult = await cacheService.EvaluateAsync(outputDir, requestedDates);

        foreach (var date in cacheResult.FetchDates)
        {
            try
            {
                Console.WriteLine($"Fetching {date:yyyy-MM-dd}...");
                var dailyData = await apiService.FetchDailyDataAsync(date, now, stationMap);

                if (dailyData.TrainTimetables.Count == 0)
                {
                    Console.Error.WriteLine($"TDX returned empty timetable data for {date:yyyy-MM-dd}.");
                }

                await writerService.WriteDailyDataAsync(dailyData, outputDir);
                successfulDates.Add(date);
                Console.WriteLine($"Generated {outputDir}{Path.DirectorySeparatorChar}{date:yyyyMMdd}.json");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to generate data for {date:yyyy-MM-dd}: {ex}");
            }

            if (successfulDates.Count > 0 && successfulDates[^1] == date)
            {
                Console.WriteLine("Waiting 10 seconds before the next TDX request...");
                await Task.Delay(TimeSpan.FromSeconds(10));
            }
        }

        foreach (var date in cacheResult.SkipDates)
        {
            successfulDates.Add(date);
        }

        var latest = new LatestData(
            UpdatedAt: now,
            AvailableDates: successfulDates.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToList(),
            DataVersion: DataVersion);

        await writerService.WriteLatestAsync(latest, outputDir);
        await writerService.WriteStationsAsync(stationMap.Values.ToList(), outputDir);

        Console.WriteLine($"Done. Successful dates: {successfulDates.Count}/{days}");
        if (successfulDates.Count < days)
        {
            Console.Error.WriteLine($"Generated data is incomplete. Requested {days} days but only {successfulDates.Count} succeeded.");
            return 1;
        }

        return 0;
    }

    internal static int ParseDays(string[] args)
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

    internal static TimeZoneInfo GetTaipeiTimeZone()
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

    private static Uri? GetPagesLatestUri()
    {
        var pagesUrl = Environment.GetEnvironmentVariable("PAGES_BASE_URL");
        if (string.IsNullOrWhiteSpace(pagesUrl))
        {
            return null;
        }

        var baseUri = pagesUrl.TrimEnd('/');
        return new Uri($"{baseUri}/data/latest.json");
    }
}
