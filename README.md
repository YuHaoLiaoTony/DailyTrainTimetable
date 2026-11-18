# DailyTrainTimetable

**DailyTrainTimetable** 是一個 .NET 8 主控台應用程式，負責從交通部 TDX 運輸資料流通服務取得臺灣鐵路每日列車時刻表資料，經過正規化處理後輸出為靜態 JSON 檔案，並透過 GitHub Pages 發布，供行動端或網頁端應用程式使用。

---

## 專案目標

TDX 提供的原始每日時刻表 API 回傳的 JSON 結構較為複雜且欄位名稱不一致（例如 `TrainTypeID` 與 `TrainTypeId` 混用）。本專案將原始資料提取、正規化為統一且精簡的結構，並按日期拆分為獨立檔案，方便用戶端以靜態 JSON 方式下載，或進一步匯入 SQLite 進行離線查詢。

---

## 功能特色

- **每日自動排程擷取**：透過 GitHub Actions 於每日臺灣時間 05:00 自動執行。
- **多日期範圍支援**：預設抓取今天 + 未來 6 天，可透過 `--days` 參數自訂天數。
- **TDX OAuth 2.0 認證**：使用 Client Credentials 流程取得存取權杖。
- **HTTP 429 自動重試**：遇到 Rate Limit 時，根據 `Retry-After` 標頭或遞增等待時間（30s → 60s → 120s）自動重試。
- **單日失敗容錯**：某日資料抓取失敗時，自動跳過該日並繼續處理其他日期；最終若有不完整情形，以非零結束代碼退出。
- **車站資料自動蒐集**：從所有成功擷取的時刻表中自動彙整車站 ID 與名稱對照表。
- **原子寫入 JSON**：使用暫存檔 + 檔案取代機制避免寫入時讀取到不完整的檔案。
- **臺灣時區處理**：以 `Asia/Taipei` 時區計算日期，支援 Windows 與 Linux 跨平台時區名稱。
- **GitHub Pages 自動部署**：產出的 JSON 資料集自動發布至 GitHub Pages，無需額外伺服器。

---

## TDX API

本專案使用 TDX 基本型 API v3：

```
https://tdx.transportdata.tw/api/basic/v3/Rail/TRA/DailyTrainTimetable/TrainDate/{yyyy-MM-dd}
```

例如：

```
https://tdx.transportdata.tw/api/basic/v3/Rail/TRA/DailyTrainTimetable/TrainDate/2026-04-13
```

認證方式為 OAuth 2.0 Client Credentials，應用程式從環境變數讀取憑證：

| 環境變數 | 說明 |
|---|---|
| `TDX_CLIENT_ID` | TDX 申請的 Client ID |
| `TDX_CLIENT_SECRET` | TDX 申請的 Client Secret |

---

## 本機執行

### 前置需求

