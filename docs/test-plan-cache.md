# 快取改善 — 測試案例

## 測試範圍

本次改善涵蓋兩個層面：
1. **GitHub Actions workflow**：新增 `actions/cache` 與 Pages `latest.json` 下載邏輯
2. **Program.cs / DataCacheService**：本地快取判斷——已存在**且資料版本相符**的日期跳過 API；
   版本不符或有 `--force`／`--refresh-dates` 時無視快取重抓

---

## 測試案例

### TC-1：首次執行（無 cache、無 Pages 資料）

| 項目 | 內容 |
|---|---|
| **前置條件** | `output/data/` 不存在；repo 從未部署 Pages |
| **測試步驟** | 執行 workflow |
| **預期結果** | cache miss → 正常執行全部日期的 API 呼叫；每筆都寫入檔案；Pages 成功部署 |
| **驗證方式** | 檢查 workflow log 顯示所有日期皆 fetch；`output/data/` 產出完整檔案 |

---

### TC-2：第二次執行（cache 命中，所有日期已存在）

| 項目 | 內容 |
|---|---|
| **前置條件** | 上一次成功執行已產生 `output/data/` |
| **測試步驟** | 再次執行 workflow |
| **預期結果** | cache restore 成功 → Program.cs 偵測到所有檔案已存在 → 零次 API 呼叫 → 直接跳部署 |
| **驗證方式** | workflow log 應顯示 `Done. Successful dates: N/N` 且無任何 `Fetching` 日誌；`latest.json` 的 `availableDates` 與 `stations.json` 保持既有內容 |

---

### TC-3：部分日期缺漏

| 項目 | 內容 |
|---|---|
| **前置條件** | cache 中有 D1~D5，但 D6~D7 不存在 |
| **測試步驟** | 執行 workflow |
| **預期結果** | cache restore D1~D5 → Program.cs 只對 D6~D7 呼叫 API → D1~D5 沿用舊檔 |
| **驗證方式** | log 顯示只 fetch 2 天；檢查檔案時間戳 D1~D5 為舊時間，D6~D7 為新時間 |

---

### TC-4：資料版本比對（`dataVersion` 不符 → 重抓）

| 項目 | 內容 |
|---|---|
| **前置條件** | `output/data/` 有 D1 等日期檔，且本地 `latest.json` 的 `dataVersion` 與目前程式 `DataVersion`（`Program.DataVersion`，目前 `"1"`）不同 |
| **測試步驟** | 執行 `dotnet run`（不帶強制參數） |
| **預期結果** | 版本比對失敗 → 所有已存在日期進入 `ForceFetchDates` → 逐日重抓並覆寫日期檔 |
| **驗證方式** | log 顯示 `Cache version mismatch: cached dataVersion='0' ... current='1'`，接著每筆 `Force re-fetching <date> (cache bypassed)...` |

**版本權威來源**：日期檔本身沒有版本欄位，因此以資料集索引 `latest.json` 的 `dataVersion` 作為整批日期檔的版本權威——
本地 `latest.json` 優先，其次 Pages `latest.json`；兩者都沒有版本資訊（缺檔、無此欄位、Pages 不可用）時視為過期，全部重抓。
因此版本比對**不需要**日期檔 schema 變更。

---

### TC-4b：手動無視快取重抓（`--force` / `--refresh-dates`）

| 項目 | 內容 |
|---|---|
| **前置條件** | 快取完整且版本相符（正常情況下全部 skip） |
| **測試步驟** | ① `dotnet run -- --days 3 --force`；② `dotnet run -- --days 3 --refresh-dates 2026-11-18` |
| **預期結果** | ① 3 天全部重抓；② 只有 2026-11-18 重抓，其餘 skip |
| **驗證方式** | log 顯示被強制重抓日期以 `Force re-fetching` 開頭，結尾有 `Force re-fetched N date(s) ignoring cache: ...`；範圍外日期會印 `Warning: --refresh-dates ... is outside the requested window ... and is ignored.` |

---

### TC-5：Pages 無法下載（網路錯誤或首次）

| 項目 | 內容 |
|---|---|
| **前置條件** | Pages 從未部署或 Pages URL 暫時不可用 |
| **測試步驟** | 執行 workflow |
| **預期結果** | 版本比對未通過（無法取得 Pages 或本地 `latest.json`）→ 不進行 Pages 日期比對，改以本地 `latest.json` 的 `dataVersion` 為準；仍無版本資訊則重抓 |
| **驗證方式** | log 顯示警告 `Warning: Could not fetch latest.json from ..., falling back to local file-only cache check.`；但流程不中斷 |

---

### TC-6：`--days` 參數減少

| 項目 | 內容 |
|---|---|
| **前置條件** | 上次執行 `--days 31`，cache 有 31 天資料 |
| **測試步驟** | 手動執行 `--days 7` |
| **預期結果** | 只處理 7 天；多餘的 cache 檔案不受影響（保留），`latest.json` 的 `availableDates` 是「本次成功日期 ∪ 既有 `availableDates` 中檔案仍存在的日期」的聯集，因此仍保留原本 31 筆 |
| **驗證方式** | log 顯示只 fetch/skip 7 天；`latest.json` 的 `availableDates` 仍有 31 筆（檔案被刪除的日期才會被剔除） |

---

### TC-7：`--days` 參數增加

| 項目 | 內容 |
|---|---|
| **前置條件** | 上次執行 `--days 7`，cache 有 7 天資料 |
| **測試步驟** | 手動執行 `--days 14` |
| **預期結果** | D1~D7 跳過 API，D8~D14 呼叫 API |
| **驗證方式** | log 顯示 skip 7 + fetch 7 |

---

### TC-8：cache miss（key 不匹配）

| 項目 | 內容 |
|---|---|
| **前置條件** | cache key 包含日期字串且與當日不符（例如 key 用 `DATE` salt），或 cache 已過期（7 天未命中） |
| **測試步驟** | 執行 workflow |
| **預期結果** | cache miss → 全部日期從 API 重新 fetch |
| **驗證方式** | workflow log 顯示 `Cache not found`；所有檔案均重新產生 |

---

### TC-9：cache 中的檔案損毀

| 項目 | 內容 |
|---|---|
| **前置條件** | cache restore 後某個 JSON 檔案內容不完整或格式錯誤 |
| **測試步驟** | 執行 Program.cs |
| **預期結果** | 日期檔存在且有可用版本資訊時仍會跳過 API；版本無法確認時（缺 `latest.json`／版本欄位）會重抓。純內容損毀但版本相符時不會被偵測，此為已知邊界情況（檔案存在性 + 版本比對皆通過） |
| **驗證方式** | （選用）可新增 JSON 完整性檢查步驟，若 parse 失敗則強制重新 fetch |

---

### TC-10：併行執行（不允許）

| 項目 | 內容 |
|---|---|
| **前置條件** | 同時觸發兩次 workflow |
| **測試步驟** | 在短時間內手動觸發兩次 |
| **預期結果** | `concurrency: group: pages` 設定讓第二次自動排隊等待，不會同時寫入 |
| **驗證方式** | GitHub Actions 頁面顯示第一次 running、第二次 pending |

---

## 測試環境

- 在 fork repo 上手動觸發 `workflow_dispatch` 測試各情境
- 必要時直接修改 `Program.cs` 模擬特定條件（例如模擬 Pages 下載失敗）
- 觀察 Actions log 確認行為符合預期
