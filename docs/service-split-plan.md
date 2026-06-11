# Service 拆分規劃

## 現狀問題

目前的 `Program.cs` 單一檔案包含：

- 參數解析
- TDX 認證（OAuth Client Credentials）
- HTTP 請求 + 429 重試邏輯
- JSON 反序列化與正規化（欄位名稱相容處理）
- 本地檔案快取判斷（尚未實作）
- 原子 JSON 寫入
- 車站資料彙整
- `latest.json` / `stations.json` 產生

全部是 top-level 陳述式 + 靜態區域函式，無法 mock、無法單獨測試。

---

## 目標架構

```
DailyTrainTimetable/
├── Program.cs                  Orchestration + 參數解析
├── Models/                     資料模型（records）
│   ├── DailyTrainData.cs
│   ├── TrainTimetable.cs
│   ├── StopTime.cs
│   ├── Station.cs
│   └── LatestData.cs
├── Services/                   抽像服務
│   ├── ITdxAuthService.cs
│   ├── TdxAuthService.cs
│   ├── ITdxApiService.cs
│   ├── TdxApiService.cs
│   ├── IDataCacheService.cs
│   ├── DataCacheService.cs
│   ├── IDataWriterService.cs
│   └── DataWriterService.cs
├── Infrastructure/             工具類別
│   ├── TimeZoneHelper.cs
│   └── JsonSerializerOptionsProvider.cs
└── output/data/                產生結果
```

---

## 服務介面與職責

### 1. ITdxAuthService

```csharp
interface ITdxAuthService
{
    Task<string> GetAccessTokenAsync(CancellationToken ct = default);
}
```

| 職責 | 說明 |
|---|---|
| 向 TDX OAuth endpoint 請求 token | `POST /auth/realms/TDXConnect/protocol/openid-connect/token` |
| 回傳 `access_token` | 若失敗拋出例外 |
| 快取 token | 可選擇在 expiry 前重用（避免每次請求重新取得） |

**依賴**：`HttpClient`

---

### 2. ITdxApiService

```csharp
interface ITdxApiService
{
    Task<DailyTrainData> FetchDailyDataAsync(
        DateOnly trainDate,
        DateTimeOffset updatedAt,
        IDictionary<string, Station> stationMap,
        CancellationToken ct = default);
}
```

| 職責 | 說明 |
|---|---|
| 對 TDX API 發送 HTTP GET | `GET /.../TrainDate/{yyyy-MM-dd}` |
| 429 重試邏輯 | 30s → 60s → 120s，支援 `Retry-After` |
| JSON 反序列化 + 正規化 | 處理 `TrainTypeID`/`TrainTypeId`、`TrainTypeName` 多語物件等 |
| 車站資訊彙整 | 寫入傳入的 `stationMap` |

**依賴**：`HttpClient`、`ITdxAuthService`

---

### 3. IDataCacheService

```csharp
interface IDataCacheService
{
    Task<CacheResult> EvaluateAsync(
        string outputDir,
        DateOnly[] requestedDates,
        CancellationToken ct = default);
}

record CacheResult(
    IReadOnlySet<DateOnly> SkipDates,       // 不需處理的日期
    IReadOnlySet<DateOnly> FetchDates,      // 需要呼叫 API 的日期
    IReadOnlySet<DateOnly> ForceFetchDates); // Pages 上有更新需強制重抓
```

| 職責 | 說明 |
|---|---|
| 檢查本地檔案是否存在 | `output/data/{yyyyMMdd}.json` File.Exists |
| 從 Pages 下載 `latest.json` | 比對 `availableDates` 與 `updatedAt` |
| 判定哪些日期可 skip | 檔案存在且 Pages 無更新 |
| 判定哪些日期需 force fetch | Pages 上的 `updatedAt` 比 cache 更新 |
| 若 Pages 無法下載 | 降級：僅以檔案存在判斷 |

**依賴**：`HttpClient`（用於 Pages 下載）

---

### 4. IDataWriterService

```csharp
interface IDataWriterService
{
    Task WriteDailyDataAsync(DailyTrainData data, string outputDir, CancellationToken ct = default);
    Task WriteLatestAsync(LatestData latest, string outputDir, CancellationToken ct = default);
    Task WriteStationsAsync(IReadOnlyList<Station> stations, string outputDir, CancellationToken ct = default);
}
```

| 職責 | 說明 |
|---|---|
| 原子寫入 JSON | 暫存檔 `.tmp` → `File.Move` 取代 |
| 產生檔名 | `{yyyyMMdd}.json` |
| 統一的 JsonSerializerOptions | camelCase + indented + 忽略 null |

**依賴**：無（或可注入 `IJsonSerializerOptionsProvider`）

---

### 5. Program.cs（Orchestration）

```csharp
// 大致流程
var args = ParseArgs(Environment.GetCommandLineArgs());
var dates = GenerateDateRange(args.Days, args.StartDate);
var cacheResult = await cacheService.EvaluateAsync(outputDir, dates);

foreach (var date in cacheResult.FetchDates.Union(cacheResult.ForceFetchDates))
{
    var data = await tdxApiService.FetchDailyDataAsync(date, now, stationMap);
    await dataWriterService.WriteDailyDataAsync(data, outputDir);
    successfulDates.Add(date);
}

// 寫入 latest.json + stations.json
```

**職責**：
- 參數解析（`--days`，未來可擴充 `--start-date`）
- DI 容器設定（手動 new 或 `Microsoft.Extensions.DependencyInjection`）
- 編排整體流程
- 最終 exit code 判斷

---

## Service 依賴圖

```
Program.cs
  ├── IDataCacheService       → HttpClient (for Pages)
  ├── ITdxApiService           → HttpClient, ITdxAuthService
  │     └── ITdxAuthService    → HttpClient (for TDX OAuth)
  └── IDataWriterService       → (無)
```

---

## 測試策略

| Service | 測試方式 | Mock 標的 |
|---|---|---|
| `TdxAuthService` | 單元測試 | `HttpClient`（`HttpMessageHandler`） |
| `TdxApiService` | 單元測試 | `HttpClient`、`ITdxAuthService` |
| `DataCacheService` | 單元測試 | `HttpClient`、`File.Exists`（若可 mock）或抽象 `IFileSystem` |
| `DataWriterService` | 整合測試 | 實際寫入暫存目錄後驗證檔案內容 |
| `Program.cs` | 整合測試 | 注入 mock service，驗證編排行為 |

---

## 實作順序（建議）

| 階段 | 內容 | 可測試 |
|---|---|---|
| 1 | 建立 `Models/` 目錄，把現有 records 移出 `Program.cs` | 不需 |
| 2 | 建立 `tests/` xUnit 專案 | ✅ |
| 3 | 實作 `DataCacheService` + 測試 | ✅ |
| 4 | 提取 `ITdxAuthService` + 測試 | ✅ |
| 5 | 提取 `ITdxApiService` + 測試 | ✅ |
| 6 | 提取 `IDataWriterService` + 測試 | ✅ |
| 7 | 重構 `Program.cs` 為 Orchestration + DI | ✅ |
| 8 | 調整 workflow：加入 `actions/cache` + 下載 latest.json | 手動驗證 |
