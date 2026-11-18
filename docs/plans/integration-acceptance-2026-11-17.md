# 整合驗收 2026-11-17（第二輪）

驗收者：整合驗收子任務（僅驗收，未修改任何程式碼 / workflow / output；工作樹 `main` 未提交變更，
本輪唯一寫入的檔案就是本報告）。

本輪重點：項目 2（TDX 請求層）已由 PENDING 改判；並回歸確認項目 1 未因後續 `Program.cs` 改動而退化。

## 全域結果

- `dotnet build --configuration Release` → 「建置成功。0 個警告，0 個錯誤」。
- `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --configuration Release`
  → `失敗: 0，通過: 53，略過: 0，總計: 53`（33 秒；上一輪 35 → 本輪 53，符合 ≥53 要求）。
- 測試檔分佈：`TdxAuthServiceCacheTests` 9、`ProgramEmptyResultTests` 5、`TdxApiServiceAuthRetryTests` 3、
  `CacheHitPreservationTests` 4、`DataCacheServiceRealTests` 6、`DataCacheServiceTests` 5、
  `ProgramIntegrationTests` 11、`TdxApiServiceRealTests` 3、`TdxApiServiceTests` 3、`TdxAuthServiceTests` 3。

## 項目 1 — 快取命中保留（回歸確認）：PASS

- 呼叫點仍存在（未因項目 2 的改動被移除）：
  - `Program.cs:87` `existingDataService.TryReadStationsAsync(outputDir)`（開跑前載入既有 stations）；
  - `Program.cs:96` `existingDataService.TryReadLatestAsync(outputDir)`（載入既有 latest）；
  - `Program.cs:155` `MergeAvailableDates(outputDir, previousLatest, successfulDates, existingDataService)`；
  - `Program.cs:200` `MergeAvailableDates` 定義、`Program.cs:222` `DateFileExists` 過濾（剔除已刪日期檔）。
  - 介面：`Services/IExistingDataService.cs:10/12/14`；實作：`Services/ExistingDataService.cs:14/34/55`。
- 行為證據（`--filter CacheHitPreservationTests|ProgramEmptyResultTests` → 10/10 通過），斷言未被放寬：
  - `days:1` + 當日快取命中 → `exitCode 0`、`AvailableDates.Count == 31`、stations 2 筆、`FetchDailyDataAsync` 未被呼叫
    （`CacheHitPreservationTests.cs:56-58`、`:61`）；
  - 新日期抓取 → 站點與日期取聯集（`:94-97`）；
  - 已刪日期檔被剔除 → `["2026-06-01","2026-06-03"]`（`:110`）；
  - corrupt 既有 JSON 不中斷 → `exitCode 0`、`availableDates ["2026-06-01"]`、`stations` 空（`:122-124`）。
- 結論：`--days 1` + 快取命中不會把 `stations.json` 洗成 `[]`、不會把 `availableDates` 縮成 1 天。

## 項目 2 — TDX 請求層（token 快取 / 401 重取一次 / 空結果不寫快取）：PASS

- 文件：`docs/plans/fix-tdx-request-layer.md` 存在（37 行，載明 M2/M3 修改內容、token 快取行為、
  空結果判定規則與已知限制）。
- token 快取（`Services/TdxAuthService.cs`）：
  - `ExpiryMargin = 60s` 提前過期（`:23`）、缺 `expires_in` 時 `MissingExpiryFallback = 5min`（`:25`）；
  - `_refreshLock = new SemaphoreSlim(1,1)`（`:31`）、`volatile CachedToken?`（`:34`）；
  - `GetAccessTokenAsync` 快取命中即回傳，進鎖後再檢查一次（`:46-64`）；
  - `InvalidateTokenAsync(staleToken)` 只作廢「就是失敗的那個」token（`:73-97`）；
  - `expires_in` 解析與缺欄位 warning：`:151`、`:171`。
- 401 重取一次（`Services/TdxApiService.cs:69-93`）：`SendWithAuthRetryAsync` 遇 `Unauthorized`
  → 印 log（`:78`）→ `InvalidateTokenAsync(token)`（`:79`）→ 重取 token（`:81`）→ 重試一次（`:82`）；
  仍 401 才拋 `HttpRequestException`（`:91`），不無限重試（`:117` 一般錯誤路徑不對 401 重試）。
- 空結果視為失敗（`Program.cs:111`）：`IsUnusableResult`（`:181`，空集合或全數缺 `TrainNo`）為真時
  只印 stderr `empty result, not cached`（`:120-121`），不呼叫 `WriteDailyDataAsync`、不加入 `successfulDates`
  → 不進 `availableDates`、計入 `failedDates` → `successfulDates.Count < days` 時 `return 1`（`:172-176`）。
  測試證據：`ProgramEmptyResultTests.cs:47/49`（exit 1、無 `20260601.json`）、`:75`、`:96`、`:117/133`（判定表）。

## 項目 3 — 資料過期告警：PASS

- `bash -n scripts/check-staleness.sh` → 無輸出（語法 OK）；執行權限保留。
- 三種 exit code（沙盒 `/tmp/accept2-AMqz`，以 `LATEST_URL=file://…` 餵樣本，**未觸碰 repo 的 `output/`**，驗後已 `rm -rf`）：
  - fresh 2h → `資料仍在容許範圍…已落後 2.0 小時（門檻 27 小時）`／`EXIT=0`；
  - stale 48h → `資料已過期…已落後 48.0 小時（門檻 27 小時）`／`EXIT=1`；
  - 壞 JSON → `無法判定資料新鮮度：無法解析 latest.json…`／`EXIT=2`；
  - 檔案不存在 → `遠端抓取失敗，且本機 fallback 不存在`／`EXIT=2`。
- 預設門檻：`MAX_AGE_HOURS="${MAX_AGE_HOURS:-27}"`（`:31`）；`fail_error` → `exit 2`（`:57`）、`exit 1`（`:199`）、`exit 0`（`:209`）。
- YAML：`yaml.safe_load` 成功；triggers `['schedule','workflow_dispatch']`、cron `15 * * * *`（`:13`）、
  `permissions: {contents: read, issues: write}`（`:18`）、job `check-staleness`（`:25`）以 `MAX_AGE_HOURS: "27"`（`:29`）
  實際呼叫 `scripts/check-staleness.sh`（`:42`），並在後段依 status 決定建立／更新 issue（`:50`）與最終失敗（`:140`）。
- README `:193` 「資料過期告警（staleness alert）」段落存在，載明 27 小時門檻、issue 行為與關閉方式（`:195-200`）。

## 結論

統計：**3 PASS / 0 FAIL / 0 PENDING**（項目 1 PASS 回歸通過、項目 2 PASS、項目 3 PASS）。
建置 0 錯誤 0 警告、53 項測試全綠；未執行 `dotnet run`、未呼叫 TDX API、未 `git commit`/`push`、
未 `workflow_dispatch`、未修改 repo 任何檔案（除本報告）、未動 `output/`。

Outcome: OK
