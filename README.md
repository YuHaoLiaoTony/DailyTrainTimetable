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
| 排程（cron） | 每日 UTC 21:00（臺灣時間 05:00）自動執行 |
| 手動觸發 | 透過 GitHub Actions 頁面的 `workflow_dispatch` 按鈕 |

### 執行步驟

1. 簽出程式碼
2. 安裝 .NET 8 SDK
3. 以 `--days 31` 參數執行應用程式（產生今天起 31 天的資料）
4. 設定 GitHub Pages
5. 上傳 `output/` 目錄為 Pages artifact
6. 部署至 GitHub Pages

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
