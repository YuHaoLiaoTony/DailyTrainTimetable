namespace DailyTrainTimetable.Models;

/// <summary>
/// 快取評估輸入：目前程式期望的資料版本（<see cref="CurrentDataVersion"/>），
/// 以及使用者透過 CLI 明確要求無視快取重抓的日期（<see cref="ForceFetchDates"/>）。
/// </summary>
public sealed record CacheEvaluationOptions(
    string? CurrentDataVersion = null,
    IReadOnlySet<DateOnly>? ForceFetchDates = null);
