#!/usr/bin/env bash
# Shield end-to-end smoke test.
# Boots the API in single-user mode, walks the public surface, tears down.
# Exit 0 on success, non-zero with a diagnostic on first failure.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

PORT="${SHIELD_SMOKE_PORT:-5099}"
BASE="http://localhost:${PORT}"
FIXTURE_DIR="$REPO_ROOT/tests/fixtures/smoke-fixture"
# SQLite resolves "Data Source=data/..." relative to the process CWD; we run the
# API from the project dir, so the data dir lands under src/Shield.Api/data.
DATA_DIR="$REPO_ROOT/src/Shield.Api/data"
LOG_FILE="$REPO_ROOT/shield-smoke.log"
PID_FILE="$REPO_ROOT/.shield-smoke.pid"
COOKIE_JAR="$REPO_ROOT/.shield-smoke.cookies"
SERVER_PID=""

log()  { printf '\033[36m[smoke]\033[0m %s\n' "$*"; }
warn() { printf '\033[33m[smoke]\033[0m %s\n' "$*" >&2; }
fail() { printf '\033[31m[smoke FAIL]\033[0m %s\n' "$*" >&2; exit 1; }

cleanup() {
  local status=$?
  if [[ -n "$SERVER_PID" ]] && kill -0 "$SERVER_PID" 2>/dev/null; then
    log "stopping API (pid $SERVER_PID)"
    kill "$SERVER_PID" 2>/dev/null || true
    for _ in 1 2 3 4 5; do
      kill -0 "$SERVER_PID" 2>/dev/null || break
      sleep 1
    done
    kill -9 "$SERVER_PID" 2>/dev/null || true
  fi
  rm -f "$PID_FILE" "$COOKIE_JAR"
  if [[ -d "$DATA_DIR" ]]; then
    log "removing smoke data dir $DATA_DIR"
    # SQLite WAL holds the file briefly after process exit on Windows; retry a few times.
    for attempt in 1 2 3 4 5; do
      if rm -rf "$DATA_DIR" 2>/dev/null; then
        break
      fi
      sleep 1
    done
    [[ -d "$DATA_DIR" ]] && warn "could not fully remove $DATA_DIR (file lock held by external process)"
  fi
  if [[ $status -ne 0 && -f "$LOG_FILE" ]]; then
    warn "last 60 lines of $LOG_FILE:"
    tail -n 60 "$LOG_FILE" >&2 || true
  fi
  exit $status
}
trap cleanup EXIT INT TERM

require() {
  command -v "$1" >/dev/null 2>&1 || fail "required tool '$1' not on PATH"
}

require curl
require dotnet
require node
require npm
require python3 || true   # python used only for tiny JSON helpers when jq missing

JQ_BIN=""
if command -v jq >/dev/null 2>&1; then
  JQ_BIN="jq"
fi

log "step 1/9 — building Shield.Web SPA"
pushd src/Shield.Web >/dev/null
# npm ci wipes node_modules; on Windows that races with locked .node binaries from
# any concurrent Node process. Fall through to npm install if ci can't clean up.
if ! npm ci; then
  warn "npm ci failed (often a Windows file-lock); falling back to npm install"
  npm install
fi
npm run build
popd >/dev/null

log "step 2/9 — starting Shield.Api on $BASE"
: > "$LOG_FILE"
# Smoke runs the published DLL directly (no `dotnet run` wrapper) so the PID we
# trap is the actual host process, not a parent that orphans its socket-owning
# child on Windows. Production env matches the container behaviour from release.yml
# and avoids ASP.NET's Development scope validator tripping on a known Singleton-
# vs-Scoped channel registration (see report).
API_DLL="$REPO_ROOT/src/Shield.Api/bin/Release/net9.0/Shield.Api.dll"
API_PROJECT_DIR="$REPO_ROOT/src/Shield.Api"
[[ -f "$API_DLL" ]] || fail "expected built API at $API_DLL — run 'dotnet build -c Release' first"
mkdir -p "$DATA_DIR"
# Launch dotnet directly (no `( cd && ... ) &` subshell) so $! is the actual host
# process; otherwise we trap a sh wrapper and orphan the socket-owning child on
# Windows. pushd/popd handles CWD without forking.
pushd "$API_PROJECT_DIR" >/dev/null
ASPNETCORE_ENVIRONMENT=Production \
  dotnet "$API_DLL" --urls "$BASE" --contentRoot "$API_PROJECT_DIR" \
  >"$LOG_FILE" 2>&1 &
