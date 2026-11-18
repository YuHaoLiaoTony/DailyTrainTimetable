#!/usr/bin/env bash
#
# check-staleness.sh — 判斷「已發布」的時刻表資料是否過期。
#
# 讀取 GitHub Pages 上的 data/latest.json（或本機 output/data/latest.json），
# 比較其 updatedAt 與現在時間的差距；超過門檻就 exit 1，讓 workflow 失敗。
#
# 用法：
#   scripts/check-staleness.sh
#   MAX_AGE_HOURS=48 scripts/check-staleness.sh
#   LATEST_URL=https://example.github.io/repo/data/latest.json scripts/check-staleness.sh
#
# 環境變數：
#   MAX_AGE_HOURS       允許的最大資料年齡（小時），預設 27。
#                       每日排程 05:00 (Asia/Taipei) 更新一次，加上 GitHub 排程
#                       延遲與 Pages 部署時間緩衝 3 小時 → 27 小時為容許上限。
#   LATEST_URL          直接指定 latest.json 的完整 URL（優先於自動推導）。
#                       空字串會被忽略，方便 workflow 直接傳 vars.LATEST_URL。
#   GITHUB_REPOSITORY   owner/repo，用於推導 Pages URL。
#                       GitHub Actions 會自動提供；本機執行時會退回 git remote origin。
#
# latest.json 的來源依序嘗試，第一個抓得到且解析成功的就用：
#   1. $LATEST_URL
#   2. `gh api repos/$GITHUB_REPOSITORY/pages --jq .html_url`（自訂網域最可靠）
#   3. repo 內的 CNAME / docs/CNAME（Pages 自訂網域）
#   4. https://<owner>.github.io/<repo>（未設自訂網域時的預設站台）
#   5. 本機 fallback 檔案 $LOCAL_LATEST
# 全部失敗才 exit 2，並列出所有試過的候選網址。
#   LOCAL_LATEST        遠端抓取失敗時的 fallback 檔案，預設 output/data/latest.json。
#   GITHUB_OUTPUT       若有設定，會寫入 status / hours_behind / updated_at /
#                       latest_date / source / detail 供 workflow 後續步驟使用。
#
# 結束代碼：
#   0 = 資料新鮮（未超過門檻）
#   1 = 資料過期
#   2 = 無法取得或解析資料（同樣視為需要告警）
#
set -euo pipefail

MAX_AGE_HOURS="${MAX_AGE_HOURS:-27}"
LOCAL_LATEST="${LOCAL_LATEST:-output/data/latest.json}"
REPO_SLUG="${GITHUB_REPOSITORY:-}"

tmpdir="$(mktemp -d)"
cleanup() { rm -rf "$tmpdir"; }
trap cleanup EXIT

log() { printf '[staleness] %s\n' "$*"; }

set_output() {
  if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
    printf '%s=%s\n' "$1" "$2" >>"$GITHUB_OUTPUT"
  fi
}

# 無法判定新鮮度時統一走這裡：仍然輸出可供 issue 使用的說明，並以 2 結束。
fail_error() {
  local detail="$1"
  set_output status "error"
  set_output hours_behind ""
  set_output updated_at ""
  set_output latest_date ""
  set_output source ""
  set_output detail "$detail"
  log "無法判定資料新鮮度：$detail"
  exit 2
}

# 用 gh 取 Pages 站台網址（自訂網域時只有這個方法可靠）。
# gh 不存在、未登入或無權限都只是跳過，不影響腳本後續推導。
pages_url_from_gh() {
  command -v gh >/dev/null 2>&1 || return 1
  [[ -n "$REPO_SLUG" ]] || return 1
  gh api "repos/$REPO_SLUG/pages" --jq '.html_url' 2>/dev/null || return 1
}

# 由 repo 內的 CNAME / docs/CNAME 取得 Pages 自訂網域。
host_from_cname() {
  local file host
  for file in CNAME docs/CNAME; do
    [[ -f "$file" ]] || continue
    host="$(grep -v '^[[:space:]]*$' "$file" 2>/dev/null | head -n 1 | tr -d '[:space:]')"
    if [[ -n "$host" ]]; then
      printf '%s' "$host"
      return 0
    fi
  done
  return 1
}

