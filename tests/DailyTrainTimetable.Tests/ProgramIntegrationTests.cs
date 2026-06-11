using Xunit;

namespace DailyTrainTimetable.Tests;

public sealed class ProgramIntegrationTests
{
    [Fact]
    public void ParseDays_should_return_default_when_no_args()
    {
        var result = Program.ParseDays([]);
        Assert.Equal(7, result);
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
}
