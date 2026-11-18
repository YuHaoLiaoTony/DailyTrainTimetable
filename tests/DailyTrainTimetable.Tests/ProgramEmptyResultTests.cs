using DailyTrainTimetable.Models;
using DailyTrainTimetable.Services;
using NSubstitute;
using Xunit;

namespace DailyTrainTimetable.Tests;

/// <summary>
/// TDX 回 200 但沒有可用班次時，必須視為失敗：不寫日期 JSON、不進 latest.json.availableDates、exit code 非 0。
/// </summary>
public sealed class ProgramEmptyResultTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 9, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateOnly Today = new(2026, 6, 1);

    private readonly string _outputDir;

    public ProgramEmptyResultTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "dtt-empty-result-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_outputDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task RunCoreAsync_should_not_cache_when_timetables_are_empty()
    {
        var apiService = Substitute.For<ITdxApiService>();
        apiService.FetchDailyDataAsync(
                Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<IDictionary<string, Station>>(), Arg.Any<CancellationToken>())
            .Returns(EmptyDailyData(Today));

        var writerService = Substitute.For<IDataWriterService>();
        var exitCode = await RunCoreAsync(apiService, writerService, new HashSet<DateOnly> { Today }, days: 1);

        Assert.Equal(1, exitCode);
        await writerService.DidNotReceiveWithAnyArgs().WriteDailyDataAsync(default!, default!);
        Assert.False(File.Exists(Path.Combine(_outputDir, "20260601.json")));

        // latest.json 仍會寫（供下游得知更新時間），但不可宣告這個失敗日期。
        await writerService.Received(1).WriteLatestAsync(
            Arg.Is<LatestData>(latest => latest.AvailableDates.Count == 0), _outputDir);
    }

    [Fact]
    public async Task RunCoreAsync_should_not_cache_when_timetables_lack_train_no()
    {
        var apiService = Substitute.For<ITdxApiService>();
        apiService.FetchDailyDataAsync(
                Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<IDictionary<string, Station>>(), Arg.Any<CancellationToken>())
            .Returns(new DailyTrainData(
                TrainDate: Today.ToString("yyyy-MM-dd"),
                Source: "TDX",
                UpdatedAt: Now,
                TrainTimetables: new List<TrainTimetable>
                {
                    new(string.Empty, null, null, null, null, null, null, null, null, Array.Empty<StopTime>())
                }));

        var writerService = Substitute.For<IDataWriterService>();
        var exitCode = await RunCoreAsync(apiService, writerService, new HashSet<DateOnly> { Today }, days: 1);

        Assert.Equal(1, exitCode);
        await writerService.DidNotReceiveWithAnyArgs().WriteDailyDataAsync(default!, default!);
    }

    [Fact]
    public async Task RunCoreAsync_should_count_empty_result_as_failure_alongside_cached_dates()
    {
        var apiService = Substitute.For<ITdxApiService>();
        apiService.FetchDailyDataAsync(
                Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<IDictionary<string, Station>>(), Arg.Any<CancellationToken>())
            .Returns(EmptyDailyData(Today.AddDays(1)));

        var writerService = Substitute.For<IDataWriterService>();
        var exitCode = await RunCoreAsync(
            apiService,
            writerService,
            fetchDates: new HashSet<DateOnly> { Today.AddDays(1) },
            days: 2,
            skipDates: new HashSet<DateOnly> { Today });

        Assert.Equal(1, exitCode);
        await writerService.DidNotReceiveWithAnyArgs().WriteDailyDataAsync(default!, default!);
        await writerService.Received(1).WriteLatestAsync(
            Arg.Is<LatestData>(latest => latest.AvailableDates.Count == 1 && latest.AvailableDates[0] == "2026-06-01"),
            _outputDir);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void IsUnusableResult_should_classify_by_usable_timetables(int timetableCount, bool expected)
    {
        var data = new DailyTrainData(
            TrainDate: "2026-06-01",
            Source: "TDX",
            UpdatedAt: Now,
            TrainTimetables: Enumerable.Range(0, timetableCount)
                .Select(i => new TrainTimetable(
                    $"10{i}", 0, "1100", "1", "自強", "1000", "臺北", "1025", "新竹", Array.Empty<StopTime>()))
                .ToList());

        Assert.Equal(expected, Program.IsUnusableResult(data));
    }

    [Fact]
    public void IsUnusableResult_should_treat_timetables_without_train_no_as_unusable()
    {
        var data = new DailyTrainData(
            TrainDate: "2026-06-01",
            Source: "TDX",
            UpdatedAt: Now,
            TrainTimetables: new List<TrainTimetable>
            {
                new(string.Empty, 0, null, null, null, null, null, null, null, Array.Empty<StopTime>()),
                new("  ", 0, null, null, null, null, null, null, null, Array.Empty<StopTime>())
            });

        Assert.True(Program.IsUnusableResult(data));
    }

    private static DailyTrainData EmptyDailyData(DateOnly date)
        => new(
            TrainDate: date.ToString("yyyy-MM-dd"),
            Source: "TDX",
            UpdatedAt: Now,
            TrainTimetables: new List<TrainTimetable>());

    private async Task<int> RunCoreAsync(
        ITdxApiService apiService,
        IDataWriterService writerService,
        HashSet<DateOnly> fetchDates,
        int days,
        HashSet<DateOnly>? skipDates = null)
    {
        var cacheService = Substitute.For<IDataCacheService>();
        cacheService.EvaluateAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<DateOnly>>(), Arg.Any<CacheEvaluationOptions?>())
            .Returns(new CacheResult(
                SkipDates: skipDates ?? new HashSet<DateOnly>(),
                FetchDates: fetchDates,
                ForceFetchDates: new HashSet<DateOnly>()));

        return await Program.RunCoreAsync(
            days: days,
            now: Now,
            outputDir: _outputDir,
            authService: Substitute.For<ITdxAuthService>(),
            apiService: apiService,
            cacheService: cacheService,
            writerService: writerService,
            existingDataService: new ExistingDataService());
    }
}
