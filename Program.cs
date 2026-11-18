using System.Globalization;
using DailyTrainTimetable.Models;
using DailyTrainTimetable.Services;

namespace DailyTrainTimetable;

public static class Program
{
    /// <summary>
    /// 目前程式產出的資料版本；用於快取有效性比對（與 latest.json 的 dataVersion 比對）。
    /// 資料 schema 有不相容變更時才需調整。
    /// </summary>
    internal const string DataVersion = "1";

    private const string CliUsage =
        "Usage: dotnet run -- [--days <n>] [--force | --refresh-dates <yyyy-MM-dd[,yyyy-MM-dd...]>]";

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

    internal static async Task<int> RunAsync(
        string[] args,
        ITdxAuthService? authServiceOverride = null,
        ITdxApiService? apiServiceOverride = null,
        IDataCacheService? cacheServiceOverride = null,
        IDataWriterService? writerServiceOverride = null,
        IExistingDataService? existingDataServiceOverride = null)
    {
        var days = ParseDays(args);
        var forceSelection = ParseForceSelection(args);
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

        if (authServiceOverride != null)
        {
            return await RunCoreAsync(
                days, now, outputDir,
                authServiceOverride,
                apiServiceOverride!,
                cacheServiceOverride!,
                writerServiceOverride!,
                existingDataServiceOverride ?? new ExistingDataService(),
                forceSelection);
        }

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DailyTrainTimetable/1.0");

        var authService = new TdxAuthService(httpClient, clientId, clientSecret);
        var apiService = new TdxApiService(httpClient, authService);
        var pagesUri = GetPagesLatestUri();
        var cacheService = new DataCacheService(httpClient, pagesUri);
        var writerService = new DataWriterService();
        var existingDataService = existingDataServiceOverride ?? new ExistingDataService();

        return await RunCoreAsync(
            days, now, outputDir, authService, apiService, cacheService, writerService, existingDataService, forceSelection);
    }

