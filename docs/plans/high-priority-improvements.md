# 高優先改善計畫

---

## 議題 1：HttpClient 未在真實模式 Dispose

### 現狀

`Program.cs` 的 `RunAsync` 中，當使用真實服務（非 test override）時：

```csharp
var httpClient = new HttpClient();  // 沒有 using
httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DailyTrainTimetable/1.0");
```

`httpClient` 沒有被 `using` 包住，也沒有在任何地方被 Dispose。雖然 process 結束後作業系統會回收，但在長時間執行或多次呼叫的情境下可能造成連線埠耗盡。

### 解決方案

#### 選項 A：保留雙路徑架構，用 try/finally 確保 dispose

保留目前的 `if (authServiceOverride != null)` / `else` 雙路徑，在 else 路徑用 `using` 或在 finally 中 dispose。

```
RunAsync {
    if (override) → RunCoreAsync (外部服務，不需 dispose)
    else → 建立真實服務，inside RunCoreAsync 結束後 dispose
}
```

實作方式：在 else 區塊內用 `try/finally` 或將 `HttpClient` 以 `using` 宣告在 if/else 之前但只在真實模式使用。

#### 選項 B：統一使用外部注入的 HttpClientFactory

引進 `IHttpClientFactory`，所有服務皆從 factory 取得 `HttpClient`。但這會增加依賴，不適合當前專案規模。

**建議採用選項 A**，變動最小且可讀性高。

### 測試驗證

- 現有 25 項測試不受影響（override 路徑不走 HttpClient）
- 無需新增測試（dispose 是 CLR 行為）

### 影響範圍

- 修改 1 個檔案：`Program.cs`
- 無 API schema 變更
- 無行為變更（只補上本來該做的 cleanup）

---

## 議題 2：DataCacheService 真實實作測試

### 現狀

`DataCacheServiceTests.cs` 現有 5 項測試全部使用 `NSubstitute.For<IDataCacheService>()` mock 介面。這些測試只驗證了「介面合約」和「編排層處理 cache 結果的行為」，完全沒有執行到 `DataCacheService` 的真實實作。

真實的 `DataCacheService` 包含：
- `File.Exists()` 判斷
- `HttpClient.GetAsync()` Pages 下載
- `JsonSerializer.Deserialize<LatestData>()`
- 日期集合的交集/差集運算

這些邏輯目前完全無測試覆蓋。

### 解決方案

新增 `DataCacheServiceRealTests.cs`（或擴展現有的 DataCacheServiceTests.cs），使用真實 `DataCacheService` 搭配 mock 相依。

#### 測試案例

| # | 測試名稱 | 行為 |
|---|---|---|
| 1 | `EvaluateAsync_should_skip_when_all_files_exist` | 在 temp 目錄預先建立所有日期檔案 → 呼叫 evaluate → 驗證全部 skip |
| 2 | `EvaluateAsync_should_fetch_when_files_missing` | 在 temp 目錄只建立部分檔案 → 驗證缺少的日期在 fetchDates 中 |
| 3 | `EvaluateAsync_should_fetch_when_file_exists_but_not_in_pages` | 建立本地檔案 + mock HTTP 回傳不含該日期的 latest.json → 驗證該日期在 fetchDates 中 |
| 4 | `EvaluateAsync_should_skip_when_file_exists_and_in_pages` | 建立本地檔案 + mock HTTP 回傳含該日期的 latest.json → 驗證該日期在 skipDates 中 |
| 5 | `EvaluateAsync_should_fallback_to_file_only_when_pages_unreachable` | 建立本地檔案 + mock HTTP 回傳 500 → 驗證檔案存在時直接 skip |
| 6 | `EvaluateAsync_should_handle_empty_requested_dates` | 傳入空清單 → 驗證三個集合皆 empty |

#### 實作方式

使用 `xUnit` 的 `IClassFixture<TempDirFixture>` 或手動在 test 中建立/刪除 temp 目錄：

```
[Fact]
public async Task EvaluateAsync_should_skip_when_all_files_exist()
{
    using var tempDir = new TempDirectory();          // 建立暫存目錄
    File.WriteAllText(tempDir.Path + "/20260601.json", "{}");
    File.WriteAllText(tempDir.Path + "/20260602.json", "{}");

    var handler = new FakeHttpMessageHandler(_ =>     // mock HTTP 回傳 latest.json
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("...")
        });
    using var httpClient = new HttpClient(handler);

    var service = new DataCacheService(httpClient);
    var result = await service.EvaluateAsync(tempDir.Path, new[] { ... });

    Assert.Equal(..., result.SkipDates);
    Assert.Empty(result.FetchDates);
}
```

#### Helper：TempDirectory

```csharp
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; }
    public TempDirectory() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
```

#### Mock HTTP 回傳 latest.json

沿用 `TdxApiServiceRealTests` 中的 `FakeHttpMessageHandler`，回傳正確格式的 `latest.json` 內容。

### 影響範圍

- 新增 1 個檔案：`tests/DailyTrainTimetable.Tests/DataCacheServiceRealTests.cs`
- 無須修改既有檔案
- 無 API schema 變更

---

## 預計工時

| 項目 | 預估時間 |
|---|---|
| HttpClient dispose 修正 | ~5 分鐘 |
| DataCacheService 真實測試 | ~30 分鐘 |
| 合計 | ~35 分鐘 |
