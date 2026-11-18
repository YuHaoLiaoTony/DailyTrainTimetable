# staleness-alert 端到端驗證（2026-11-18）

## 真實 Pages 網址

`gh api repos/YuHaoLiaoTony/DailyTrainTimetable/pages` → `html_url = https://api.mdevs.uk/`（`cname=api.mdevs.uk`、`status=built`、憑證 approved、expires 2026-12-19）。
因此 `latest.json` 在 `https://api.mdevs.uk/data/latest.json`；`https://yuhaoliaotony.github.io/DailyTrainTimetable/...` 只會 301 轉到自訂網域。

**重大發現**：`api.mdevs.uk` 在 Cloudflare 授權 DNS（`meilani.ns.cloudflare.com`）回 **NXDOMAIN**，apex `mdevs.uk` 也無 A 記錄 → 已部署的資料對外實際上抓不到（不是本機網路限制）。以 `--resolve api.mdevs.uk:443:185.199.108.153` 直連 GitHub Pages CDN 可取得 200 與正確內容，證明只有 DNS 這一段壞掉。DNS 需在 Cloudflare 補上（超出本次修改範圍）。

## 腳本解析順序（`scripts/check-staleness.sh`）

`LATEST_URL`（空字串忽略）→ `gh api repos/$GITHUB_REPOSITORY/pages --jq .html_url`（gh 不可用／無權限則跳過）→ repo `CNAME` / `docs/CNAME` → `https://<owner>.github.io/<repo>` → 本機 `output/data/latest.json`；每個候選都實際抓取並解析，第一個成功者採用，全失敗才 exit 2 並印出所有試過的網址與失敗原因。

同時修掉舊 bug：repo 名稱被 `tr` 轉小寫，Pages 路徑大小寫敏感 → `dailytraintimetable` 404。workflow 另加 `LATEST_URL: ${{ vars.LATEST_URL }}`（可 `gh variable set LATEST_URL` 覆寫）與 `GH_TOKEN`。

## 端到端結果

| 項目 | 結果 |
| --- | --- |
| 本機（真實環境，DNS 無解析） | exit 2，列出 2 個候選與 curl 失敗原因（正確反映站台不可達） |
| 本機（`--resolve` 到真實 Pages CDN） | **exit 0 fresh**，`updatedAt=2026-11-18T09:38:20+08:00`、落後 **0.1 小時**、最新資料日期 `2026-11-20`、`source=pages` |
| Workflow run（`workflow_dispatch`，僅一次） | https://github.com/YuHaoLiaoTony/DailyTrainTimetable/actions/runs/44218381775 → **failure**，`status=error`（DNS NXDOMAIN），自動建立 issue #1 `data-staleness`（未關閉，反映站台真的抓不到） |

## Schedule 狀態

`gh api .../actions/workflows` → `Data staleness alert` **state=active**；`gh run list --workflow=staleness-alert.yml` 在本次手動觸發前**零次執行** → `cron: "15 * * * *"` 尚未註冊／尚未輪到。結論：workflow 本身正常（手動觸發可跑完），schedule 尚未生效；只記錄，未改 cron，建議數小時後再以 `gh run list` 確認是否開始出現 `schedule` 事件。
