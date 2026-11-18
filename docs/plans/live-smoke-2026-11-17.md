# 線上驗證：TDX 憑證與資料更新（live smoke）

## 執行摘要

| 項目 | 值 |
| --- | --- |
| 執行時間 | 2026-11-18 09:37 (Asia/Taipei) / 01:37 UTC |
| 觸發方式 | `gh workflow run update-train-data.yml -f days=3 --ref main`（僅此一次，基準 `7b8c7ba`） |
| Run url | https://github.com/YuHaoLiaoTony/DailyTrainTimetable/actions/runs/44217157707 |
| Conclusion | **success**（約 2 分鐘完成）；TDX 判定 **可用** |

## TDX 可用性結論

- 日誌：`Fetching 2026-11-18 / 19 / 20` → `Generated output/data/2026{1118,1119,1120}.json` → `Done. Successful dates: 3/3`。
- 無 `invalid_client` / 401 / `unauthorized`（憑證有效）、無 429 / `quota` / `forbidden`（未受限）、無 `empty result`。
- 實際筆數 906 / 907 / 928 筆 `trainTimetables`，確認非空結果假成功；節流 `Waiting 10 seconds` 正常。
- 註：觸發時台北已跨日為 11-18，故日期為 11-18 ~ 11-20（非 11-17 起），屬預期。

## Pages 上的 latest.json

自訂網域 `api.mdevs.uk` 在本驗證環境無法解析 DNS，改用同一次 run 部署的 `github-pages` artifact 驗證內容；部署步驟回報 `Reported success!`（Pages status: built）：

```json
{ "updatedAt": "2026-11-18T09:38:20+08:00", "availableDates": ["2026-11-18", "2026-11-19", "2026-11-20"], "dataVersion": "1" }
```

`updatedAt` 為當日、3 個新日期、`dataVersion` = 1。

## 其他觀察

- `data-staleness` 與 `workflow-failure` 標籤目前無任何 issue（open/closed 皆無）。
- `staleness-alert.yml` 自加入後**尚無任何 run**；可能剛 push 排程未輪到，或 schedule 未註冊 —— 僅記錄，未干預。
- run 後 `origin/main` 仍為 `7b8c7ba`；keepalive 判定 `nextDue=2027-01-11` 跳過，未產生額外 commit。

## 後續建議

1. **憑證與額度皆正常**，可放心維持排程 `--days 31`；建議下次以 `-f days=31` 再驗一次請求量仍在配額內。
2. 建議把 TDX 請求量（token / API 次數）印進日誌，讓額度問題可及早發現。
3. 確認 `staleness-alert.yml` schedule 是否註冊成功（目前 0 次執行）；必要時微調 cron。
4. `api.mdevs.uk` 對外可達性建議另找網路環境驗證；本次以部署 artifact 為準。
