using DailyTrainTimetable.Models;

namespace DailyTrainTimetable.Services;

/// <summary>
/// 讀取既有輸出檔（latest.json / stations.json / 日期檔），用於跨次執行的狀態合併。
/// </summary>
public interface IExistingDataService
{
    Task<LatestData?> TryReadLatestAsync(string outputDir, CancellationToken ct = default);

    Task<IReadOnlyList<Station>> TryReadStationsAsync(string outputDir, CancellationToken ct = default);

    bool DateFileExists(string outputDir, DateOnly date);
}