    internal static async Task<int> RunCoreAsync(
        int days,
        DateTimeOffset now,
        string outputDir,
        ITdxAuthService authService,
        ITdxApiService apiService,
        IDataCacheService cacheService,
        IDataWriterService writerService,
        IExistingDataService existingDataService,
        ForceFetchSelection? forceSelection = null)
    {
        var stationMap = new SortedDictionary<string, Station>(StringComparer.Ordinal);
        var successfulDates = new List<DateOnly>();
        var failedDates = new List<DateOnly>();

        // 先載入既有 stations.json 與 latest.json，避免本次只處理少量日期（或全部快取命中）時
        // 把站點清單與可用日期清單覆寫成退化版本。
        var existingStations = await existingDataService.TryReadStationsAsync(outputDir);
        foreach (var station in existingStations)
        {
            if (!string.IsNullOrWhiteSpace(station.StationId))
            {
                stationMap[station.StationId] = station;
            }
        }

        var previousLatest = await existingDataService.TryReadLatestAsync(outputDir);
        var startDate = DateOnly.FromDateTime(now.DateTime);
        var requestedDates = Enumerable.Range(0, days).Select(offset => startDate.AddDays(offset)).ToList();

        var forcedDates = ResolveForcedDates(forceSelection, requestedDates);
        var cacheOptions = new CacheEvaluationOptions(DataVersion, forcedDates);
        var cacheResult = await cacheService.EvaluateAsync(outputDir, requestedDates, cacheOptions);

        var datesToFetch = cacheResult.FetchDates
            .Concat(cacheResult.ForceFetchDates)
            .Distinct()
            .OrderBy(date => date)
            .ToList();

        foreach (var date in datesToFetch)
        {
            var succeeded = false;
            var forced = cacheResult.ForceFetchDates.Contains(date);

            try
            {
                Console.WriteLine(forced
                    ? $"Force re-fetching {date:yyyy-MM-dd} (cache bypassed)..."
                    : $"Fetching {date:yyyy-MM-dd}...");
                var dailyData = await apiService.FetchDailyDataAsync(date, now, stationMap);

                if (IsUnusableResult(dailyData))
                {
                    // TDX 回 200 但沒有可用班次：視為失敗，不寫日期 JSON、不進 successfulDates，
                    // 讓該日期下次執行時重新抓取，避免把空檔永久快取。
                    //
                    // 合法情境的區分方式：TDX 目前對「該日無營運（例如全面停駛）」與「資料尚未產生」
                    // 都只回空陣列，沒有任何可辨識的欄位可以區分類，因此無法安全地只寫「合法的空檔」。
                    // 這裡一律不寫檔（寧可下次重抓，也不要留下永久空資料）；若日後 TDX 提供明確的
                    // no-service 標記，再於此處放行該情境寫檔。
                    Console.Error.WriteLine(
                        $"TDX returned empty result for {date:yyyy-MM-dd} ({DescribeUnusableResult(dailyData)}); empty result, not cached.");
                }
                else
                {
                    await writerService.WriteDailyDataAsync(dailyData, outputDir);
                    successfulDates.Add(date);
                    succeeded = true;
                    Console.WriteLine($"Generated {outputDir}{Path.DirectorySeparatorChar}{date:yyyyMMdd}.json");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to generate data for {date:yyyy-MM-dd}: {ex}");
            }

            if (!succeeded)
            {
                failedDates.Add(date);
            }

            if (succeeded)
            {
                Console.WriteLine("Waiting 10 seconds before the next TDX request...");
                await Task.Delay(TimeSpan.FromSeconds(10));
            }
        }

        foreach (var date in cacheResult.SkipDates)
        {
            successfulDates.Add(date);
        }

        if (forcedDates.Count > 0)
        {
            Console.WriteLine(
                $"Force re-fetched {forcedDates.Count} date(s) ignoring cache: " +
                string.Join(", ", forcedDates.OrderBy(d => d).Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));
        }

        var latest = new LatestData(
            UpdatedAt: now,
            AvailableDates: MergeAvailableDates(outputDir, previousLatest, successfulDates, existingDataService),
            DataVersion: DataVersion);

        await writerService.WriteLatestAsync(latest, outputDir);
        await writerService.WriteStationsAsync(stationMap.Values.ToList(), outputDir);

        Console.WriteLine($"Done. Successful dates: {successfulDates.Count}/{days}");
        if (failedDates.Count > 0)
        {
            Console.Error.WriteLine(
                $"Failed dates ({failedDates.Count}): {string.Join(", ", failedDates.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))}");
        }

        if (successfulDates.Count < days)
        {
            Console.Error.WriteLine($"Generated data is incomplete. Requested {days} days but only {successfulDates.Count} succeeded.");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// 判斷一次 TDX 回應是否「不可用」（不能寫入快取）。
    /// 不可用 = 班次集合為空，或所有班次都缺少必要欄位（TrainNo）。
    /// </summary>
    internal static bool IsUnusableResult(DailyTrainData data)
    {
        if (data.TrainTimetables == null || data.TrainTimetables.Count == 0)
        {
            return true;
        }

        return data.TrainTimetables.All(t => string.IsNullOrWhiteSpace(t.TrainNo));
    }

    private static string DescribeUnusableResult(DailyTrainData data)
        => data.TrainTimetables == null || data.TrainTimetables.Count == 0
            ? "0 timetables"
            : $"{data.TrainTimetables.Count} timetables without TrainNo";

    /// <summary>
    /// 本次結果與既有 latest.json 的聯集；既有項目只在對應日期檔仍存在時保留，
    /// 避免已移除的日期殘留在清單中。
    /// </summary>
    internal static List<string> MergeAvailableDates(
        string outputDir,
        LatestData? previousLatest,
        IEnumerable<DateOnly> successfulDates,
        IExistingDataService existingDataService)
    {
        var merged = new SortedSet<DateOnly>();

        foreach (var date in successfulDates)
        {
            merged.Add(date);
        }

        if (previousLatest?.AvailableDates != null)
        {
            foreach (var text in previousLatest.AvailableDates)
            {
                if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    continue;
                }

                if (existingDataService.DateFileExists(outputDir, date))
                {
                    merged.Add(date);
                }
            }
        }

        return merged.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToList();
    }

    internal static int ParseDays(string[] args)
    {
        const int defaultDays = 1;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] != "--days")
            {
                continue;
            }

            if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out var days) || days <= 0)
            {
                throw new ArgumentException(CliUsage);
            }

            return days;
        }

        return defaultDays;
    }

    /// <summary>
    /// 解析無視快取重抓的 CLI 開關：<c>--force</c> 代表本次要求的全部日期，
    /// <c>--refresh-dates yyyy-MM-dd[,yyyy-MM-dd...]</c> 代表指定日期。兩者同時出現時以 <c>--force</c> 為準。
    /// </summary>
    internal static ForceFetchSelection ParseForceSelection(string[] args)
    {
        var allDates = false;
        var dates = new HashSet<DateOnly>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--force":
                    allDates = true;
                    break;

                case "--refresh-dates":
                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException(CliUsage);
                    }

                    foreach (var text in args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        {
                            throw new ArgumentException($"Invalid --refresh-dates value '{text}'. {CliUsage}");
                        }

                        dates.Add(date);
                    }

                    i++;
                    break;
            }
        }

        return new ForceFetchSelection(allDates, dates);
    }

    /// <summary>
    /// 把 CLI 的強制重抓選擇收斂成本次要求的日期集合；範圍外的日期會被忽略（並記錄警告），
    /// 因為本次執行只處理 <c>--days</c> 所指的日期窗口。
    /// </summary>
    internal static IReadOnlySet<DateOnly> ResolveForcedDates(
        ForceFetchSelection? forceSelection,
        IReadOnlyList<DateOnly> requestedDates)
    {
        if (forceSelection == null || forceSelection.IsEmpty)
        {
            return new HashSet<DateOnly>();
        }

        if (forceSelection.AllDates)
        {
            return new HashSet<DateOnly>(requestedDates);
        }

        var requested = new HashSet<DateOnly>(requestedDates);
        var result = new HashSet<DateOnly>();

        foreach (var date in forceSelection.Dates)
        {
            if (requested.Contains(date))
            {
                result.Add(date);
            }
            else
            {
                Console.Error.WriteLine(
                    $"Warning: --refresh-dates {date:yyyy-MM-dd} is outside the requested window " +
                    $"({requestedDates[0]:yyyy-MM-dd}..{requestedDates[^1]:yyyy-MM-dd}) and is ignored.");
            }
        }

        return result;
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

    /// <summary>
    /// CLI 的強制重抓選擇：<see cref="AllDates"/> 代表全部要求的日期，
    /// <see cref="Dates"/> 代表 <c>--refresh-dates</c> 指定的日期。
    /// </summary>
    internal sealed record ForceFetchSelection(bool AllDates, IReadOnlySet<DateOnly> Dates)
    {
        public bool IsEmpty => !AllDates && Dates.Count == 0;
    }
}
