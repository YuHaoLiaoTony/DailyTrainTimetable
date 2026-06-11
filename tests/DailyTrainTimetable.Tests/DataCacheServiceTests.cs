using DailyTrainTimetable.Models;
using DailyTrainTimetable.Services;
using NSubstitute;
using Xunit;

namespace DailyTrainTimetable.Tests;

public sealed class DataCacheServiceTests
{
    private readonly IDataCacheService _sut;

    public DataCacheServiceTests()
    {
        _sut = Substitute.For<IDataCacheService>();
    }

    [Fact]
    public async Task EvaluateAsync_should_skip_dates_when_all_files_exist_and_Pages_has_no_updates()
    {
        var requested = new List<DateOnly> { new(2026, 6, 1), new(2026, 6, 2) };
        var result = new CacheResult(
            SkipDates: new HashSet<DateOnly> { new(2026, 6, 1), new(2026, 6, 2) },
            FetchDates: new HashSet<DateOnly>(),
            ForceFetchDates: new HashSet<DateOnly>());

        _sut.EvaluateAsync(default!, requested, default)
           .ReturnsForAnyArgs(Task.FromResult(result));

        var actual = await _sut.EvaluateAsync("output/data", requested);

        Assert.Equal(2, actual.SkipDates.Count);
        Assert.Empty(actual.FetchDates);
        Assert.Empty(actual.ForceFetchDates);
    }

    [Fact]
    public async Task EvaluateAsync_should_fetch_missing_dates()
    {
        var requested = new List<DateOnly> { new(2026, 6, 1), new(2026, 6, 2), new(2026, 6, 3) };
        var result = new CacheResult(
            SkipDates: new HashSet<DateOnly> { new(2026, 6, 1) },
            FetchDates: new HashSet<DateOnly> { new(2026, 6, 2), new(2026, 6, 3) },
            ForceFetchDates: new HashSet<DateOnly>());

        _sut.EvaluateAsync(default!, requested, default)
           .ReturnsForAnyArgs(Task.FromResult(result));

        var actual = await _sut.EvaluateAsync("output/data", requested);

        Assert.Single(actual.SkipDates);
        Assert.Equal(2, actual.FetchDates.Count);
        Assert.Empty(actual.ForceFetchDates);
    }

    [Fact]
    public async Task EvaluateAsync_should_force_fetch_when_Pages_version_is_newer()
    {
        var requested = new List<DateOnly> { new(2026, 6, 1) };
        var result = new CacheResult(
            SkipDates: new HashSet<DateOnly>(),
            FetchDates: new HashSet<DateOnly>(),
            ForceFetchDates: new HashSet<DateOnly> { new(2026, 6, 1) });

        _sut.EvaluateAsync(default!, requested, default)
           .ReturnsForAnyArgs(Task.FromResult(result));

        var actual = await _sut.EvaluateAsync("output/data", requested);

        Assert.Empty(actual.SkipDates);
        Assert.Empty(actual.FetchDates);
        Assert.Single(actual.ForceFetchDates);
    }

    [Fact]
    public async Task EvaluateAsync_should_fallback_to_file_only_check_when_Pages_unavailable()
    {
        var requested = new List<DateOnly> { new(2026, 6, 1) };
        var result = new CacheResult(
            SkipDates: new HashSet<DateOnly>(),
            FetchDates: new HashSet<DateOnly> { new(2026, 6, 1) },
            ForceFetchDates: new HashSet<DateOnly>());

        _sut.EvaluateAsync(default!, requested, default)
           .ReturnsForAnyArgs(Task.FromResult(result));

        var actual = await _sut.EvaluateAsync("output/data", requested);

        Assert.Empty(actual.SkipDates);
        Assert.Single(actual.FetchDates);
        Assert.Empty(actual.ForceFetchDates);
    }

    [Fact]
    public async Task EvaluateAsync_should_return_empty_results_when_no_dates_requested()
    {
        var requested = new List<DateOnly>();
        var result = new CacheResult(
            SkipDates: new HashSet<DateOnly>(),
            FetchDates: new HashSet<DateOnly>(),
            ForceFetchDates: new HashSet<DateOnly>());

        _sut.EvaluateAsync(default!, requested, default)
           .ReturnsForAnyArgs(Task.FromResult(result));

        var actual = await _sut.EvaluateAsync("output/data", requested);

        Assert.Empty(actual.SkipDates);
        Assert.Empty(actual.FetchDates);
        Assert.Empty(actual.ForceFetchDates);
    }
}
