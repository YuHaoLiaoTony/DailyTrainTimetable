# M4 修復：快取 dataVersion 比對 + ForceFetchDates 接線

## 問題（audit-2026-11-17 M4 / m6）
- `DataCacheService` 只看「日期檔存在」與 Pages `availableDates`，不讀 `dataVersion`。
- `ForceFetchDates` 從未填充，`Program` 也不理它 → 死碼；README / status-report / TC-4 宣稱的版本比對不存在。

## 本次做法（無 schema 變更）
- **版本權威**：日期檔（`DailyTrainData`）沒有版本欄位，因此以資料集索引 `latest.json` 的 `dataVersion`
  作為整批日期檔的版本權威：本地 `latest.json` 優先，其次 Pages `latest.json`。
- **判定**：`CacheEvaluationOptions.CurrentDataVersion`（由 `Program.DataVersion` 傳入，值未變更 `"1"`）
  與上述任一來源相符 → 可跳過；不符或**完全沒有版本資訊**（缺檔／缺欄位／Pages 不可用）→ 既有日期進 `ForceFetchDates`，重抓覆寫。
- **新增 CLI**：`--force`（本次要求的全部日期）、`--refresh-dates yyyy-MM-dd[,...]`（指定日期；範圍外會警告並忽略）。
  兩者同時出現以 `--force` 為準；`Program` 實際以 `FetchDates ∪ ForceFetchDates` 決定抓取清單，並印出
  `Force re-fetching <date> (cache bypassed)...` 與收尾的 `Force re-fetched N date(s) ...`。
- **檔案**：`Models/CacheEvaluationOptions.cs`（新）、`Services/DataCacheService.cs`（注入 `IExistingDataService`、版本判定、
  填充 `ForceFetchDates`）、`Services/IDataCacheService.cs`、`Program.cs`（`DataVersion` 改 internal、CLI 解析、強制重抓串接）。
- **測試**：`DataCacheServiceRealTests`（版本相同跳過／版本不同重抓／缺版本重抓／CLI 指定強制重抓／Pages 版本不符）、
  `ProgramForceFetchTests`（CLI 解析、`ResolveForcedDates`、`RunAsync` 傳入 options 且真的抓 `ForceFetchDates`）。
  基線 53 → 67 個測試全通過。
- **文件**：`docs/test-plan-cache.md` TC-4（版本比對）、TC-4b（強制重抓）、TC-6（availableDates 聯集）、TC-2/TC-5/TC-9 敘述同步。

## 未做（需決策）
- 未在日期檔加 `dataVersion` 欄位。若要「逐檔版本」而非「資料集版本」，需改 `DailyTrainData` schema（+ `DataWriterService`
  寫入 `dataVersion`、README schema 範例、`DataVersion` 升版），會讓舊檔缺欄位而全數重抓一次。目前以 `latest.json`
  為權威在多數情境等價（同一次 run 產生的日期檔版本一致），故不切。
- 未做 `updatedAt` 新舊比較：日期檔的 `updatedAt` 是抓取時間，非版本訊號，容易誤判為「較舊」而重抓。
- 未支援 `--refresh-dates` 抓取 `--days` 窗口外的日期（屬 B1 `--start-date` 範疇）。

## 建議 README 文字
- 已存在日期的快取有效性以 `latest.json` 的 `dataVersion` 為準；版本不符或無版本資訊時會自動重抓該批日期。
- 需要強制更新時：`dotnet run -- --force`（本次全部日期）或 `dotnet run -- --refresh-dates 2026-11-17,2026-11-18`（指定日期）。
- `--refresh-dates` 只接受 `--days` 決定範圍內的日期；範圍外會被忽略並印出警告。
