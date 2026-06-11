# 快取改善 — 測試案例

## 測試範圍

本次改善涵蓋兩個層面：
1. **GitHub Actions workflow**：新增 `actions/cache` 與 Pages `latest.json` 下載邏輯
2. **Program.cs**：新增本地快取判斷，已存在且無更新的日期跳過 API

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
| **驗證方式** | workflow log 應顯示 `Skipped N dates (already exist)`；無任何 `Fetching` 日誌 |

---

### TC-3：部分日期缺漏

| 項目 | 內容 |
|---|---|
| **前置條件** | cache 中有 D1~D5，但 D6~D7 不存在 |
| **測試步驟** | 執行 workflow |
| **預期結果** | cache restore D1~D5 → Program.cs 只對 D6~D7 呼叫 API → D1~D5 沿用舊檔 |
| **驗證方式** | log 顯示只 fetch 2 天；檢查檔案時間戳 D1~D5 為舊時間，D6~D7 為新時間 |

---

### TC-4：當日資料有更新（cache 舊版 vs Pages 新版）

| 項目 | 內容 |
|---|---|
| **前置條件** | cache 中有 D1 但 `updatedAt` 早於 Pages 上的版本 |
| **測試步驟** | 執行 workflow |
| **預期結果** | cache restore 後，Program.cs 比對 Pages 的 `latest.json`，發現 D1 需更新 → 重新 fetch D1 |
| **驗證方式** | log 顯示 `Re-fetching D1 (outdated)`；D1 檔案更新 |

---

### TC-5：Pages 無法下載（網路錯誤或首次）

| 項目 | 內容 |
|---|---|
| **前置條件** | Pages 從未部署或 Pages URL 暫時不可用 |
| **測試步驟** | 執行 workflow |
| **預期結果** | 下載 `latest.json` 失敗 → Program.cs 不進行版本比對 → 以 cache 檔案存在與否為唯一判斷依據 |
| **驗證方式** | log 顯示警告 `Could not fetch latest.json from Pages, skipping version check`；但不影響正常執行 |

---

### TC-6：`--days` 參數減少

| 項目 | 內容 |
|---|---|
| **前置條件** | 上次執行 `--days 31`，cache 有 31 天資料 |
| **測試步驟** | 手動執行 `--days 7` |
| **預期結果** | 只處理 7 天；多餘的 cache 檔案不受影響（保留） |
| **驗證方式** | log 顯示只 fetch/skip 7 天；`latest.json` 的 `availableDates` 只有 7 筆 |

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
| **預期結果** | `WriteJsonAtomicallyAsync` 不會驗證舊檔，但檔案存在判斷為 true → 跳過 API。此為已知邊界情況 |
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