SERVER_PID=$!
popd >/dev/null
echo "$SERVER_PID" > "$PID_FILE"
log "  api pid=$SERVER_PID, logs=$LOG_FILE"

log "step 3/9 — waiting up to 30s for /healthz"
for attempt in $(seq 1 30); do
  if curl -sf "$BASE/healthz" >/dev/null 2>&1; then
    log "  healthz ok after ${attempt}s"
    break
  fi
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then
    fail "API process died during startup (see $LOG_FILE)"
  fi
  sleep 1
  if [[ "$attempt" -eq 30 ]]; then
    fail "/healthz never returned 200 within 30s"
  fi
done

log "step 4/9 — GET /api/auth/me (single-user mode)"
me_body="$(curl -sf -c "$COOKIE_JAR" -b "$COOKIE_JAR" "$BASE/api/auth/me")" \
  || fail "GET /api/auth/me did not return 200"
log "  body: $me_body"
if [[ -n "$JQ_BIN" ]]; then
  username="$(printf '%s' "$me_body" | $JQ_BIN -r '.username // empty')"
  single="$(printf '%s' "$me_body" | $JQ_BIN -r '.singleUserMode // empty')"
else
  username="$(printf '%s' "$me_body" | python3 -c "import json,sys; print(json.load(sys.stdin).get('username') or '')")"
  single="$(printf '%s' "$me_body"  | python3 -c "import json,sys; print(json.load(sys.stdin).get('singleUserMode') or '')")"
fi
[[ -n "$username" ]] || fail "/api/auth/me returned empty username"
[[ "$single" == "true" || "$single" == "True" ]] || fail "/api/auth/me did not report singleUserMode=true (got: $single)"
log "  authenticated as '$username' (singleUserMode=$single)"

log "step 5/9 — POST /api/sources (LocalFolder pointing at fixture)"
# Git-Bash on Windows hands us a /c/... path the .NET process can't open; convert
# back to a native path before serializing into the LocalFolderConfig.
FIXTURE_PATH="$FIXTURE_DIR"
if command -v cygpath >/dev/null 2>&1; then
  FIXTURE_PATH="$(cygpath -w "$FIXTURE_DIR")"
