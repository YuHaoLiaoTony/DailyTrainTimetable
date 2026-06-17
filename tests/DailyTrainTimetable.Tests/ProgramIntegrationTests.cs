using DailyTrainTimetable.Models;
using DailyTrainTimetable.Services;
using NSubstitute;
using Xunit;

namespace DailyTrainTimetable.Tests;

public sealed class ProgramIntegrationTests : IDisposable
{
    private const string ClientId = "test-client-id";
    private const string ClientSecret = "test-client-secret";

    public ProgramIntegrationTests()
    {
        Environment.SetEnvironmentVariable("TDX_CLIENT_ID", ClientId);
        Environment.SetEnvironmentVariable("TDX_CLIENT_SECRET", ClientSecret);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TDX_CLIENT_ID", null);
        Environment.SetEnvironmentVariable("TDX_CLIENT_SECRET", null);
    }

    [Fact]
    public void ParseDays_should_return_default_when_no_args()
    {
        var result = Program.ParseDays([]);
        Assert.Equal(1, result);
    }

    [Fact]
    public void ParseDays_should_return_custom_days()
    {
        var result = Program.ParseDays(["--days", "14"]);
        Assert.Equal(14, result);
    }

    [Fact]
    public void ParseDays_should_throw_on_missing_value()
    {
        Assert.Throws<ArgumentException>(() => Program.ParseDays(["--days"]));
    }

    [Fact]
    public void ParseDays_should_throw_on_invalid_value()
    {
        Assert.Throws<ArgumentException>(() => Program.ParseDays(["--days", "abc"]));
    }

    [Fact]
    public void ParseDays_should_throw_on_non_positive()
    {
        Assert.Throws<ArgumentException>(() => Program.ParseDays(["--days", "0"]));
    }

    [Fact]
    public void GetTaipeiTimeZone_should_return_valid_timezone()
    {
        var tz = Program.GetTaipeiTimeZone();
        Assert.NotNull(tz);
        Assert.Equal(8, tz.BaseUtcOffset.Hours);
    }

    [Fact]
    public void GetTaipeiTimeZone_should_have_id_Asia_Taipei_or_Taipei_Standard_Time()
    {
        var tz = Program.GetTaipeiTimeZone();
        Assert.Contains(tz.Id, new[] { "Asia/Taipei", "Taipei Standard Time" });
    }

    [Fact]
    public async Task RunAsync_should_not_call_api_when_all_dates_are_cached()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.Now.Date);

        var cacheService = Substitute.For<IDataCacheService>();
        cacheService.EvaluateAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<DateOnly>>(), default)
            .Returns(new CacheResult(
                SkipDates: new HashSet<DateOnly> { today },
                FetchDates: new HashSet<DateOnly>(),
                ForceFetchDates: new HashSet<DateOnly>()));

        var apiService = Substitute.For<ITdxApiService>();
        var authService = Substitute.For<ITdxAuthService>();
        var writerService = Substitute.For<IDataWriterService>();

        var exitCode = await Program.RunAsync(["--days", "1"], authService, apiService, cacheService, writerService);

        Assert.Equal(0, exitCode);
        await apiService.DidNotReceiveWithAnyArgs().FetchDailyDataAsync(default, default, default!);
    }

    [Fact]
    public async Task RunAsync_should_fetch_only_missing_dates()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.Now.Date);

        var cacheService = Substitute.For<IDataCacheService>();
        cacheService.EvaluateAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<DateOnly>>(), default)
            .Returns(new CacheResult(
                SkipDates: new HashSet<DateOnly> { today },
                FetchDates: new HashSet<DateOnly> { today.AddDays(1), today.AddDays(2) },
                ForceFetchDates: new HashSet<DateOnly>()));

        var apiService = Substitute.For<ITdxApiService>();
        apiService.FetchDailyDataAsync(
                Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<IDictionary<string, Station>>(), Arg.Any<CancellationToken>())
            .Returns(new DailyTrainData(
                TrainDate: "2026-06-01", Source: "TDX",
                UpdatedAt: DateTimeOffset.Now,
                TrainTimetables: new List<TrainTimetable>()));

        var authService = Substitute.For<ITdxAuthService>();
        var writerService = Substitute.For<IDataWriterService>();

        var exitCode = await Program.RunAsync(["--days", "3"], authService, apiService, cacheService, writerService);

        Assert.Equal(0, exitCode);
        await apiService.Received(2).FetchDailyDataAsync(
            Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset>(),
            Arg.Any<IDictionary<string, Station>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_should_continue_when_single_date_fails()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.Now.Date);
        var tomorrow = today.AddDays(1);
        var callCount = 0;

        var cacheService = Substitute.For<IDataCacheService>();
        cacheService.EvaluateAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<DateOnly>>(), default)
            .Returns(new CacheResult(
                SkipDates: new HashSet<DateOnly>(),
                FetchDates: new HashSet<DateOnly> { today, tomorrow },
                ForceFetchDates: new HashSet<DateOnly>()));

        var apiService = Substitute.For<ITdxApiService>();
        apiService.FetchDailyDataAsync(
                Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<IDictionary<string, Station>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return new DailyTrainData(
                        TrainDate: "2026-06-01", Source: "TDX",
                        UpdatedAt: DateTimeOffset.Now,
                        TrainTimetables: new List<TrainTimetable>());
                }

                throw new HttpRequestException("API error");
            });

        var authService = Substitute.For<ITdxAuthService>();
        var writerService = Substitute.For<IDataWriterService>();

        var exitCode = await Program.RunAsync(["--days", "2"], authService, apiService, cacheService, writerService);

        Assert.Equal(1, exitCode);
        await apiService.Received(2).FetchDailyDataAsync(
            Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset>(),
            Arg.Any<IDictionary<string, Station>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_should_return_1_when_credentials_missing()
    {
        Environment.SetEnvironmentVariable("TDX_CLIENT_ID", null);
        Environment.SetEnvironmentVariable("TDX_CLIENT_SECRET", null);

        try
        {
            var result = await Program.RunAsync([]);
            Assert.Equal(1, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TDX_CLIENT_ID", ClientId);
            Environment.SetEnvironmentVariable("TDX_CLIENT_SECRET", ClientSecret);
        }
    }
}
