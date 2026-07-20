#!/usr/bin/env bash
# Stands up a complete, throwaway Reports stack on this machine: an isolated PostgreSQL
# cluster, the .NET API, and the Next.js frontend, with a registered tenant and an access
# token ready to use.
#
#   ./scripts/reports-e2e.sh up        # provision + start everything
#   ./scripts/reports-e2e.sh seed      # (re)run the report seeders against it
#   ./scripts/reports-e2e.sh token     # print the access token (for curl / localStorage)
#   ./scripts/reports-e2e.sh test      # run the Platform suite against the same database
#   ./scripts/reports-e2e.sh backfill  # dry-run the ownerless-report backfill (--apply to write)
#   ./scripts/reports-e2e.sh down      # stop everything and delete the cluster + credentials
#
# Nothing here touches Azure or any shared database. See docs/local-e2e-reports.md for the
# reasoning behind each step and for the manual equivalents.
set -euo pipefail

# ── Configuration ────────────────────────────────────────────────────────────
PG_BIN="${PG_BIN:-/c/Program Files/PostgreSQL/18/bin}"
PG_PORT="${PG_PORT:-55432}"          # deliberately not 5432: never collide with a real local server
API_PORT="${API_PORT:-5177}"
WEB_PORT="${WEB_PORT:-3001}"         # package.json's `dev` script uses 3001
DB_NAME="${DB_NAME:-hrcloud_e2e}"
DB_USER="${DB_USER:-testadmin}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORK="${WORK:-$ROOT/.e2e}"           # cluster data, logs and the token live here; gitignored
DATA="$WORK/pgdata"
CONN="Host=localhost;Port=$PG_PORT;Database=$DB_NAME;Username=$DB_USER"

EMAIL="${EMAIL:-admin@sanad.test}"
PASSWORD="${PASSWORD:-Passw0rd!23}"
COMPANY="${COMPANY:-Sanad E2E}"

# The test projects target net8.0 but this machine may only have the .NET 10 runtime; without
# roll-forward every `dotnet test` / `dotnet ef` aborts with "You must install or update .NET".
export DOTNET_ROLL_FORWARD=Major

say() { printf '\n\033[1m▸ %s\033[0m\n' "$*"; }
die() { printf '\033[31m✗ %s\033[0m\n' "$*" >&2; exit 1; }

psql_() { "$PG_BIN/psql.exe" -h localhost -p "$PG_PORT" -U "$DB_USER" "$@"; }

wait_for() { # wait_for <url> <seconds> <label>
  local url=$1 secs=$2 label=$3
  for _ in $(seq 1 "$secs"); do
    curl -sf "$url" >/dev/null 2>&1 && { echo "  $label is up"; return 0; }
    sleep 1
  done
  die "$label did not come up within ${secs}s — check $WORK/*.log"
}

