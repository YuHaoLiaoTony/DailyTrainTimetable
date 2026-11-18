namespace DailyTrainTimetable.Tests;

/// <summary>可手動推進時間的 TimeProvider，避免測試真的等待 token 過期。</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FakeTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow += delta;

    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
}
