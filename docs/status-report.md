# 專案現況報告

> 產生日期：2026-06-17

---

## 專案概述

.NET 8 主控台應用程式，從 TDX 運輸資料流通服務取得臺灣鐵路每日時刻表，正規化後輸出靜態 JSON 並透過 GitHub Pages 發布。

---

## 當前狀態

### 已完成功能

- [x] TDX OAuth 2.0 認證（Client Credentials）
- [x] 每日列車時刻表 API 擷取（支援 `--days` 參數）
- [x] HTTP 429 自動重試（30s → 60s → 120s）
- [x] 單日失敗容錯（跳過該日繼續處理）
- [x] 車站資料自動彙整（`stations.json`）
- [x] 原子寫入 JSON（暫存檔 + 檔案取代）
- [x] 臺灣時區處理（跨平台支援）
- [x] GitHub Pages 自動部署

### Service 架構（已重構完成）

```
Program.cs  Orchestration + 參數解析
├── TdxAuthService      OAuth token 取得
├── TdxApiService       API 請求 + 429 重試 + JSON 正規化
├── DataCacheService    本地檔案快取判斷 + Pages 版本比對
└── DataWriterService   原子 JSON 寫入
```

### 測試覆蓋

- 18 項單元測試（xUnit + NSubstitute）
- DataCacheService：5 項測試
- TdxApiService：3 項測試
- TdxAuthService：3 項測試
- Program：7 項測試（ParseDays、GetTaipeiTimeZone）

### CI/CD

- GitHub Actions 每日 UTC 21:00（臺灣時間 05:00）排程執行
- `workflow_dispatch` 支援手動觸發
- `actions/cache` 快取 `output/data/` 跨執行保留
- `dotnet test` 在 generate 前執行，失敗即中止

---

## 已知問題

### TDX 額度超量停權

| 項目 | 內容 |
|---|---|
| 發生時間 | 2026-06-17 |
| 原因 | 先前每次 workflow 執行呼叫 31 次 API，每日執行累積超量 |
| 狀態 | 憑證被停權，需等待額度重置（預計每月 1 號） |
| 因應措施 | `DataCacheService` + `actions/cache` 已上線，重置後每日只需 ~1 次 API 呼叫 |

---

## 輸出檔案結構

```
output/data/
├── {yyyyMMdd}.json       # 單日列車時刻表（正規化後）
├── latest.json            # 成功日期清單 + 版本資訊
└── stations.json          # 車站 ID 與名稱對照表
```

---

## 目錄結構

```
DailyTrainTimetable/
├── Program.cs                     入口 + 編排邏輯
├── Models/                        資料模型（6 個 record）
├── Services/                      服務實作（4 介面 + 4 實作）
├── tests/
│   └── DailyTrainTimetable.Tests/  測試專案（18 項測試）
├── docs/                          規劃與報告文件
├── .github/workflows/              CI/CD 工作流程
└── output/data/                    產出 JSON（未上版控）
```

---

## 下一步

- [ ] 等待 TDX 額度重置後恢復執行
- [ ] 驗證 cache 機制正常運作（首次 miss → 全部抓；後續 hit → 只抓缺少的）
- [ ] 可選：workflow 加入 `--days` 參數彈性（目前固定 31）
- [ ] 可選：拆分 workflow 為「每日抓 1 天」+「補缺」減少單次傳輸量