- 安裝 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- 向 [TDX](https://tdx.transportdata.tw/) 註冊取得 Client ID 與 Client Secret

### 執行指令

```powershell
$env:TDX_CLIENT_ID = "your-client-id"
$env:TDX_CLIENT_SECRET = "your-client-secret"
dotnet run
```

預設行為：以 `Asia/Taipei` 時區的今天起算，產生今天 + 未來 6 天（共 7 天）的資料。

### 自訂天數

```powershell
dotnet run -- --days 14
```

產生今天起 14 天的資料。

快取有效性以 `output/data/latest.json` 的 `dataVersion` 為準；版本不符或無版本資訊時會自動重抓該批日期。
強制更新：`dotnet run -- --force`（本次全部日期）或 `dotnet run -- --refresh-dates 2026-11-17,2026-11-18`（指定日期，超出 `--days` 範圍會被忽略）。

---

## 輸出檔案

所有檔案寫入 `output/data/` 目錄。

```
output/data/
├── {yyyyMMdd}.json       # 單日列車時刻表
├── latest.json            # 成功日期清單與版本資訊
└── stations.json          # 車站 ID 與名稱對照表
```

### {yyyyMMdd}.json

每日一個 JSON 檔案，包含該日所有列車的時刻資料：

```json
{
  "trainDate": "2026-04-13",
  "source": "TDX",
  "updatedAt": "2026-06-08T05:00:00+08:00",
  "trainTimetables": [
    {
      "trainNo": "123",
      "direction": 0,
      "trainTypeId": "1100",
      "trainTypeCode": "1",
      "trainTypeName": "自強",
      "startingStationId": "1000",
      "startingStationName": "臺北",
      "endingStationId": "1025",
      "endingStationName": "新竹",
      "stopTimes": [
        {
          "stationId": "1000",
          "stationName": "臺北",
          "arrivalTime": "08:00",
          "departureTime": "08:02",
          "stopSequence": 1
        }
      ]
    }
  ]
}
```

### latest.json

記錄資料產生時間、成功產生的日期清單與資料結構版本：

- `updatedAt`：產生時間（臺灣時區）
- `availableDates`：成功產生的日期字串陣列
- `dataVersion`：靜態版本號（目前為 `"1"`）

### stations.json

從所有成功擷取的時刻表中自動蒐集不重複車站列表：

- `stationId`：車站代碼
- `stationName`：車站中文名稱

---

## GitHub Actions 排程

工作流程定義於 `.github/workflows/update-train-data.yml`。

### 執行時機

| 觸發方式 | 說明 |
|---|---|
| 排程（cron） | 每日 UTC 21:00（臺灣時間 05:00）自動執行，固定 `--days 31` |
| 手動觸發 | 透過 GitHub Actions 頁面的 `workflow_dispatch` 按鈕，可填 `days` 輸入（預設 `31`，字串）覆寫天數 |

### 執行步驟

1. 簽出程式碼
2. 檢查並視需要更新 `.github/heartbeat.json`（見下方「防止排程被自動停用」）
3. 安裝 .NET 8 SDK
4. 以 `--days` 參數執行應用程式（排程固定 31 天；手動觸發時由 `days` 輸入決定）
5. 設定 GitHub Pages
6. 上傳 `output/` 目錄為 Pages artifact
7. 部署至 GitHub Pages

### 防止排程被自動停用（keepalive）

GitHub 會在 **repository 連續 60 天沒有 push 活動**後，自動停用 `schedule` 觸發的 workflow（狀態會變成 `disabled_inactivity`，之後完全不再執行，且不會有任何通知）。

本專案用「到期日」機制避免這件事：

- `.github/heartbeat.json` 記錄 `lastRefreshedAt` 與 `intervalDays`（預設 **55** 天，刻意小於 60 留 5 天緩衝）。
- 每次 workflow 執行時比對 `今天 >= lastRefreshedAt + intervalDays`：
  - 未到期 → 直接跳過，**不產生任何 commit**。
  - 已到期 → 把 `lastRefreshedAt` 改為今天、重算 `nextDueAt`，commit 並 push 回當前分支。
- 因此每年只會產生約 7 個 `chore: refresh keepalive until ...` commit，即可讓 60 天計時器永遠不會到期。

```json
{
  "intervalDays": 55,
  "lastRefreshedAt": "2026-11-17",
  "nextDueAt": "2027-01-11"
}
```

`nextDueAt` 僅供人眼與監控查看，程式判斷一律以 `lastRefreshedAt + intervalDays` 計算，避免兩個欄位不一致。

注意事項：

- 需要 workflow 的 `permissions.contents: write`（已設定）。
- 預設使用 `GITHUB_TOKEN` 推 commit；若要以「真人帳號」身分 push（或 repository 有分支保護規則），請新增 repository secret `KEEPALIVE_TOKEN`（fine-grained PAT，權限 `Contents: Read and write`），workflow 會自動優先使用它。
- 推 commit 用的是 `$GITHUB_TOKEN`/`KEEPALIVE_TOKEN`，`GITHUB_TOKEN` 的 push 不會觸發其他 workflow，因此不會造成遞迴執行。
- 若 workflow 已被停用，keepalive 也無法執行，需手動恢復一次：

  ```bash
  gh workflow enable update-train-data.yml
  ```

### Keepalive 備援（`keepalive.yml`）

上面的 keepalive 步驟住在 `update-train-data.yml` 內，一旦該 workflow 被 GitHub 停用，keepalive 也會跟著停（雞生蛋）。`.github/workflows/keepalive.yml` 是同一套「到期才 refresh」邏輯的獨立備援：

- 自己的 `schedule`（每日 UTC 20:30／臺灣時間 04:30）＋ `workflow_dispatch`，權限只需 `contents: write`。
- 判斷機制與主 workflow 完全相同（以 `.github/heartbeat.json` 的 `lastRefreshedAt + intervalDays` 算到期日），**未到期就是 no-op**，兩邊同時跑也安全、可重複執行。
- 主 workflow 被停用時，這裡仍會 push，讓 60 天計時器重新計時。

### 資料過期告警（staleness alert）

`update-train-data.yml` 失敗時只會在 Actions 頁面留下紅燈，資料會靜靜停在舊日期。`staleness-alert.yml` 每小時檢查已發布的 `latest.json`，`updatedAt` 超過 **27 小時**（24 小時排程 + 3 小時緩衝）即：

- 讓 workflow 失敗，GitHub 會通知最近修改該 workflow 的人。
- 用 `gh` 建立或更新 label 為 `data-staleness` 的 issue（已有開啟中的同 label issue 就留言、每天最多一則，避免重複開單與洗版），資料恢復正常後自動關閉。

閾值由 `staleness-alert.yml` 的 `MAX_AGE_HOURS` 控制，腳本本體為 `scripts/check-staleness.sh`（本機可跑 `MAX_AGE_HOURS=48 scripts/check-staleness.sh`，它會改讀 `output/data/latest.json`）。要關閉此告警：`gh workflow disable staleness-alert.yml`。

### 執行失敗回報（`workflow-failure` issue）

`update-train-data.yml` 內建 `if: failure()` 的回報步驟，任何一步失敗（`dotnet test`、TDX 抓取、Pages 部署）都會用 `gh` 在 label 為 `workflow-failure` 的 issue 留言（已存在開啟中的同 label issue 就留言、否則開新單，同一 run 不重複留言）；成功時自動關閉該 issue。回報步驟是 `continue-on-error`，即使它自己失敗也不會蓋掉原始錯誤。

> 資料集不保證連續：程式只從執行當下的台北日期往後產生 N 天，過去缺口不會自動補回。

### Secrets 設定

在 GitHub 儲存庫中設定以下 Secrets：

1. 進入 `Settings` > `Secrets and variables` > `Actions`
2. 新增 Repository secrets：
   - `TDX_CLIENT_ID`
   - `TDX_CLIENT_SECRET`

### GitHub Pages 啟用

1. 進入 `Settings` > `Pages`
2. `Build and deployment` 區塊選擇 `Source` 為 `GitHub Actions`
3. 手動執行一次 `Update train data` 工作流程，或等待排程自動執行

---

## 錯誤處理機制

- **憑證缺失**：啟動時若 `TDX_CLIENT_ID` 或 `TDX_CLIENT_SECRET` 未設定，直接輸出錯誤訊息並結束，結束代碼 1。
- **HTTP 429 Rate Limit**：自動等待後重試，最多重試 3 次（30s、60s、120s），支援 TDX 回傳的 `Retry-After` 標頭。
- **其他 HTTP 錯誤**：直接擲回例外，並在回應中有 Body 時一併輸出。
- **單日失敗**：該日寫入日誌後跳過，不中斷整體流程。
- **空資料回應**：TDX 回傳空陣列時會記錄警告訊息，但仍視為成功日期（寫入 `latest.json`）。
- **最終檢查**：若成功產生的天數少於要求天數，結束代碼為 1。

---

## 用戶端資料整合建議

用戶端應用程式可將下載的 JSON 匯入 SQLite 進行離線查詢。

### 建議表格結構

#### TrainDailyTimetable（列車主檔）

```sql
CREATE TABLE TrainDailyTimetable (
    TrainDate TEXT NOT NULL,
    TrainNo TEXT NOT NULL,
    Direction INTEGER,
    TrainTypeCode TEXT,
    TrainTypeName TEXT,
    StartingStationId TEXT,
    EndingStationId TEXT,
    PRIMARY KEY (TrainDate, TrainNo)
);
```

#### TrainStopTime（停靠站明細）

```sql
CREATE TABLE TrainStopTime (
    TrainDate TEXT NOT NULL,
    TrainNo TEXT NOT NULL,
    StationId TEXT NOT NULL,
    StationName TEXT,
    ArrivalTime TEXT,
    DepartureTime TEXT,
    StopSequence INTEGER,
    PRIMARY KEY (TrainDate, TrainNo, StationId, StopSequence)
);
```

### 建議索引

```sql
CREATE INDEX IX_TrainStopTime_Search
ON TrainStopTime (TrainDate, StationId, DepartureTime, StopSequence);

CREATE INDEX IX_TrainStopTime_Train
ON TrainStopTime (TrainDate, TrainNo, StopSequence);
```

### 查詢範例

查詢某日從某站到某站、指定時間之後的車次：

```sql
SELECT
    t.TrainDate,
    t.TrainNo,
    t.Direction,
    t.TrainTypeCode,
    t.TrainTypeName,
    depart.StationName AS DepartureStationName,
    arrive.StationName AS ArrivalStationName,
    depart.DepartureTime,
    arrive.ArrivalTime
FROM TrainDailyTimetable t
JOIN TrainStopTime depart
    ON depart.TrainDate = t.TrainDate
   AND depart.TrainNo = t.TrainNo
JOIN TrainStopTime arrive
    ON arrive.TrainDate = t.TrainDate
   AND arrive.TrainNo = t.TrainNo
WHERE t.TrainDate = @trainDate
  AND depart.StationId = @departureStationId
  AND arrive.StationId = @arrivalStationId
  AND depart.StopSequence < arrive.StopSequence
  AND depart.DepartureTime >= @departureTime
ORDER BY depart.DepartureTime, arrive.ArrivalTime;
```

查詢邏輯說明：
- 出發站的 `StopSequence` 必須小於到達站的 `StopSequence`（確保方向正確）
- 出發時間必須大於或等於使用者選擇的時間
- 結果依出發時間、到達時間排序
