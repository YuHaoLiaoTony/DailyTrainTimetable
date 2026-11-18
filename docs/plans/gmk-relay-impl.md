# GitHub Pages 跳板：實作結果（2026-11-18）

執行紀錄，對應 `docs/plans/gmk-relay-plan.md` 方案 P（公開）+ T（Tailscale）並行。

## DNS
- **未新增、未修改、未刪除任何 Cloudflare 記錄。** `*.p.gmk.mdevs.uk` 泛解析已涵蓋第三層。
- 驗證：`dig +short @1.1.1.1 zz-probe-8121.gh.p.gmk.mdevs.uk` → `1.34.138.73`；`dailytrain.gh.gmk.mdevs.uk` → `100.97.162.7`。
- 先備註：計畫中的風險 (c) 已解除 —— `yuhaoliaotony.github.io/DailyTrainTimetable/data/latest.json` 直接回 **200**，不再 301 到 `api.mdevs.uk`（Pages 自訂網域已移除）。

## 憑證
- 新 lineage `gmk-gh-wildcard`，SAN 2 個：`*.gh.p.gmk.mdevs.uk`、`*.gh.gmk.mdevs.uk`，到期 2027-02-16，issuer `CN=YE1`（Let's Encrypt）。
- 舊 12 個 lineage 全部完好（`bk/cems/cems.p/dash/manage.share/mcp/n8n/piweb/router/share.p/train/webgit`），`diff -r` 備份與現況僅多出 `gmk-gh-wildcard.conf`（純新增）。
- ACME 帳號既有註冊無 email（`regr.json` contact 為空），故申請未帶 `--email`，未改動帳號。

## nginx
- 新增單一檔案 `/etc/nginx/sites-enabled/gmk-gh-relay.conf`（既有 12 個 conf 完全未動，`diff -r` 僅多此檔）。
- 內容：`map $host → $gh_upstream` allowlist（`default ""`）、`map $gh_upstream → $gh_origin` 取 SNI 用 host、regex `server_name ~^[a-z0-9-]+\.gh\.p?\.gmk…`（80 轉 301、443 終結）、`resolver 1.1.1.1 8.8.8.8`、`proxy_pass https://$gh_upstream$request_uri`、`proxy_ssl_server_name on` + `proxy_ssl_name $gh_origin`、`proxy_set_header Host $gh_origin`、`proxy_redirect` 改寫。未設 `default_server`。
- `nginx -t` 成功後 `systemctl reload nginx`；`nginx -T | grep -c server_name`：**26 → 29**（+2 新 server block、+1 `proxy_ssl_server_name`）。

## 驗證
- 本機：`dailytrain.gh.p.gmk.mdevs.uk/data/latest.json` → **200**，JSON 含 `updatedAt: 2026-11-18T10:47:46+08:00`；`gh.gmk` 版同樣 200；未知 slug → **404**；http → **301** 到 https。
- 外部：`curl https://dailytrain.gh.p.gmk.mdevs.uk/data/latest.json` → **200**，憑證 `CN=*.gh.p.gmk.mdevs.uk`（issuer YE1，2026-11-18 ~ 2027-02-16）；另以 `r.jina.ai` 外部抓取 → 成功取得同一份 JSON（`updatedAt` 相同）。
- 既有站台基準比較（改動前/後完全相同）：`cems.gmk` 200、`cems.p.gmk` 200、`n8n.gmk` 200、`piweb.gmk` 200。
- 續約演練：`certbot renew --cert-name gmk-gh-wildcard --dry-run` → **success**。

## 新增第二個 repo
1. 在 `gmk-gh-relay.conf` 的 `map $host $gh_upstream` 加一行（DNS 與憑證都不用動）：
   `"myslug.gh.p.gmk.mdevs.uk"  "yuhaoliaotony.github.io/MyRepo";`
   若要內網也通，再補 `"myslug.gh.gmk.mdevs.uk"` 同值一行。
2. `nginx -t && systemctl reload nginx`。
   注意：`proxy_ssl_name`/`Host` 由 `$gh_origin` 自動截出 `github.io`，不需另改。

## 回滾
- nginx：`rm /etc/nginx/sites-enabled/gmk-gh-relay.conf && nginx -t && systemctl reload nginx`
  （完整備份：`/root/nginx-sites-backup-20261118162037`）
- 憑證：`certbot delete --cert-name gmk-gh-wildcard`（其他 lineage 備份：`/root/letsencrypt-renewal-backup-20261118162037`）
- DNS：無需回滾（未變更）。
- 注意：`certbot.timer` 續約後不會自動 reload nginx（無 deploy-hook），憑證更新後需 `systemctl reload nginx`。
