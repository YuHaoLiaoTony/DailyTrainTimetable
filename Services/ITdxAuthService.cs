namespace DailyTrainTimetable.Services;

public interface ITdxAuthService
{
    /// <summary>取得可用的 access token（實作會快取並在快過期時自動重取）。</summary>
    Task<string> GetAccessTokenAsync(CancellationToken ct = default);

    /// <summary>
    /// 作廢快取中的 token，讓下一次 <see cref="GetAccessTokenAsync"/> 強制重取。
    /// 傳入 <paramref name="staleToken"/> 時，只有快取中的 token 與它相同才作廢，
    /// 避免平行呼叫端作廢別人剛取到的新 token。
    /// </summary>
    Task InvalidateTokenAsync(string? staleToken = null, CancellationToken ct = default)
        => Task.CompletedTask;
}
