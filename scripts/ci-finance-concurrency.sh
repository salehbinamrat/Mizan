#!/usr/bin/env bash
# PostgreSQL concurrency regression: disposable CI database only.
# Simultaneous requests must never overpay, overspend, duplicate journals,
# or accept two open/closed states for the same personal cash desk.
set -euo pipefail
BASE="${MIZAN_TEST_URL:-http://127.0.0.1:5081}"
: "${MIZAN_TEST_ADMIN_USERNAME:?}"
: "${MIZAN_TEST_ADMIN_PASSWORD:?}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

login() {
  curl --fail-with-body --silent --show-error --max-time 30 \
    -X POST "$BASE/api/auth/login" -H 'Content-Type: application/json' \
    -d "$(jq -n --arg u "$1" --arg p "$2" '{username:$u,password:$p}')" | jq -r .token
}
request() {
  curl --fail-with-body --silent --show-error --max-time 30 \
    -X POST "$BASE/api/$1" -H 'Content-Type: application/json' \
    -H "Authorization: Bearer $2" -d "$3"
}
send() {
  local path="$1" token="$2" payload="$3" file="$4"
  curl --silent --show-error --max-time 40 -X POST "$BASE/api/$path" \
    -H 'Content-Type: application/json' -H "Authorization: Bearer $token" \
    -d "$payload" -o "$file.body" -w '%{http_code}' > "$file.status"
}
concurrent_pair() {
  local path="$1" token="$2" data1="$3" data2="$4" label="$5"
  send "$path" "$token" "$data1" "$work/$label-a" &
  local a=$!
  send "$path" "$token" "$data2" "$work/$label-b" &
  local b=$!
  wait "$a"
  wait "$b"
}
expect_one_success() {
  local label="$1" success="$2" a b
  a="$(cat "$work/$label-a.status")"
  b="$(cat "$work/$label-b.status")"
  if [[ "$a" == "$success" && ( "$b" == "400" || "$b" == "409" ) ]]; then
    WINNER_BODY="$work/$label-a.body"
  elif [[ "$b" == "$success" && ( "$a" == "400" || "$a" == "409" ) ]]; then
    WINNER_BODY="$work/$label-b.body"
  else
    echo "FAIL: $label expected one $success and one 400/409, received $a and $b" >&2
    cat "$work/$label-a.body" "$work/$label-b.body" >&2
    exit 1
  fi
}
sql_count() {
  local statement="$1"
  docker run --rm --network host -e PGPASSWORD=ci-only-password postgres:17 psql \
    -h 127.0.0.1 -p 5432 -U mizan_ci -d mizan_ci -Atc "$statement"
}
assert_eq() {
  if [[ "$1" != "$2" ]]; then echo "FAIL: expected $2 got $1" >&2; exit 1; fi
}
uuid() { cat /proc/sys/kernel/random/uuid; }
OWNER="$(login "$MIZAN_TEST_ADMIN_USERNAME" "$MIZAN_TEST_ADMIN_PASSWORD")"
[[ ${#OWNER} == 64 ]] || { echo 'No owner token'; exit 1; }
tag="${RANDOM}${RANDOM}"
department="$(request departments "$OWNER" "$(jq -n --arg c "C$tag" '{code:$c,name:"قسم اختبار التزامن",maxActiveUsers:4}')")"
DEPT="$(jq -r .id <<< "$department")"
patient="$(request patients "$OWNER" "$(jq -n --arg n "$tag" --arg d "$DEPT" '{fileNumber:("CON-"+$n),fullName:"مريض اختبار التزامن",departmentId:$d}')")"
service="$(request services "$OWNER" "$(jq -n --arg n "$tag" --arg d "$DEPT" '{code:("CON-"+$n),name:"اختبار مالي",department:"",price:100,departmentId:$d}')")"
PATIENT="$(jq -r .id <<< "$patient")"
SERVICE="$(jq -r .id <<< "$service")"
cashierName="parallel$tag"
cashierPassword='CI-Parallel-Password-2026!'
cashier="$(request users "$OWNER" "$(jq -n --arg u "$cashierName" --arg d "$DEPT" --arg pw "$cashierPassword" '{username:$u,fullName:"صندوق الاختبار",password:$pw,role:"Cashier",departmentId:$d,allDepartments:false,permissions:["desks.read","shifts.open","shifts.read","shifts.close","invoices.read","receipts.create","expenses.create"]}')")"
CASHIER_ID="$(jq -r .id <<< "$cashier")"
CASHIER="$(login "$cashierName" "$cashierPassword")"
DESK="$(curl --silent --show-error --fail-with-body "$BASE/api/cash-desks" -H "Authorization: Bearer $CASHIER" | jq -r '.[0].id')"

# 1: Only one of two simultaneous shift opens can win.
opening="$(jq -n --arg d "$DESK" '{cashDeskId:$d,openingCash:100}')"
concurrent_pair shifts/open "$CASHIER" "$opening" "$opening" opening
expect_one_success opening 201
SHIFT="$(jq -r .id < "$WINNER_BODY")"
assert_eq "$(sql_count "SELECT count(*) FROM \"CashShifts\" WHERE \"CashDeskId\"='$DESK'")" 1

# 2: Distinct payment requests must not concurrently overpay 100.00 invoice.
invoice="$(request invoices "$OWNER" "$(jq -n --arg k "$(uuid)" --arg p "$PATIENT" --arg s "$SERVICE" '{requestId:$k,patientId:$p,lines:[{serviceId:$s,quantity:1}]}')")"
INVOICE="$(jq -r .id <<< "$invoice")"
PAY_A="$(uuid)"; PAY_B="$(uuid)"
paymentA="$(jq -n --arg k "$PAY_A" --arg i "$INVOICE" --arg s "$SHIFT" '{requestId:$k,invoiceId:$i,shiftId:$s,amount:70}')"
paymentB="$(jq -n --arg k "$PAY_B" --arg i "$INVOICE" --arg s "$SHIFT" '{requestId:$k,invoiceId:$i,shiftId:$s,amount:70}')"
concurrent_pair receipts "$CASHIER" "$paymentA" "$paymentB" payment
expect_one_success payment 201
assert_eq "$(sql_count "SELECT \"Paid\" FROM \"Invoices\" WHERE \"Id\"='$INVOICE'")" 70.00
assert_eq "$(sql_count "SELECT count(*) FROM \"CashReceipts\" WHERE \"InvoiceId\"='$INVOICE'")" 1
assert_eq "$(sql_count "SELECT count(*) FROM \"JournalEntries\" WHERE \"ReferenceType\"='receipt' AND \"ReferenceId\" IN (SELECT \"Id\" FROM \"CashReceipts\" WHERE \"InvoiceId\"='$INVOICE')")" 1
# A repeated, already committed request returns its original receipt, not a duplicate.
if [[ "$(cat "$work/payment-a.status")" == 201 ]]; then winningPayment="$paymentA"; else winningPayment="$paymentB"; fi
repeated="$(request receipts "$CASHIER" "$winningPayment")"
assert_eq "$(jq -r .repeated <<< "$repeated")" true

