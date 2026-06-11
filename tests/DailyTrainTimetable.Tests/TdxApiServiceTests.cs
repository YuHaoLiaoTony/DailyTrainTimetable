using DailyTrainTimetable.Models;
using DailyTrainTimetable.Services;
using NSubstitute;
using Xunit;

namespace DailyTrainTimetable.Tests;

public sealed class TdxApiServiceTests
{
    private readonly ITdxApiService _sut;

    public TdxApiServiceTests()
    {
        _sut = Substitute.For<ITdxApiService>();
    }

    [Fact]
    public async Task FetchDailyDataAsync_should_return_DailyTrainData_on_success()
    {
        var date = new DateOnly(2026, 6, 1);
        var now = DateTimeOffset.Now;
        var stationMap = new Dictionary<string, Station>();

        var expected = new DailyTrainData(
            TrainDate: "2026-06-01",
            Source: "TDX",
            UpdatedAt: now,
            TrainTimetables: new List<TrainTimetable>
            {
                new("123", 0, "1100", "1", "自強",
                    "1000", "臺北", "1025", "新竹",
                    new List<StopTime>
                    {
                        new("1000", "臺北", "08:00", "08:02", 1)
                    })
            });

        _sut.FetchDailyDataAsync(date, now, stationMap, default)
            .Returns(Task.FromResult(expected));

        var actual = await _sut.FetchDailyDataAsync(date, now, stationMap);

        Assert.NotNull(actual);
        Assert.Equal("2026-06-01", actual.TrainDate);
        Assert.Single(actual.TrainTimetables);
    }

    [Fact]
    public async Task FetchDailyDataAsync_should_throw_on_network_error()
    {
        var date = new DateOnly(2026, 6, 1);
        var now = DateTimeOffset.Now;
        var stationMap = new Dictionary<string, Station>();

        _sut.FetchDailyDataAsync(date, now, stationMap, default)
            .Returns(Task.FromException<DailyTrainData>(new HttpRequestException("Network error")));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            _sut.FetchDailyDataAsync(date, now, stationMap));
    }

    [Fact]
    public async Task FetchDailyDataAsync_should_propagate_station_map()
    {
        var date = new DateOnly(2026, 6, 1);
        var now = DateTimeOffset.Now;
        var stationMap = new Dictionary<string, Station>();

        var expected = new DailyTrainData(
            TrainDate: "2026-06-01",
            Source: "TDX",
            UpdatedAt: now,
            TrainTimetables: new List<TrainTimetable>
            {
                new("456", 1, "1100", "2", "莒光",
                    "1000", "臺北", "1030", "高雄",
                    new List<StopTime>
                    {
                        new("1000", "臺北", null, "09:00", 1),
                        new("1030", "高雄", "14:00", null, 2)
                    })
            });

        _sut.FetchDailyDataAsync(date, now, stationMap, default)
            .Returns(Task.FromResult(expected));

        var actual = await _sut.FetchDailyDataAsync(date, now, stationMap);

        Assert.Equal(2, actual.TrainTimetables[0].StopTimes.Count);
    }
}
