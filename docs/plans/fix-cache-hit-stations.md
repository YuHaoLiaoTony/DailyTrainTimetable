# 修正：快取命中／`--days 1` 導致 stations.json 與 availableDates 退化

## 問題（Blocking）

`Program.RunCoreAsync` 每次都以「本次執行結果」為唯一真相：

1. `stationMap` 每次執行都從空的 `SortedDictionary` 開始，只有真正呼叫 TDX 的日期才會補站點。
   全部快取命中時 `stationMap` 為空 → `stations.json` 被覆寫成 `[]`。
2. `latest.json.availableDates` 只放本次請求窗內的日期，於是 `--days 1` 會把既有 31 天清單縮成 1 天。

## 修改內容

| 檔案 | 變更 |
|---|---|
| `Services/IExistingDataService.cs`（新） | 讀取既有輸出狀態的介面：`TryReadLatestAsync`、`TryReadStationsAsync`、`DateFileExists` |
| `Services/ExistingDataService.cs`（新） | 實作；camelCase + case-insensitive 反序列化；檔案不存在或 JSON 損毀時回傳 null/空集合並印 warning，不中斷執行 |
| `Program.cs` | `RunCoreAsync` 改為 `internal` 並新增 `IExistingDataService` 參數；`RunAsync` 新增對應 optional override（既有呼叫端不受影響）。開始前先載入既有 `stations.json` 灌入 `stationMap`、既有 `latest.json` 供合併；新增 `MergeAvailableDates` |
| `tests/.../CacheHitPreservationTests.cs`（新） | 4 個回歸測試（暫存目錄 + 真實 DataWriterService/ExistingDataService） |

## 行為變更點

- `stations.json` = 既有站點聯集（seed 進 `stationMap`）+ 本次新發現站點；同一 `stationId` 以既有資料為優先（與既有跨日期 dedup 行為一致）。首次執行仍會產生 `[]`（正常）。
- `latest.json.availableDates` = 本次成功日期 ∪ 既有 `availableDates` 中「日期檔仍存在」者，遞增排序。因此 `--days 1` 不再縮短清單；反之檔案被移除的日期會被自動剔除，不會無限累積幽靈日期。
- `Done. Successful dates: X/days` 與非 0 結束碼判斷仍以「本次請求窗」為準，未改變。
- 損毀的既有 JSON 只會印 warning 並被視為「無既有資料」，不影響本次輸出。
- 註：`docs/test-plan-cache.md` TC-6 記載「`--days 7` 後 availableDates 只有 7 筆」已不再成立（本任務不修改該檔，建議另案更新）。

## 驗證

```bash
dotnet build --configuration Release
DOTNET_ROLL_FORWARD=LatestMajor dotnet test --configuration Release
```

結果：build 0 warning / 0 error；test 35 passed / 0 failed。
（本機僅安裝 .NET 10 runtime，測試以 `DOTNET_ROLL_FORWARD` 執行；未執行 `dotnet run`，未呼叫 TDX API。）
