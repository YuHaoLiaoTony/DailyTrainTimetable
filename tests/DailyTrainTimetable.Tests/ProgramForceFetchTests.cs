using DailyTrainTimetable.Models;
using DailyTrainTimetable.Services;
using NSubstitute;
using Xunit;

namespace DailyTrainTimetable.Tests;

/// <summary>
/// 無視快取重抓（--force / --refresh-dates）的 CLI 解析與 Program 串接測試。
/// </summary>
public sealed class ProgramForceFetchTests : IDisposable
{
    private readonly string _outputDir;

    public ProgramForceFetchTests()
    {
        Environment.SetEnvironmentVariable("TDX_CLIENT_ID", "test-client-id");
        Environment.SetEnvironmentVariable("TDX_CLIENT_SECRET", "test-client-secret");
        _outputDir = Path.Combine(Path.GetTempPath(), "dtt-force-fetch-" + Guid.NewGuid().ToString("N"));
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
    public void ParseForceSelection_should_return_empty_without_flags()
    {
        var selection = Program.ParseForceSelection(["--days", "7"]);

        Assert.True(selection.IsEmpty);
        Assert.False(selection.AllDates);
        Assert.Empty(selection.Dates);
    }

    [Fact]
    public void ParseForceSelection_should_enable_force_all()
    {
        var selection = Program.ParseForceSelection(["--force"]);

        Assert.True(selection.AllDates);
        Assert.True(selection.IsEmpty is false);
    }

    [Fact]
    public void ParseForceSelection_should_parse_refresh_dates_list()
    {
        var selection = Program.ParseForceSelection(["--refresh-dates", "2026-11-17,2026-11-18"]);

        Assert.False(selection.AllDates);
        Assert.Equal(2, selection.Dates.Count);
        Assert.Contains(new DateOnly(2026, 11, 17), selection.Dates);
        Assert.Contains(new DateOnly(2026, 11, 18), selection.Dates);
    }

    [Fact]
    public void ParseForceSelection_should_throw_on_invalid_refresh_date()
    {
        Assert.Throws<ArgumentException>(() => Program.ParseForceSelection(["--refresh-dates", "2026-13-01"]));
        Assert.Throws<ArgumentException>(() => Program.ParseForceSelection(["--refresh-dates"]));
    }

    [Fact]
    public void ResolveForcedDates_should_cover_whole_window_when_force()
    {
        var requested = new List<DateOnly> { new(2026, 11, 17), new(2026, 11, 18) };
        var selection = new Program.ForceFetchSelection(true, new HashSet<DateOnly>());

        var forced = Program.ResolveForcedDates(selection, requested);

        Assert.Equal(2, forced.Count);
    }

    [Fact]
    public void ResolveForcedDates_should_ignore_dates_outside_window()
    {
        var requested = new List<DateOnly> { new(2026, 11, 17), new(2026, 11, 18) };
        var selection = new Program.ForceFetchSelection(
            false, new HashSet<DateOnly> { new(2026, 11, 17), new(2026, 1, 1) });

        var forced = Program.ResolveForcedDates(selection, requested);

        Assert.Single(forced);
        Assert.Contains(new DateOnly(2026, 11, 17), forced);
    }

    [Fact]
    public async Task RunAsync_should_pass_force_dates_to_cache_service()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.Now.Date);

        var cacheService = Substitute.For<IDataCacheService>();
        CacheEvaluationOptions? captured = null;
        cacheService
            .EvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<DateOnly>>(),
                Arg.Do<CacheEvaluationOptions?>(options => captured = options))
            .Returns(new CacheResult(
                SkipDates: new HashSet<DateOnly>(),
                FetchDates: new HashSet<DateOnly>(),
                ForceFetchDates: new HashSet<DateOnly> { today }));

        var apiService = Substitute.For<ITdxApiService>();
        apiService.FetchDailyDataAsync(
                Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<IDictionary<string, Station>>(), Arg.Any<CancellationToken>())
            .Returns(new DailyTrainData(
                TrainDate: today.ToString("yyyy-MM-dd"),
                Source: "TDX",
                UpdatedAt: DateTimeOffset.Now,
                TrainTimetables: new List<TrainTimetable>
                {
                    new("101", 0, "1100", "1", "自強", "1000", "臺北", "1025", "新竹", Array.Empty<StopTime>())
                }));

        var exitCode = await Program.RunAsync(
            ["--days", "1", "--force"],
            Substitute.For<ITdxAuthService>(),
            apiService,
            cacheService,
            Substitute.For<IDataWriterService>());

        Assert.Equal(0, exitCode);
        Assert.NotNull(captured);
        Assert.Equal(Program.DataVersion, captured!.CurrentDataVersion);
        Assert.Contains(today, captured.ForceFetchDates!);
        await apiService.Received(1).FetchDailyDataAsync(
            Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset>(),
            Arg.Any<IDictionary<string, Station>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_should_fetch_dates_that_cache_marks_as_forced()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.Now.Date);
        var tomorrow = today.AddDays(1);

        var cacheService = Substitute.For<IDataCacheService>();
        cacheService
            .EvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<DateOnly>>(),
                Arg.Any<CacheEvaluationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(new CacheResult(
                SkipDates: new HashSet<DateOnly> { today },
                FetchDates: new HashSet<DateOnly>(),
                ForceFetchDates: new HashSet<DateOnly> { tomorrow }));

        var apiService = Substitute.For<ITdxApiService>();
        apiService.FetchDailyDataAsync(
                Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<IDictionary<string, Station>>(), Arg.Any<CancellationToken>())
            .Returns(new DailyTrainData(
                TrainDate: tomorrow.ToString("yyyy-MM-dd"),
                Source: "TDX",
                UpdatedAt: DateTimeOffset.Now,
                TrainTimetables: new List<TrainTimetable>
                {
                    new("101", 0, "1100", "1", "自強", "1000", "臺北", "1025", "新竹", Array.Empty<StopTime>())
                }));

        var exitCode = await Program.RunAsync(
            ["--days", "2"],
            Substitute.For<ITdxAuthService>(),
            apiService,
            cacheService,
            Substitute.For<IDataWriterService>());

        Assert.Equal(0, exitCode);
        await apiService.Received(1).FetchDailyDataAsync(
            Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset>(),
            Arg.Any<IDictionary<string, Station>>(), Arg.Any<CancellationToken>());
    }
}
