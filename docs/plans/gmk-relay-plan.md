# `*.gh.gmk.mdevs.uk` → GitHub Pages 跳板：盤點與計畫
唯讀盤點（2026-11-18），未改 nginx / DNS / 憑證 / Pages；未讀任何私鑰或 `.ini` 內容。**條件**：nginx 1.26.3 已在 `0.0.0.0:80,443` 服務、certbot DNS-01+Cloudflare 現成、`*.p.gmk.mdevs.uk` DNS-only 指向公開 IP 且外部實測可達 → 兩條路都可行。**缺**：萬用字元憑證、泛用 Host→repo 的 server block。**最大風險**：Pages 自訂網域 `api.mdevs.uk` 未移除（NXDOMAIN），github.io 一律 301 到壞網址。
## 1 環境
1. `tailscale ip -4`=**100.97.162.7**；`ifconfig.me`=**1.34.138.73**；`ip route get 1` src=192.168.0.25。
2. nginx **1.26.3**；sites-enabled 12 conf：cems, cems.p, dash, default, manage.share, mcp, n8n, piweb, router, share.p, train, webgit。
3. `ss -tlnp`：`0.0.0.0:80/443` + `[::]:80/443`（全網卡，未綁單一 IP）。
4. upstream 全為本機/內網（127.0.0.1:3000/5197/5678/8081/8090/8504/8505/8765、192.168.0.1）；**無**以公開 IP 為主的 server block。
5. **無** `server_name ~regex`、**無** `map`、**無** `proxy_ssl_server_name` → 本計畫首例，無既有風格可抄（見 §5）。
6. **無 443 default_server**：未命中 Host 落到字母序第一個 443 block(cems)；實測 `dailytrain.gh.gmk` 取得 `CN=cems.gmk.mdevs.uk` + HTTP 200（錯憑證、錯站台）。
## 2 憑證
7. `live/` 12 個 lineage，**全是單一名稱、無 `*.` 萬用字元**；到期 2026-12-27(cems) ~ 2027-02-10(webgit)。
8. 續約：10 個 `authenticator=dns-cloudflare`、2 個 `nginx`（cems.p、share.p）；全域 `key_type=ecdsa`。
9. `/etc/letsencrypt/cloudflare.ini` **存在**，`-rw------- root root`（僅查存在與權限）；`propagation_seconds=60`。
10. 續約排程：`certbot.timer` enabled+active（上次 11-18 12:39、下次 11-19 09:23）＋ `/etc/cron.d/certbot` legacy fallback。
## 3 DNS（@1.1.1.1，另用 authoritative NS 交叉驗證）
11. NS：`meilani.ns.cloudflare.com` / `mitchell.ns.cloudflare.com`；`*.gmk.mdevs.uk` → A **100.97.162.7**，TTL 120，DNS-only（非 CF 代理 IP）；多層亦命中（`a.b.c.gmk` 同值）。`gh.gmk`、`dailytrain.gh.gmk` 同值＝泛解析吃到、**無專屬記錄**；`api.mdevs.uk` → **NXDOMAIN**。
12. `*.p.gmk.mdevs.uk` → A **1.34.138.73**（＝本機公開 IP，DNS-only）；`zz-probe-8471.p.gmk`、`deep.x.p.gmk` 同值 → 泛解析成立、無 AAAA；單層 `p.gmk` 本身被 `*.gmk` 吃到 100.97.162.7。
13. 外部視角：`r.jina.ai` 抓 `https://cems.p.gmk.mdevs.uk/` 回 200（final `/login`）→ **80/443 對公網可達**；但未知 Host `zz-probe-8471.p.gmk` 的 TLS 只給 `CN=cems.gmk.mdevs.uk`，curl `(60)` 失敗（同 §1-6 根因）。
14. `HEAD https://yuhaoliaotony.github.io/DailyTrainTimetable/data/latest.json` → **301**，`location: https://api.mdevs.uk/data/latest.json`；`-L` 追隨失敗（curl exit 6）。本機出外 443 正常（185.199.110.153）。
## 4 兩方案並列
| 項目 | T：Tailscale 跳板 | P：公開跳板 |
|---|---|---|
| 網域 → IP | `*.gh.gmk.mdevs.uk` → **100.97.162.7** | `*.gh.p.gmk.mdevs.uk` → **1.34.138.73** |
| Actions runner | **不可觸及**（到不了 100.64/10） | **可觸及**（外部實測 443 通） |
| staleness 能否抓到資料 | **不能** | **能（唯一可行）** |
| 新增 CF 記錄 | 0（既有泛解析涵蓋） | 1（`*.gh.p` A，DNS only） |
| 憑證層級 | 第二層 `*.gh.gmk.mdevs.uk` | 第三層 `*.gh.p.gmk.mdevs.uk` |
## 5 nginx 草稿（僅報告用，未建立檔案）
```nginx
map $gh_repo $gh_path {   # /etc/nginx/conf.d/gh-pages-map.conf（http 區塊，T/P 共用） default "yuhaoliaotony/$gh_repo"; "dailytrain" "yuhaoliaotony/DailyTrainTimetable"; }
# /etc/nginx/sites-enabled/gh-pages.gmk.mdevs.uk.conf
server {                                              # :80 → 301（方案 P 才有意義）
    listen 80; listen [::]:80;
    server_name ~^(?<gh_repo>[a-z0-9-]+)\.gh\.gmk\.mdevs\.uk$;    # 方案 P 改 \.gh\.p\.gmk
    return 301 https://$host$request_uri;
}
server {
    listen 443 ssl; listen [::]:443 ssl;
    server_name ~^(?<gh_repo>[a-z0-9-]+)\.gh\.gmk\.mdevs\.uk$;    # 方案 P 改 \.gh\.p\.gmk
    ssl_certificate /etc/letsencrypt/live/gmk-gh-wildcard/fullchain.pem;   # P：gmk-p-gh-wildcard
    ssl_certificate_key /etc/letsencrypt/live/gmk-gh-wildcard/privkey.pem;
    include /etc/letsencrypt/options-ssl-nginx.conf; ssl_dhparam /etc/letsencrypt/ssl-dhparams.pem;
    resolver 1.1.1.1 8.8.8.8 valid=300s ipv6=off;      # 變數 proxy_pass 必需，否則 nginx -t 失敗
    if ($gh_path = "") { return 404; }                 # 白名單掛勾：未知 repo 直接 404
    location / {
        proxy_pass https://yuhaoliaotony.github.io/$gh_path$request_uri;
        proxy_ssl_server_name on; proxy_ssl_name yuhaoliaotony.github.io;  # 回源 SNI＝Pages host
        proxy_set_header Host yuhaoliaotony.github.io;  # 不可用 $host，Pages 靠 Host 選站
        proxy_set_header X-Forwarded-Host $host; proxy_set_header X-Forwarded-Proto https;
        proxy_redirect https://yuhaoliaotony.github.io/ https://$host/;    # 改寫絕對網址
        proxy_redirect http://yuhaoliaotony.github.io/  https://$host/;
        proxy_read_timeout 30s;                        # /data/latest.json 純 JSON，可直通 200
    }
}
```
## 6 憑證計畫
```bash
sudo certbot certonly --dns-cloudflare --dns-cloudflare-credentials /etc/letsencrypt/cloudflare.ini \
  --dns-cloudflare-propagation-seconds 60 --cert-name gmk-gh-wildcard --key-type ecdsa \
  -d '*.gmk.mdevs.uk' -d '*.gh.gmk.mdevs.uk' -d '*.gh.p.gmk.mdevs.uk'
```
- 一張涵蓋 T+P（帶 `*.gmk.mdevs.uk` 便於未來加單層）；只選一條路可只帶該 SAN。
- **不衝突**：僅新增 `live/gmk-gh-wildcard/` 與 `renewal/gmk-gh-wildcard.conf`，不覆寫既有 12 個 lineage，其他 lineage 也不會動它。
- 續約沿用既有 `certbot.timer`（無 deploy-hook → 換發後需 `systemctl reload nginx`）。
## 7 Cloudflare 記錄（type / name / target / proxied）
- 方案 T：**0 筆**（`*.gmk.mdevs.uk` `A` → `100.97.162.7`，DNS only 已涵蓋）。
- 方案 P：`A` | `*.gh.p` | `1.34.138.73` | **DNS only（Proxied=OFF）**。一律不可 Proxied：CF 無法代理到 100.64/10，且第二/三層萬用字元不在 Universal SSL 範圍。
## 8 風險
- (a) **T 對 Actions 無用**：指向 Tailscale IP，runner 到不了；staleness 檢查必然失敗。**只有 P** 能讓 runner 抓到 `data/latest.json`。
- (b) **憑證層級**：CF Universal SSL 僅涵蓋 apex 與 `*.mdevs.uk`；`*.gh.gmk…`/`*.gh.p…` 若 proxied 必不匹配（需 Advanced Certificate Manager）→ 只能 DNS-only + certbot。
- (c) **Pages 自訂網域未移除**：`api.mdevs.uk` 仍是 custom domain，github.io 全 301 到 NXDOMAIN，代理只會原樣回給使用者。必須先移除。
- (d) **Host/SNI**：漏 `proxy_set_header Host yuhaoliaotony.github.io` 或 `proxy_ssl_server_name on` → 404 / TLS 失敗；漏 `proxy_redirect` → 回應洩漏 github.io 絕對網址。另變數 `proxy_pass` 缺 `resolver` 會 `nginx -t` 失敗、回源 IP 變動受 TTL(300s) 影響。
- (e) **無 443 default_server**：泛解析下任何未建 block 的 Host 落到 cems 站（錯憑證＋錯內容，實測見 §1-6、§3-14）。建議補 catch-all `return 444` / `ssl_reject_handshake`。
- (f) **P 的曝露面**：80/443 已對公網開放（既有 `*.p.gmk` 慣例），新 wildcard 等於全子網域曝給掃描器 → 保留白名單與限流。
## 9 需要使用者做的事（僅 DNS / 授權類）
1. **移除 Pages 自訂網域**：repo → Settings → Pages → Custom domain 清空 `api.mdevs.uk`（並刪 repo 內 CNAME，如之後產生）。
2. **選方案 + Cloudflare（僅 P）**：要讓 Actions / staleness 抓到資料只有方案 P，需新增 `A *.gh.p → 1.34.138.73`，Proxy status 選 **DNS only**。
3. **授權憑證申請**：執行 §6 指令（會寫 `_acme-challenge` TXT，需 `cloudflare.ini` 內 token 具 Zone.DNS 編輯權）。
4. **授權變更 nginx**：建立 §5 檔案後 `nginx -t && systemctl reload nginx`。
5. **確認 repo 對應慣例**：保留 `dailytrain` → `DailyTrainTimetable` 別名，或統一用 repo 真實名稱。
