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
#   GITHUB_REPOSITORY   owner/repo，用於推導 Pages URL。
#                       GitHub Actions 會自動提供；本機執行時可自行 export。
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

# 由 owner/repo 推導 GitHub Pages 的 latest.json URL（不寫死 owner/repo）。
resolve_latest_url() {
  if [[ -n "${LATEST_URL:-}" ]]; then
    printf '%s' "$LATEST_URL"
    return 0
  fi
  [[ -n "$REPO_SLUG" ]] || return 1

  # Pages 主機名稱不分大小寫，統一轉小寫讓 log 與實際 URL 一致。
  local owner name
  owner="$(printf '%s' "${REPO_SLUG%%/*}" | tr '[:upper:]' '[:lower:]')"
  name="$(printf '%s' "${REPO_SLUG##*/}" | tr '[:upper:]' '[:lower:]')"
  if [[ -z "$owner" || -z "$name" || "$owner" == "$name" ]]; then
    return 1
  fi

  # 使用者/組織頁面 repo（<owner>.github.io）的站台根目錄就是 repo 根目錄。
  if [[ "$name" == *.github.io ]]; then
    printf 'https://%s/data/latest.json' "$name"
  else
    printf 'https://%s.github.io/%s/data/latest.json' "$owner" "$name"
  fi
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

latest_file=""
source=""

latest_url="$(resolve_latest_url || true)"
if [[ -n "$latest_url" ]]; then
  log "抓取 $latest_url"
  if fetch_latest "$latest_url" "$tmpdir/latest.json"; then
    latest_file="$tmpdir/latest.json"
    source="pages"
  else
    log "遠端抓取失敗，改用本機 fallback：$LOCAL_LATEST"
  fi
else
  log "未設定 LATEST_URL 且無法由 GITHUB_REPOSITORY 推導 Pages URL，改用本機 fallback：$LOCAL_LATEST"
fi

if [[ -z "$latest_file" ]]; then
  [[ -f "$LOCAL_LATEST" ]] || fail_error "遠端 latest.json 抓取失敗，且本機 fallback 不存在（$LOCAL_LATEST）"
  latest_file="$LOCAL_LATEST"
  source="local"
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