fi
# SourceType.LocalFolder = 1. ScanInterval is TimeSpan ("hh:mm:ss"). ConfigJson is a *string*
# containing the LocalFolderConfig payload, so we escape it.
fixture_json="$(printf '{"path":"%s"}' "${FIXTURE_PATH//\\/\\\\}")"
escaped_fixture_json="$(printf '%s' "$fixture_json" | python3 -c 'import json,sys; print(json.dumps(sys.stdin.read()))')"
create_body=$(cat <<EOF
{
  "type": 1,
  "name": "smoke-fixture",
  "configJson": ${escaped_fixture_json},
  "scanInterval": "01:00:00",
  "enabled": true
}
EOF
)
create_resp="$(curl -sf -X POST -H 'Content-Type: application/json' \
  -c "$COOKIE_JAR" -b "$COOKIE_JAR" \
  --data "$create_body" "$BASE/api/sources")" \
  || fail "POST /api/sources failed"
if [[ -n "$JQ_BIN" ]]; then
  source_id="$(printf '%s' "$create_resp" | $JQ_BIN -r '.id')"
else
  source_id="$(printf '%s' "$create_resp" | python3 -c "import json,sys; print(json.load(sys.stdin)['id'])")"
fi
[[ "$source_id" =~ ^[0-9]+$ ]] || fail "create response missing numeric id: $create_resp"
log "  created source id=$source_id"

log "step 6/9 — POST /api/sources/$source_id/scan-now"
http_code="$(curl -s -o /dev/null -w '%{http_code}' -X POST \
  -c "$COOKIE_JAR" -b "$COOKIE_JAR" \
  "$BASE/api/sources/$source_id/scan-now")"
[[ "$http_code" == "202" ]] || fail "scan-now returned HTTP $http_code (expected 202)"

log "step 7/9 — waiting up to 15s for snapshot"
snapshot_ok=0
for attempt in $(seq 1 15); do
  detail="$(curl -sf -c "$COOKIE_JAR" -b "$COOKIE_JAR" "$BASE/api/sources/$source_id")" || true
  if [[ -n "$detail" ]]; then
    if [[ -n "$JQ_BIN" ]]; then
      snap_id="$(printf '%s' "$detail" | $JQ_BIN -r '.lastSnapshot.id // empty')"
      item_count="$(printf '%s' "$detail" | $JQ_BIN -r '.lastSnapshot.itemCount // empty')"
    else
      snap_id="$(printf '%s' "$detail" | python3 -c "import json,sys; d=json.load(sys.stdin); s=d.get('lastSnapshot'); print((s or {}).get('id') or '')")"
      item_count="$(printf '%s' "$detail" | python3 -c "import json,sys; d=json.load(sys.stdin); s=d.get('lastSnapshot'); print((s or {}).get('itemCount') or '')")"
    fi
    if [[ -n "$snap_id" && "$snap_id" != "null" ]]; then
      log "  snapshot $snap_id with $item_count item(s) after ${attempt}s"
      snapshot_ok=1
      break
    fi
  fi
  sleep 1
done
[[ "$snapshot_ok" -eq 1 ]] || fail "source $source_id never produced a snapshot within 15s"

if [[ -n "${DISCORD_WEBHOOK_URL:-}" ]]; then
  log "step 8/9 — DISCORD_WEBHOOK_URL set, creating Discord channel"
  webhook_payload_json="$(printf '{"webhookUrl":"%s"}' "$DISCORD_WEBHOOK_URL")"
  escaped_webhook="$(printf '%s' "$webhook_payload_json" | python3 -c 'import json,sys; print(json.dumps(sys.stdin.read()))')"
  ch_body=$(cat <<EOF
{
  "type": 0,
  "name": "smoke-discord",
  "configJson": ${escaped_webhook},
  "minSeverity": 0,
  "enabled": true
}
EOF
)
  ch_resp="$(curl -sf -X POST -H 'Content-Type: application/json' \
    -c "$COOKIE_JAR" -b "$COOKIE_JAR" \
    --data "$ch_body" "$BASE/api/channels")" \
    || fail "POST /api/channels failed"
  if [[ -n "$JQ_BIN" ]]; then
    ch_id="$(printf '%s' "$ch_resp" | $JQ_BIN -r '.id')"
  else
    ch_id="$(printf '%s' "$ch_resp" | python3 -c "import json,sys; print(json.load(sys.stdin)['id'])")"
  fi
  [[ -n "$ch_id" && "$ch_id" != "null" ]] || fail "channel create response missing id: $ch_resp"
  log "  created channel id=$ch_id"

  log "  POST /api/channels/$ch_id/test-send"
  test_code="$(curl -s -o /dev/null -w '%{http_code}' -X POST \
    -c "$COOKIE_JAR" -b "$COOKIE_JAR" \
    "$BASE/api/channels/$ch_id/test-send")"
  [[ "$test_code" == "200" ]] || fail "test-send returned HTTP $test_code (expected 200)"
  log "  discord test-send ok"
else
  log "step 8/9 — DISCORD_WEBHOOK_URL not set, skipping Discord channel check"
fi

log "step 9/9 — done, smoke PASSED"
exit 0