# 3: Two concurrent 110.00 expenses cannot both spend the same 170.00 cash.
EXP_A="$(uuid)"; EXP_B="$(uuid)"
expenseA="$(jq -n --arg k "$EXP_A" --arg s "$SHIFT" '{requestId:$k,shiftId:$s,category:"فحص",description:"صرف متزامن",amount:110}')"
expenseB="$(jq -n --arg k "$EXP_B" --arg s "$SHIFT" '{requestId:$k,shiftId:$s,category:"فحص",description:"صرف متزامن",amount:110}')"
concurrent_pair expenses "$CASHIER" "$expenseA" "$expenseB" expense
expect_one_success expense 201
assert_eq "$(sql_count "SELECT count(*) FROM \"CashExpenses\" WHERE \"CashShiftId\"='$SHIFT'")" 1
assert_eq "$(sql_count "SELECT coalesce(sum(\"Amount\"),0) FROM \"CashExpenses\" WHERE \"CashShiftId\"='$SHIFT'")" 110
assert_eq "$(sql_count "SELECT count(*) FROM \"JournalEntries\" WHERE \"ReferenceType\"='expense' AND \"ReferenceId\" IN (SELECT \"Id\" FROM \"CashExpenses\" WHERE \"CashShiftId\"='$SHIFT')")" 1

# 4: Closing the same shift concurrently must close exactly once.
closing="$(jq -n --arg s "$SHIFT" '{shiftId:$s,countedCash:60,comment:"متوازن"}')"
concurrent_pair shifts/close "$CASHIER" "$closing" "$closing" closing
expect_one_success closing 200
assert_eq "$(sql_count "SELECT \"ExpectedCash\" FROM \"CashShifts\" WHERE \"Id\"='$SHIFT'")" 60.00
assert_eq "$(sql_count "SELECT count(*) FROM \"CashShifts\" WHERE \"CashDeskId\"='$DESK' AND \"ClosedAtUtc\" IS NULL")" 0
assert_eq "$(sql_count 'SELECT CASE WHEN sum("Debit")=sum("Credit") THEN 1 ELSE 0 END FROM "JournalLines"')" 1
echo 'PASS: concurrent opening, overpayment, overspending and close cannot duplicate funds or journals.'
