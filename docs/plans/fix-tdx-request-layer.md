# 修正：TDX 請求層兩個缺陷（token 快取、空結果不寫快取）

對應盤點報告 `docs/plans/audit-2026-11-17.md` 的 M2（token 未快取）與 M3（空結果被永久快取）。
範圍限定在請求層；未改資料 schema、未改 `DataVersion`、未改 workflow / README。

## 修改內容

| 檔案 | 變更 |
|---|---|
| `Services/TdxAuthService.cs` | 新增 token 快取（`access_token` + `expires_in`）；可注入 `TimeProvider` 供測試推進時間 |
| `Services/ITdxAuthService.cs` | 新增 `InvalidateTokenAsync(staleToken)`：作廢「就是失敗的那個」token，下一次取用強制重取 |
| `Services/TdxApiService.cs` | 401 → 作廢 token、重取一次、重試一次；仍 401 才拋錯（不無限重試） |
| `Program.cs` | 空結果改判為失敗（不寫日期 JSON、不進 `successfulDates`、計入失敗數、exit code 非 0）；新增 `IsUnusableResult` |
| `tests/.../TdxAuthServiceCacheTests.cs`（新） | token 重用／將過期重取／平行只取一次／invalidate／缺 `expires_in`／錯誤回應 |
| `tests/.../TdxApiServiceAuthRetryTests.cs`（新） | 401 只重取一次 token、多日期共用一个 token、重取後仍 401 則失敗 |
| `tests/.../ProgramEmptyResultTests.cs`（新） | 空結果不寫檔、不進 `availableDates`、exit code 1 |
| `tests/.../FakeTimeProvider.cs`（新） | 測試用可推進時鐘 |

## token 快取行為

- 同一個 process 內只向 token endpoint 索取一次；有效期以 `expires_in` 計算，**提前 60 秒**視為過期（壽命 ≤ 60 秒時取一半）。
- `expires_in` 缺失或不可解析 → 保守假設 5 分鐘寿命並印 warning（寧可多取一次，也不拿過期 token）。
- Thread-safe：`SemaphoreSlim` 序列化重取，進鎖後再檢查一次快取；快取值是不可變 record 搭配 `volatile` 讀取，單一 process 內循序或平行呼叫都只會產生一個 token 請求。
- token 失效（API 回 401）→ `InvalidateTokenAsync(oldToken)` 只作廢該 token（避免平行呼叫端互砍新 token）→ 重取並重試一次。

## 空結果判定規則（M3）

`Program.IsUnusableResult`：TDX 回 200 但 **班次集合為空**，或 **所有班次都缺 `TrainNo`** → 視為該日期失敗：

1. 不寫 `output/data/{yyyyMMdd}.json`；
2. 不加進 `successfulDates`（因此不進 `latest.json.availableDates`）；
3. 計入失敗數（`Failed dates (...)` log），沿用既有機制讓 exit code 非 0；
4. log 明確標示 `empty result, not cached`，下次執行會重新抓取。

**區分「合法無營運日」的說明**：TDX 目前對「該日無營運（例如全面停駛）」與「資料尚未產生／暫時抓不到」都只回空陣列，沒有任何可辨識欄位可區分，因此不做一刀切放行，一律不寫檔（寧可下次重抓，也不留下永久空資料）。若日後 TDX 提供明確 no-service 標記，再於 `Program.cs` 該處放行寫檔。

已知限制（不在本次範圍）：本次之前已寫入的空白日期檔仍會被快取判定為命中而不重抓，需靠 M4（快取版本／時間比對）或清掉該檔處理。
