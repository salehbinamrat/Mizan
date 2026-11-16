#!/usr/bin/env bash
# Runs only on disposable PostgreSQL CI. Tests real pg_dump -> .mizanbak -> pg_restore.
set -euo pipefail
BASE="${MIZAN_TEST_URL:-http://127.0.0.1:5081}"
: "${MIZAN_TEST_ADMIN_USERNAME:?}"
: "${MIZAN_TEST_ADMIN_PASSWORD:?}"
: "${MIZAN_TEST_HOSPITAL_NAME:?}"
temp="$(mktemp -d)"
chmod 700 "$temp"
trap 'rm -rf "$temp"' EXIT

# Isolate the restore login from the smoke suite rate limiter on loopback.
ownerToken=$(curl --interface 127.0.0.3 --fail-with-body --silent --show-error \
  -H 'Content-Type: application/json' -X POST "$BASE/api/auth/login" \
  -d "$(jq -n --arg u "$MIZAN_TEST_ADMIN_USERNAME" --arg p "$MIZAN_TEST_ADMIN_PASSWORD" \
  --arg h "$MIZAN_TEST_HOSPITAL_NAME" '{username:$u,password:$p,hospitalName:$h}')" | jq -r .token)
[[ ${#ownerToken} == 64 ]] || { echo 'Owner login failed for backup restore test'; exit 1; }
export MIZAN_RESTORE_PASSPHRASE='ci-only-mizan-restore-passphrase-2026!'
export MIZAN_RESTORE_CONFIRM='YES_DISPOSABLE'
export PGPASSWORD='ci-only-password' PGHOST='127.0.0.1' PGPORT='5432' PGUSER='mizan_ci'
backup="$temp/Mizan-CI-Full.mizanbak"

curl --fail-with-body --silent --show-error -X POST "$BASE/api/admin/backup/download" \
  -H "Authorization: Bearer $ownerToken" -H "Content-Type: application/json" \
  -d "$(jq -n --arg pw "$MIZAN_RESTORE_PASSPHRASE" '{passphrase:$pw}')" \
  --output "$backup"
[[ -s "$backup" ]] || { echo 'Backup download is empty'; exit 1; }
sha256sum "$backup" | awk '{print toupper($1)}' > "$backup.sha256"

docker run --rm --network host -e PGPASSWORD postgres:17 createdb \
  -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" mizan_restore_test
dotnet run --project src/MizanMedical.RestoreTool/MizanMedical.RestoreTool.csproj \
  -c Release --no-build -- "$backup" mizan_restore_test
count=$(docker run --rm --network host -e PGPASSWORD postgres:17 psql \
  -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d mizan_restore_test -Atc \
  'SELECT count(*) FROM "Departments";')
[[ "$count" -ge 2 ]] || { echo 'Restored department count mismatch'; exit 1; }
balanced=$(docker run --rm --network host -e PGPASSWORD postgres:17 psql \
  -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d mizan_restore_test -Atc \
  'SELECT CASE WHEN SUM("Debit")=SUM("Credit") THEN 1 ELSE 0 END FROM "JournalLines";')
[[ "$balanced" == 1 ]] || { echo 'Restored financial ledger is not balanced'; exit 1; }
echo 'PASS: encrypted PostgreSQL backup restored into disposable database; departments and ledger verified.'
