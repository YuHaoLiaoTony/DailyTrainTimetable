namespace DailyTrainTimetable.Services;

public sealed class TdxAuthService : ITdxAuthService
{
    public Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}
