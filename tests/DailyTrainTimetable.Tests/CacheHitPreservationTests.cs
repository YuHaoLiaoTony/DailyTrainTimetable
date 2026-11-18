using System.Globalization;
using System.Text.Json;
using DailyTrainTimetable.Models;
using DailyTrainTimetable.Services;
using NSubstitute;
using Xunit;

namespace DailyTrainTimetable.Tests;

/// <summary>
/// 回歸測試：--days 1（或全部快取命中）不得把 stations.json 洗成空陣列，
/// 也不得讓 latest.json 的 availableDates 從既有清單退化。
/// </summary>
public sealed class CacheHitPreservationTests : IDisposable
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _outputDir;
    private readonly DateTimeOffset _now = new(2026, 6, 1, 9, 0, 0, TimeSpan.FromHours(8));
    private readonly DateOnly _today = new(2026, 6, 1);

    public CacheHitPreservationTests()
    {
        Environment.SetEnvironmentVariable("TDX_CLIENT_ID", "test-client-id");
        Environment.SetEnvironmentVariable("TDX_CLIENT_SECRET", "test-client-secret");
        _outputDir = Path.Combine(Path.GetTempPath(), "dtt-cache-hit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TDX_CLIENT_ID", null);
        Environment.SetEnvironmentVariable("TDX_CLIENT_SECRET", null);
        try
        {
            Directory.Delete(_outputDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task CacheHit_with_days_1_should_preserve_available_dates_and_stations()
    {
        await SeedExistingDataAsync(dayCount: 31);
        await WriteStationsAsync(new Station("1000", "臺北"), new Station("3300", "新竹"));

        var apiService = Substitute.For<ITdxApiService>();
        var (exitCode, latest, stations) = await RunDays1Async(apiService, skipToday: true);

        Assert.Equal(0, exitCode);
        Assert.Equal(31, latest!.AvailableDates.Count);
        Assert.Contains("2026-06-01", latest.AvailableDates);
        Assert.Equal(2, stations!.Count);
        await apiService.DidNotReceiveWithAnyArgs().FetchDailyDataAsync(default, default, default!);
    }

    [Fact]
    public async Task Fetching_new_date_should_union_stations_and_dates()
    {
        await SeedExistingDataAsync(dayCount: 3, startOffset: 1);
        await WriteStationsAsync(new Station("1000", "臺北"));

        var apiService = Substitute.For<ITdxApiService>();
        apiService.FetchDailyDataAsync(
                Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<IDictionary<string, Station>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var stationMap = callInfo.ArgAt<IDictionary<string, Station>>(2);
                if (!stationMap.ContainsKey("9000"))
                {
                    stationMap["9000"] = new Station("9000", "新站");
                }

                return new DailyTrainData(
                    TrainDate: "2026-06-01",
                    Source: "TDX",
                    UpdatedAt: DateTimeOffset.Now,
                    TrainTimetables: new List<TrainTimetable>
                    {
                        new("101", 0, "1100", "1", "自強", "1000", "臺北", "1025", "新竹", Array.Empty<StopTime>())
                    });
            });

        var (exitCode, latest, stations) = await RunDays1Async(apiService, skipToday: false);

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "2026-06-01", "2026-06-02", "2026-06-03", "2026-06-04" }, latest!.AvailableDates);
        Assert.Equal(2, stations!.Count);
        Assert.Contains(stations, s => s.StationId == "1000");
        Assert.Contains(stations, s => s.StationId == "9000");
    }

    [Fact]
    public async Task Available_dates_should_drop_entries_whose_file_no_longer_exists()
    {
        await SeedExistingDataAsync(dayCount: 3);
        File.Delete(Path.Combine(_outputDir, "20260602.json"));

        var apiService = Substitute.For<ITdxApiService>();
        var (exitCode, latest, _) = await RunDays1Async(apiService, skipToday: true);

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "2026-06-01", "2026-06-03" }, latest!.AvailableDates);
    }

    [Fact]
    public async Task Corrupt_existing_files_should_not_break_run()
    {
        await File.WriteAllTextAsync(Path.Combine(_outputDir, "stations.json"), "[ not json");
        await File.WriteAllTextAsync(Path.Combine(_outputDir, "latest.json"), "{ broken");

        var apiService = Substitute.For<ITdxApiService>();
        var (exitCode, latest, stations) = await RunDays1Async(apiService, skipToday: true);

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "2026-06-01" }, latest!.AvailableDates);
        Assert.Empty(stations!);
    }

    private async Task<(int ExitCode, LatestData? Latest, List<Station>? Stations)> RunDays1Async(
        ITdxApiService apiService,
        bool skipToday)
    {
        var cacheService = Substitute.For<IDataCacheService>();
        cacheService.EvaluateAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<DateOnly>>(), Arg.Any<CacheEvaluationOptions?>())
            .Returns(new CacheResult(
                SkipDates: skipToday ? new HashSet<DateOnly> { _today } : new HashSet<DateOnly>(),
                FetchDates: skipToday ? new HashSet<DateOnly>() : new HashSet<DateOnly> { _today },
                ForceFetchDates: new HashSet<DateOnly>()));

        var exitCode = await Program.RunCoreAsync(
            days: 1,
            now: _now,
            outputDir: _outputDir,
            authService: Substitute.For<ITdxAuthService>(),
            apiService: apiService,
            cacheService: cacheService,
            writerService: new DataWriterService(),
            existingDataService: new ExistingDataService());

        var latest = await ReadJsonAsync<LatestData>("latest.json");
        var stations = await ReadJsonAsync<List<Station>>("stations.json");
        return (exitCode, latest, stations);
    }

    private async Task SeedExistingDataAsync(int dayCount, int startOffset = 0)
    {
        var dates = Enumerable.Range(startOffset, dayCount)
            .Select(offset => _today.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToList();

        foreach (var date in dates)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_outputDir, date.Replace("-", "") + ".json"),
                "{\"trainDate\":\"" + date + "\",\"trainTimetables\":[]}");
        }

        var latest = new LatestData(_now, dates, "1");
        await File.WriteAllTextAsync(
            Path.Combine(_outputDir, "latest.json"),
            JsonSerializer.Serialize(latest, WriteOptions));
    }

    private async Task WriteStationsAsync(params Station[] stations)
    {
        await File.WriteAllTextAsync(
            Path.Combine(_outputDir, "stations.json"),
            JsonSerializer.Serialize(stations, WriteOptions));
    }

    private async Task<T?> ReadJsonAsync<T>(string fileName)
    {
        var json = await File.ReadAllTextAsync(Path.Combine(_outputDir, fileName));
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        });
    }
}