# 由 owner/repo 推導 GitHub Pages 預設站台網址（不寫死 owner/repo）。
# 注意：設定自訂網域後，這個網址只會 301 轉到自訂網域，通常抓不到內容。
pages_host_from_slug() {
  [[ -n "$REPO_SLUG" ]] || return 1

  # Pages 主機名稱（owner 部分）不分大小寫，統一轉小寫；
  # repo 名稱是 URL 路徑的一部分，大小寫敏感，必須保留原樣。
  local owner name
  owner="$(printf '%s' "${REPO_SLUG%%/*}" | tr '[:upper:]' '[:lower:]')"
  name="${REPO_SLUG##*/}"
  if [[ -z "$owner" || -z "$name" || "$owner" == "$name" ]]; then
    return 1
  fi

  # 使用者/組織頁面 repo（<owner>.github.io）的站台根目錄就是 repo 根目錄。
  if [[ "$name" == *.github.io ]]; then
    printf 'https://%s' "$name"
  else
    printf 'https://%s.github.io/%s' "$owner" "$name"
  fi
}

# 收集候選 latest.json URL（去重、保留順序）。
add_candidate() {
  local url="$1" existing
  [[ -n "$url" ]] || return 0
  if [[ ${#candidate_urls[@]} -gt 0 ]]; then
    for existing in "${candidate_urls[@]}"; do
      [[ "$existing" == "$url" ]] && return 0
    done
  fi
  candidate_urls+=("$url")
}

fetch_latest() {
  local url="$1" dest="$2"
  curl --fail --silent --show-error --location \
    --header 'Cache-Control: no-cache' \
    --retry 3 --retry-delay 5 --max-time 30 \
    --output "$dest" "$url"
}

# 解析 latest.json，輸出兩行：updatedAt、availableDates 的最大值。
parse_latest() {
  python3 - "$1" <<'PY'
import json
import sys

path = sys.argv[1]
try:
    with open(path, encoding="utf-8") as handle:
        data = json.load(handle)
except Exception as exc:  # noqa: BLE001 - 任何解析失敗都要轉成清楚的訊息
    sys.stderr.write(f"讀取或解析失敗: {exc}")
    sys.exit(3)

updated_at = data.get("updatedAt")
if not isinstance(updated_at, str) or not updated_at.strip():
    sys.stderr.write("latest.json 缺少 updatedAt 欄位或型別不是字串")
    sys.exit(4)

dates = data.get("availableDates") or []
latest_date = max(dates) if isinstance(dates, list) and dates else ""

print(updated_at.strip())
print(latest_date)
PY
}

# 計算 updatedAt 到現在經過多少小時（保留一位小數）。
hours_since() {
  python3 - "$1" <<'PY'
import sys
from datetime import datetime, timezone

raw = sys.argv[1].strip()
if raw.endswith(("Z", "z")):
    raw = raw[:-1] + "+00:00"
try:
    timestamp = datetime.fromisoformat(raw)
except ValueError:
    sys.stderr.write(f"無法解析時間戳: {raw}")
    sys.exit(3)

if timestamp.tzinfo is None:
    timestamp = timestamp.replace(tzinfo=timezone.utc)

delta = datetime.now(timezone.utc) - timestamp
print(f"{delta.total_seconds() / 3600:.1f}")
PY
}

# --- 前置檢查 ---------------------------------------------------------------

if ! [[ "$MAX_AGE_HOURS" =~ ^[0-9]+([.][0-9]+)?$ ]]; then
  fail_error "MAX_AGE_HOURS 必須是數字（目前為「$MAX_AGE_HOURS」）"
fi
if ! command -v python3 >/dev/null 2>&1; then
  fail_error "找不到 python3，無法解析 latest.json"
fi

# --- 取得 latest.json -------------------------------------------------------

# 本機執行時 GITHUB_REPOSITORY 通常沒設定，退回 git remote origin 以取得 owner/repo。
if [[ -z "$REPO_SLUG" ]] && command -v git >/dev/null 2>&1; then
  remote_url="$(git config --get remote.origin.url 2>/dev/null || true)"
  case "$remote_url" in
    git@github.com:*) REPO_SLUG="${remote_url#git@github.com:}" ;;
    ssh://git@github.com/*) REPO_SLUG="${remote_url#ssh://git@github.com/}" ;;
    https://github.com/*) REPO_SLUG="${remote_url#https://github.com/}" ;;
  esac
  REPO_SLUG="${REPO_SLUG%.git}"
  if [[ -n "$REPO_SLUG" ]]; then
    log "GITHUB_REPOSITORY 未設定，改用 git remote origin：$REPO_SLUG"
  fi
fi

latest_file=""
source=""
candidate_urls=()
attempts=()

# 1. 明確指定的 LATEST_URL（空字串視同未設定）。
add_candidate "${LATEST_URL:-}"

# 2. GitHub Pages API 的站台網址，設定自訂網域時這是最可靠的來源。
if pages_url="$(pages_url_from_gh)" && [[ -n "$pages_url" ]]; then
  log "gh api pages 站台網址：$pages_url"
  add_candidate "${pages_url%/}/data/latest.json"
fi

# 3. repo 內的 CNAME（Pages 自訂網域）。
if cname_host="$(host_from_cname)"; then
  log "CNAME 自訂網域：$cname_host"
  add_candidate "https://${cname_host}/data/latest.json"
fi

# 4. 預設的 <owner>.github.io/<repo>。
if pages_host="$(pages_host_from_slug)"; then
  add_candidate "${pages_host}/data/latest.json"
fi

for url in ${candidate_urls[@]+"${candidate_urls[@]}"}; do
  log "嘗試 $url"
  if ! fetch_latest "$url" "$tmpdir/latest.json" 2>"$tmpdir/curl.err"; then
    attempts+=("$url（curl 失敗：$(tr '\n' ' ' <"$tmpdir/curl.err")）")
    continue
  fi
  # 抓得到但內容解析不了（例如錯誤頁）也視為失敗，換下一個候選。
  if ! parse_latest "$tmpdir/latest.json" >/dev/null 2>"$tmpdir/parse.err"; then
    attempts+=("$url（內容無法解析：$(tr '\n' ' ' <"$tmpdir/parse.err")）")
    continue
  fi
  latest_file="$tmpdir/latest.json"
  source="pages"
  log "成功：$url"
  break
done

if [[ -z "$latest_file" ]]; then
  if [[ -f "$LOCAL_LATEST" ]]; then
    latest_file="$LOCAL_LATEST"
    source="local"
  else
    tried=""
    if [[ ${#attempts[@]} -gt 0 ]]; then
      tried="$(printf '%s; ' "${attempts[@]}")"
    fi
    fail_error "所有候選來源都失敗，且本機 fallback 不存在（$LOCAL_LATEST）。試過：${tried:-無候選網址}"
  fi
fi

log "使用來源：$source（$latest_file）"

# --- 解析與判定 -------------------------------------------------------------

if ! parsed="$(parse_latest "$latest_file" 2>"$tmpdir/parse.err")"; then
  fail_error "無法解析 latest.json（來源 $source）：$(tr '\n' ' ' <"$tmpdir/parse.err")"
fi
updated_at="$(printf '%s' "$parsed" | sed -n '1p')"
latest_date="$(printf '%s' "$parsed" | sed -n '2p')"

if ! hours_behind="$(hours_since "$updated_at" 2>"$tmpdir/time.err")"; then
  fail_error "updatedAt 格式無法解析（$updated_at）：$(tr '\n' ' ' <"$tmpdir/time.err")"
fi

msg="updatedAt=$updated_at，已落後 ${hours_behind} 小時（門檻 ${MAX_AGE_HOURS} 小時），來源=$source，最新資料日期=${latest_date:-unknown}"

if awk -v hours="$hours_behind" -v limit="$MAX_AGE_HOURS" 'BEGIN { exit !(hours > limit) }'; then
  set_output status "stale"
  set_output hours_behind "$hours_behind"
  set_output updated_at "$updated_at"
  set_output latest_date "$latest_date"
  set_output source "$source"
  set_output detail "資料已過期：$msg"
  log "資料已過期：$msg"
  exit 1
fi

set_output status "fresh"
set_output hours_behind "$hours_behind"
set_output updated_at "$updated_at"
set_output latest_date "$latest_date"
set_output source "$source"
set_output detail "資料仍在容許範圍：$msg"
log "資料仍在容許範圍：$msg"
exit 0
