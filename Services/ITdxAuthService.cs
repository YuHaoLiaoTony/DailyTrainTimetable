namespace DailyTrainTimetable.Services;

public interface ITdxAuthService
{
    Task<string> GetAccessTokenAsync(CancellationToken ct = default);
}