# ── up ───────────────────────────────────────────────────────────────────────
cmd_up() {
  [ -d "$PG_BIN" ] || die "PostgreSQL binaries not found at '$PG_BIN'. Set PG_BIN."
  mkdir -p "$WORK"

  say "Creating an isolated PostgreSQL cluster on :$PG_PORT"
  # A fresh cluster rather than the machine's existing server: no password to discover, no shared
  # state, and `down` can delete the whole thing. trust auth is safe because it listens on
  # localhost only and holds nothing but throwaway data.
  rm -rf "$DATA"; mkdir -p "$DATA"
  "$PG_BIN/initdb.exe" -D "$DATA" -U "$DB_USER" --auth=trust --encoding=UTF8 --locale=C >"$WORK/initdb.log" 2>&1 \
    || die "initdb failed — see $WORK/initdb.log"
  "$PG_BIN/pg_ctl.exe" -D "$DATA" -l "$WORK/pg.log" \
    -o "-p $PG_PORT -c listen_addresses=localhost" start >/dev/null 2>&1 &
  sleep 5
  psql_ -d postgres -c "SELECT 1;" >/dev/null 2>&1 || die "cluster did not start — see $WORK/pg.log"
  psql_ -d postgres -c "CREATE DATABASE $DB_NAME;" >/dev/null
  echo "  $DB_NAME created"

  say "Applying migrations"
  ( cd "$ROOT/backend" && ConnectionStrings__DefaultConnection="$CONN" \
      dotnet ef database update \
        --project src/HR.Infrastructure/HR.Infrastructure.csproj \
        --startup-project src/HR.Api/HR.Api.csproj ) >"$WORK/migrate.log" 2>&1 \
    || die "migrations failed — see $WORK/migrate.log"
  echo "  $(grep -c 'Applying migration' "$WORK/migrate.log" || echo 0) migrations applied"

  say "Starting the API on :$API_PORT"
  # Cors__Origins__0 matters: appsettings.json only allows http://localhost:3000, while `next dev`
  # serves on 3001. Without this every browser call fails preflight and the UI looks empty.
  ( cd "$ROOT/backend/src/HR.Api" && \
      ConnectionStrings__DefaultConnection="$CONN" \
      ConnectionStrings__Redis="" \
      ASPNETCORE_ENVIRONMENT=Development \
      ASPNETCORE_URLS="http://localhost:$API_PORT" \
      Cors__Origins__0="http://localhost:$WEB_PORT" \
      Cors__Origins__1="http://localhost:3000" \
      dotnet run --no-launch-profile ) >"$WORK/api.log" 2>&1 &
  wait_for "http://localhost:$API_PORT/swagger/index.html" 180 "API"

  say "Registering the tenant and admin user"
  # /api/auth/register bootstraps tenant + admin in one call; there is no other way to get a
  # usable account on an empty database.
  curl -s -X POST "http://localhost:$API_PORT/api/auth/register" \
    -H "Content-Type: application/json" \
    -d "{\"companyName\":\"$COMPANY\",\"fullName\":\"Admin User\",\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}" \
    > "$WORK/register.json"
  python3 - "$WORK/register.json" "$WORK/token.txt" <<'PY' || die "registration failed — see $WORK/register.json"
import json, sys
d = json.load(open(sys.argv[1], encoding="utf-8"))
data = d.get("data") or {}
tok = data.get("accessToken") or data.get("token")
if not tok:
    raise SystemExit(1)
open(sys.argv[2], "w", encoding="utf-8").write(tok)
PY
  echo "  $EMAIL registered; token written to $WORK/token.txt"

  cmd_seed

  say "Starting the frontend on :$WEB_PORT"
  ( cd "$ROOT" && NEXT_PUBLIC_API_URL="http://localhost:$API_PORT" \
      npx next dev --port "$WEB_PORT" ) >"$WORK/next.log" 2>&1 &
  wait_for "http://localhost:$WEB_PORT" 180 "frontend"

  cat <<EOF

  Ready.
    Frontend  http://localhost:$WEB_PORT/reports
    API       http://localhost:$API_PORT/swagger
    Database  $CONN

  The UI reads its token from localStorage. In DevTools on http://localhost:$WEB_PORT:

    localStorage.setItem('hr_access_token', '<paste $WORK/token.txt>');
    localStorage.setItem('hr_refresh_token', '<same>');
    localStorage.setItem('hr_user', JSON.stringify({id:'0',email:'$EMAIL',fullName:'Admin User',permissions:[]}));
    location.reload();

  Or headlessly, before any app code runs:
    page.addInitScript(t => localStorage.setItem('hr_access_token', t), token)

  Tear down with:  ./scripts/reports-e2e.sh down
EOF
}

# ── seed ─────────────────────────────────────────────────────────────────────
cmd_seed() {
  [ -f "$WORK/token.txt" ] || die "no token — run 'up' first"
  say "Seeding reportable objects and report definitions"
  local t; t=$(cat "$WORK/token.txt")
  # Order matters: registering the objects first is what lets master-data references
  # (leave type, nationality, request type) resolve to names instead of raw GUIDs.
  for ep in objects/seed-reportable reports/seed-system reports/seed-defaults; do
    printf '  %-26s ' "$ep"
    curl -s -o /dev/null -w '%{http_code}\n' -X POST \
      -H "Authorization: Bearer $t" -H "Content-Type: application/json" \
      "http://localhost:$API_PORT/api/platform/$ep"
  done
  psql_ -d "$DB_NAME" -t -A -c 'SELECT count(*) FROM engine_report_definitions;' \
    | tr -d '\r' | sed 's/^/  report definitions: /'
}

# ── token ────────────────────────────────────────────────────────────────────
cmd_token() { [ -f "$WORK/token.txt" ] || die "no token — run 'up' first"; cat "$WORK/token.txt"; echo; }

