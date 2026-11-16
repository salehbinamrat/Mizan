#!/usr/bin/env bash
# Integration smoke test. Run ONLY against a disposable PostgreSQL CI database.
set -euo pipefail
BASE="${MIZAN_TEST_URL:-http://127.0.0.1:5081}"
: "${MIZAN_TEST_ADMIN_USERNAME:?Set test username}"
: "${MIZAN_TEST_ADMIN_PASSWORD:?Set test password}"
assert_eq() {
  [[ "$1" == "$2" ]] && return 0
  # JSON decimals may be serialized as 80.00 while the expected fixture is 80.
  if [[ "$1" =~ ^-?[0-9]+(\.[0-9]+)?$ && "$2" =~ ^-?[0-9]+(\.[0-9]+)?$ ]] \
     && jq -en --argjson a "$1" --argjson b "$2" '$a == $b' >/dev/null; then return 0; fi
  echo "FAIL expected=$2 got=$1" >&2; exit 1
}
request() { curl --silent --show-error --fail-with-body -H 'Content-Type: application/json' -H "Authorization: Bearer $TOKEN" -X "$1" "$BASE/api/$2" -d "$3"; }
post() { request POST "$1" "$2"; }
get() { curl --silent --show-error --fail-with-body -H "Authorization: Bearer $TOKEN" "$BASE/api/$1"; }
status() { curl -sS -o /dev/null -w '%{http_code}' -H 'Content-Type: application/json' -H "Authorization: Bearer $1" -X "$2" "$BASE/api/$3" -d "$4"; }
login() {
  curl --fail-with-body --silent --show-error -H 'Content-Type: application/json' -X POST "$BASE/api/auth/login" \
    -d "$(jq -n --arg u "$1" --arg p "$2" '{username:$u,password:$p}')" | jq -r .token
}
TOKEN=$(login "$MIZAN_TEST_ADMIN_USERNAME" "$MIZAN_TEST_ADMIN_PASSWORD")
[[ ${#TOKEN} == 64 ]] || { echo 'Authentication token missing'; exit 1; }
# On a populated database, first-run owner setup must be permanently closed.
setupStatus=$(curl --silent --show-error --fail-with-body "$BASE/api/initial-setup/status")
assert_eq "$(jq -r .required <<< "$setupStatus")" false
assert_eq "$(jq -r .canInitialize <<< "$setupStatus")" false
assert_eq "$(status "$TOKEN" POST initial-setup/complete '{"setupCode":"invalid"}') " 401
ownerContext=$(get auth/me)
jq -e '.fullName | type == "string" and length > 0' <<< "$ownerContext" >/dev/null ||
  { echo 'Session context must include full name'; exit 1; }
assert_eq "$(jq -r .isSystemOwner <<< "$ownerContext")" true
seed="$(date +%s)-$RANDOM"
ward=$(post departments "$(jq -n --arg code "A$RANDOM" '{code:$code,name:"الطوارئ",maxActiveUsers:2}')")
lab=$(post departments "$(jq -n --arg code "B$RANDOM" '{code:$code,name:"المختبر"}')")
WARD=$(jq -r .id <<< "$ward"); LAB=$(jq -r .id <<< "$lab")
patient=$(post patients "$(jq -n --arg n "$seed" --arg d "$WARD" '{fileNumber:("F-"+$n),fullName:"مريض الطوارئ",departmentId:$d}')")
service=$(post services "$(jq -n --arg n "$seed" --arg d "$WARD" '{code:("S-"+$n),name:"كشف الطوارئ",department:"",price:80,departmentId:$d}')")
PATIENT=$(jq -r .id <<< "$patient"); SERVICE=$(jq -r .id <<< "$service")
TOKEN_OWNER="$TOKEN"
cashierName="cashier$RANDOM$RANDOM"
cashierPass="cashier-password-12345!"
cashier=$(post users "$(jq -n --arg u "$cashierName" --arg d "$WARD" --arg pw "$cashierPass" '{username:$u,fullName:"أمين صندوق",password:$pw,role:"Cashier",departmentId:$d,allDepartments:false,permissions:["desks.read","shifts.open","shifts.read","shifts.close","receipts.create","expenses.create"]}')")
CASHIER_ID=$(jq -r .id <<< "$cashier")
TOKEN_CASHIER=$(login "$cashierName" "$cashierPass")
TOKEN="$TOKEN_CASHIER"
cashierContext=$(get auth/me)
assert_eq "$(jq -r .role <<< "$cashierContext")" Cashier
assert_eq "$(jq -r .departmentId <<< "$cashierContext")" "$WARD"
assert_eq "$(jq -r .allDepartments <<< "$cashierContext")" false
if jq -e '.permissions | index("users.manage") != null' <<< "$cashierContext" >/dev/null; then
  echo 'Cashier must not receive owner administration permission' >&2; exit 1
fi
desk=$(get cash-desks | jq '.[0]'); DESK=$(jq -r .id <<< "$desk")
assert_eq "$(jq -r .ownerUserId <<< "$desk")" "$CASHIER_ID"
shift=$(post shifts/open "$(jq -n --arg id "$DESK" '{cashDeskId:$id,openingCash:100}')")
SHIFT=$(jq -r .id <<< "$shift")
invoicePayload=$(jq -n --arg k "$(cat /proc/sys/kernel/random/uuid)" --arg p "$PATIENT" --arg s "$SERVICE" '{requestId:$k,patientId:$p,lines:[{serviceId:$s,quantity:1}]}')
TOKEN="$TOKEN_OWNER"
invoice=$(post invoices "$invoicePayload"); INVOICE=$(jq -r .id <<< "$invoice")
assert_eq "$(jq -r .total <<< "$invoice")" 80
assert_eq "$(jq -r .repeated <<< "$(post invoices "$invoicePayload")")" true
TOKEN="$TOKEN_CASHIER"
receiptPayload=$(jq -n --arg k "$(cat /proc/sys/kernel/random/uuid)" --arg i "$INVOICE" --arg s "$SHIFT" '{requestId:$k,invoiceId:$i,shiftId:$s,amount:50}')
assert_eq "$(jq -r .invoiceBalance <<< "$(post receipts "$receiptPayload")")" 30
assert_eq "$(jq -r .repeated <<< "$(post receipts "$receiptPayload")")" true
badPayment=$(jq -n --arg k "$(cat /proc/sys/kernel/random/uuid)" --arg i "$INVOICE" --arg s "$SHIFT" '{requestId:$k,invoiceId:$i,shiftId:$s,amount:31}')
assert_eq "$(status "$TOKEN" POST receipts "$badPayment")" 400
expensePayload=$(jq -n --arg k "$(cat /proc/sys/kernel/random/uuid)" --arg s "$SHIFT" '{requestId:$k,shiftId:$s,category:"اختبار",description:"مصروف تجريبي",amount:10}')
post expenses "$expensePayload" >/dev/null
# A second department with independent patient/service/invoice.
TOKEN="$TOKEN_OWNER"
patientB=$(post patients "$(jq -n --arg n "$seed" --arg d "$LAB" '{fileNumber:("L-"+$n),fullName:"مريض المختبر",departmentId:$d}')")
serviceB=$(post services "$(jq -n --arg n "$seed" --arg d "$LAB" '{code:("L-"+$n),name:"تحليل",department:"",price:99,departmentId:$d}')")
patientBId=$(jq -r .id <<< "$patientB"); serviceBId=$(jq -r .id <<< "$serviceB")
invoiceB=$(post invoices "$(jq -n --arg k "$(cat /proc/sys/kernel/random/uuid)" --arg p "$patientBId" --arg s "$serviceBId" '{requestId:$k,patientId:$p,lines:[{serviceId:$s,quantity:1}]}')")
INVOICE_B=$(jq -r .id <<< "$invoiceB")
nameA="ward$RANDOM$RANDOM"
PASS='ward-only-password-12345!'
created=$(post users "$(jq -n --arg u "$nameA" --arg d "$WARD" --arg pw "$PASS" '{username:$u,fullName:"مسؤول قسم الطوارئ",password:$pw,role:"DepartmentManager",departmentId:$d,allDepartments:false,permissions:["patients.read","patients.create","invoices.read","invoices.issue","services.read","reports.read","shifts.open","desks.read"]}')")
USER_A=$(jq -r .id <<< "$created")
TOKEN_A=$(login "$nameA" "$PASS")
# Same-department coworker must not operate another cashier's box.
assert_eq "$(status "$TOKEN_A" POST shifts/open "$(jq -n --arg d "$DESK" '{cashDeskId:$d,openingCash:0}')")" 404
TOKEN="$TOKEN_A"
assert_eq "$(get cash-desks | jq 'length')" 1
if [[ "$(get cash-desks | jq -r '.[0].id')" == "$DESK" ]]; then echo 'Cross-user cash desk leaked' >&2; exit 1; fi
TOKEN="$TOKEN_OWNER"
# A third active staff member exceeds this department's limit.
assert_eq "$(status "$TOKEN_OWNER" POST users "$(jq -n --arg u "extra$RANDOM" --arg d "$WARD" '{username:$u,fullName:"زيادة",password:"test-password-12345!",role:"Cashier",departmentId:$d,allDepartments:false,permissions:["desks.read"]}')")" 400
TOKEN_OWNER="$TOKEN"; TOKEN="$TOKEN_A"
assert_eq "$(get patients | jq 'length')" 1
assert_eq "$(get invoices | jq 'length')" 1
assert_eq "$(get services | jq 'length')" 1
assert_eq "$(curl -sS -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $TOKEN_A" "$BASE/api/invoices/$INVOICE_B")" 404
assert_eq "$(status "$TOKEN_A" POST patients "$(jq -n --arg d "$LAB" '{fileNumber:"bad",fullName:"رفض",departmentId:$d}')")" 403
assert_eq "$(status "$TOKEN_A" POST invoices "$(jq -n --arg k "$(cat /proc/sys/kernel/random/uuid)" --arg p "$patientBId" --arg s "$serviceBId" '{requestId:$k,patientId:$p,lines:[{serviceId:$s,quantity:1}]}')")" 404
assert_eq "$(curl -sS -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $TOKEN_A" "$BASE/api/users")" 403
assert_eq "$(curl -sS -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $TOKEN_A" "$BASE/api/audit/recent")" 403
assert_eq "$(get reports/daily | jq -r .invoicedAmount)" 80
TOKEN="$TOKEN_OWNER"
assert_eq "$(get patients | jq 'length')" 2
assert_eq "$(get reports/daily | jq -r .invoicedAmount)" 179
# General manager can view ALL departments only with explicitly assigned view grants.
generalName="general$RANDOM$RANDOM"
post users "$(jq -n --arg u "$generalName" '{username:$u,fullName:"مشرف مالي عام",password:"general-manager-12345!",role:"Manager",departmentId:null,allDepartments:true,permissions:["reports.read","invoices.read"]}')" >/dev/null
TOKEN_G=$(login "$generalName" 'general-manager-12345!')
TOKEN="$TOKEN_G"
assert_eq "$(get invoices | jq 'length')" 2
assert_eq "$(get reports/daily | jq -r .invoicedAmount)" 179
assert_eq "$(curl -sS -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $TOKEN_G" "$BASE/api/users")" 403
assert_eq "$(status "$TOKEN_G" POST expenses "$expensePayload")" 403
TOKEN="$TOKEN_OWNER"
# Revocation is immediate: existing sessions must be invalidated.
request PUT "users/$USER_A" "$(jq -n --arg d "$WARD" '{fullName:"مسؤول الطوارئ",role:"DepartmentManager",departmentId:$d,allDepartments:false,isActive:true,permissions:["patients.read"]}')" >/dev/null
assert_eq "$(curl -sS -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $TOKEN_A" "$BASE/api/patients")" 401
TOKEN_A_NEW=$(login "$nameA" "$PASS")
assert_eq "$(curl -sS -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $TOKEN_A_NEW" "$BASE/api/invoices")" 403
TOKEN="$TOKEN_CASHIER"
close=$(post shifts/close "$(jq -n --arg s "$SHIFT" '{shiftId:$s,countedCash:140,comment:"مطابق"}')")
assert_eq "$(jq -r .expectedCash <<< "$close")" 140
assert_eq "$(jq -r .variance <<< "$close")" 0
TOKEN="$TOKEN_OWNER"
request POST auth/logout '{}' >/dev/null || true
assert_eq "$(curl -sS -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $TOKEN_OWNER" "$BASE/api/audit/recent")" 401
printf 'PASS: department scoping, cross-department 404/403, explicit permission grants, revoked sessions, general manager scope, receipts and shift financial posting\n'
