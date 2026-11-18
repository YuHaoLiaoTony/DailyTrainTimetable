# Pages 自訂網域移除（2026-11-18）

## 改動
- `gh api --method PUT repos/YuHaoLiaoTony/DailyTrainTimetable/pages -f cname=""`（最小改動，未用 DELETE，Pages 站未停用）。
- 未修改任何 repo 檔案、未 commit／push。

## 改動前後
| | 改動前 | 改動後 |
|---|---|---|
| `cname` / `html_url` | `api.mdevs.uk` / `https://api.mdevs.uk/` | `null` / `https://yuhaoliaotony.github.io/DailyTrainTimetable/` |
| `status` / `build_type` | `built` / `workflow` | `built` / `workflow`（不變） |
| `latest.json` | `301 → https://api.mdevs.uk/...`（NXDOMAIN） | `200` |

- `api.mdevs.uk` DNS 為 NXDOMAIN（Cloudflare 無記錄），301 目標取不到任何資料。
- CDN 邊緣快取舊 301（TTL 1 小時）：15:11 建立、`age` 累積至 3592 秒後，16:12:26 起回 `200`。

## 驗證
- `curl -sI .../data/latest.json` → `HTTP/2 200`（`last-modified: 2026-11-18 02:53:31 GMT`）；`gh api .cname` → `null`。
- 內容：`updatedAt: 2026-11-18T10:47:46+08:00`，`availableDates` 由 `2026-11-18` 起連續 31 天。

## 監控
- `bash scripts/check-staleness.sh` → **exit 0**，`來源=pages`，落後 5.5 小時（門檻 27）。
- `gh workflow run staleness-alert.yml --ref main` → run `44265766248` **success**（14s）。
- issue #1「資料過期…」由 workflow 於 `2026-11-18T08:18:02Z` **自動關閉**（未手動介入）。

## Workflow 依賴檢查（僅回報，未修改）
- 無 `CNAME` 檔；`actions/configure-pages@v5` 未帶 `cname` 參數；`PAGES_BASE_URL` 已指向 `github.io` → 無 `api.mdevs.uk` 依賴。

## 結論
對外正式資料集網址 = `https://yuhaoliaotony.github.io/DailyTrainTimetable/data/latest.json`