# ── backfill ─────────────────────────────────────────────────────────────────
# Shows exactly what the ownerless-report backfill would do. Dry run unless --apply is
# passed, and even then nothing is guessed: unresolved reports are listed for a human.
cmd_backfill() {
  [ -f "$WORK/token.txt" ] || die "no token — run 'up' first"
  local dry=true; [ "${1:-}" = "--apply" ] && dry=false
  local t; t=$(cat "$WORK/token.txt")

  say "$( [ "$dry" = true ] && echo 'Dry run:' || echo 'Applying:') report owner backfill"
  # Response to a file, program via heredoc: piping curl into `python3 - <<'PY'` does not work,
  # because the heredoc *is* stdin and shadows the pipe.
  curl -s -X POST -H "Authorization: Bearer $t" \
    "http://localhost:$API_PORT/api/platform/reports/backfill-owners?dryRun=$dry" \
    > "$WORK/backfill.json"
  python3 - "$WORK/backfill.json" <<'PY'
import json, sys
# Report names are Arabic; a Windows console defaults to cp1252 and raises UnicodeEncodeError
# partway through the listing, which looks like the backfill itself crashed.
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
d = json.load(open(sys.argv[1], encoding="utf-8"))
if not d.get("success"):
    print("  request failed:", d.get("message")); raise SystemExit(1)
r = d["data"]
REASON = {
    "SystemManaged":   "system-managed (clone-only)",
    "NoCreatedBy":     "no CreatedBy recorded",
    "CreatorNotFound": "creator not found in this tenant",
    "CreatorAmbiguous":"CreatedBy matches several accounts",
}
print(f"  mode: {'DRY RUN' if r['dryRun'] else 'APPLIED'}   ownerless scanned: {r['scannedOwnerless']}")

print(f"\n  WILL RECEIVE AN OWNER ({len(r['assigned'])})")
for a in r["assigned"] or []:
    print(f"    {a['code']:<24} {a['name'][:28]:<30} -> {a['ownerEmail']}")
if not r["assigned"]: print("    (none)")

print(f"\n  SYSTEM, STAYING OWNERLESS ({len(r['systemManaged'])})")
for s in r["systemManaged"] or []:
    print(f"    {s['code']:<24} {s['name'][:28]:<30}    clone to customise")
if not r["systemManaged"]: print("    (none)")

print(f"\n  NEEDS AN ADMINISTRATOR ({len(r['unresolved'])})")
for s in r["unresolved"] or []:
    print(f"    {s['code']:<24} {s['name'][:28]:<30}    {REASON.get(s['reason'], s['reason'])}"
          + (f"  [CreatedBy: {s['createdBy']}]" if s.get("createdBy") else ""))
if not r["unresolved"]: print("    (none)")

if r["dryRun"] and r["assigned"]:
    print("\n  Re-run with --apply to write these.")
PY
}

# ── test ─────────────────────────────────────────────────────────────────────
cmd_test() {
  say "Running the Platform suite against $DB_NAME"
  # The DB-backed tests are SkippableFact on REPORTS_TEST_DB — without it they skip silently
  # and a green run proves nothing about the report engine.
  ( cd "$ROOT/backend" && REPORTS_TEST_DB="$CONN" \
      dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --nologo -v q )
}

# ── down ─────────────────────────────────────────────────────────────────────
cmd_down() {
  say "Stopping services"
  for port in "$API_PORT" "$WEB_PORT"; do
    powershell -NoProfile -Command \
      "Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id \$_.OwningProcess -Force -ErrorAction SilentlyContinue }" \
      >/dev/null 2>&1 || true
    echo "  :$port stopped"
  done

  if [ -d "$DATA" ]; then
    "$PG_BIN/pg_ctl.exe" -D "$DATA" -m immediate stop >/dev/null 2>&1 || true
    sleep 2
    echo "  cluster stopped"
  fi

  say "Deleting the cluster, logs and credentials"
  # The token is a real bearer credential for the throwaway tenant; it does not outlive the run.
  rm -rf "$WORK"
  echo "  $WORK removed"
}

case "${1:-}" in
  up)       cmd_up ;;
  seed)     cmd_seed ;;
  token)    cmd_token ;;
  test)     cmd_test ;;
  backfill) shift; cmd_backfill "${1:-}" ;;
  down)     cmd_down ;;
  *) sed -n '2,12p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 1 ;;
esac
