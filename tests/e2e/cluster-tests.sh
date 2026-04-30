#!/bin/sh
# =============================================================================
# E2E Tests for ContosoInsurance - runs inside AKS cluster
# Upload via: az aks command invoke --file tests/e2e/cluster-tests.sh
# =============================================================================

PASSED=0
FAILED=0
FAILURES=""

WEB="http://webfrontend-service.contoso-insurance.svc.cluster.local:80"
API="http://api-service.contoso-insurance.svc.cluster.local:8080"

check_status() {
    local name="$1"
    local url="$2"
    local expected="${3:-200}"
    local method="${4:-GET}"
    local body="$5"
    
    if [ "$method" = "POST" ] && [ -n "$body" ]; then
        STATUS=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 -X POST -H "Content-Type: application/json" -d "$body" "$url")
    else
        STATUS=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 "$url")
    fi
    
    if [ "$STATUS" = "$expected" ]; then
        PASSED=$((PASSED + 1))
        echo "  PASS: $name (HTTP $STATUS)"
    else
        FAILED=$((FAILED + 1))
        FAILURES="${FAILURES}\n  - $name: expected $expected, got $STATUS"
        echo "  FAIL: $name (expected $expected, got $STATUS)"
    fi
}

check_contains() {
    local name="$1"
    local url="$2"
    local pattern="$3"
    
    BODY=$(curl -s --max-time 15 "$url")
    STATUS=$?
    
    if echo "$BODY" | grep -q "$pattern"; then
        PASSED=$((PASSED + 1))
        echo "  PASS: $name (contains '$pattern')"
    else
        FAILED=$((FAILED + 1))
        FAILURES="${FAILURES}\n  - $name: pattern '$pattern' not found"
        echo "  FAIL: $name (pattern '$pattern' not found)"
    fi
}

check_header() {
    local name="$1"
    local url="$2"
    local header="$3"
    
    HEADERS=$(curl -s -D - -o /dev/null --max-time 10 "$url")
    
    if echo "$HEADERS" | grep -qi "$header"; then
        PASSED=$((PASSED + 1))
        echo "  PASS: $name"
    else
        FAILED=$((FAILED + 1))
        FAILURES="${FAILURES}\n  - $name: header '$header' not found"
        echo "  FAIL: $name"
    fi
}

echo "========================================"
echo "ContosoInsurance E2E Tests"
echo "========================================"

# --- Pod Health ---
echo ""
echo "--- Pod Health ---"
kubectl get pods -n contoso-insurance --no-headers | while read -r line; do
    NAME=$(echo "$line" | awk '{print $1}')
    READY=$(echo "$line" | awk '{print $2}')
    STATUS=$(echo "$line" | awk '{print $3}')
    if [ "$READY" = "1/1" ] && [ "$STATUS" = "Running" ]; then
        PASSED=$((PASSED + 1))
        echo "  PASS: $NAME ($READY $STATUS)"
    else
        FAILED=$((FAILED + 1))
        echo "  FAIL: $NAME ($READY $STATUS)"
    fi
done

# --- Web Frontend ---
echo ""
echo "--- Web Frontend Pages ---"
check_status "Home Page" "$WEB/"
check_status "Dashboard" "$WEB/dashboard"
check_status "Customers Page" "$WEB/customers"
check_status "Policies Page" "$WEB/policies"
check_status "Quotes Page" "$WEB/quotes"
check_status "Claims Page" "$WEB/claims"
check_status "File Claim Page" "$WEB/claims/new"

# --- API Health ---
echo ""
echo "--- API Health ---"
check_contains "Liveness /alive" "$API/alive" "Healthy"
check_contains "Readiness /health" "$API/health" "Healthy"

# --- API Customers CRUD ---
echo ""
echo "--- API: Customers CRUD ---"
check_status "List Customers" "$API/api/customers"
check_status "List Customers (paged)" "$API/api/customers?page=1&pageSize=5"
check_contains "Customers response has items" "$API/api/customers" "items"
check_contains "Customers has totalCount" "$API/api/customers" "totalCount"
check_contains "Seeded customer exists" "$API/api/customers" "Maria"

# Create a customer with unique email and verify
UNIQ=$(date +%s)
CUST_RESP=$(curl -s --max-time 15 -X POST -H "Content-Type: application/json" \
  -d "{\"firstName\":\"E2E\",\"lastName\":\"Test${UNIQ}\",\"email\":\"e2e-${UNIQ}@test.com\",\"phone\":\"555-0199\",\"address\":\"123 Test St\"}" \
  -w "\n%{http_code}" "$API/api/customers")
CUST_STATUS=$(echo "$CUST_RESP" | tail -1)
CUST_BODY=$(echo "$CUST_RESP" | head -n -1)
if [ "$CUST_STATUS" = "201" ]; then
    PASSED=$((PASSED + 1))
    echo "  PASS: Create Customer (HTTP 201)"
    # Extract ID and verify GET
    CUST_ID=$(echo "$CUST_BODY" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
    if [ -n "$CUST_ID" ]; then
        check_status "Get Created Customer" "$API/api/customers/$CUST_ID"
        check_contains "Created customer has name" "$API/api/customers/$CUST_ID" "E2E"
    fi
else
    FAILED=$((FAILED + 1))
    FAILURES="${FAILURES}\n  - Create Customer: expected 201, got $CUST_STATUS"
    echo "  FAIL: Create Customer (expected 201, got $CUST_STATUS)"
fi

check_status "Get Non-existent Customer" "$API/api/customers/00000000-0000-0000-0000-000000000099" "404"

# --- API Policies ---
echo ""
echo "--- API: Policies ---"
check_status "List Policies" "$API/api/policies"
check_status "List Policies (paged)" "$API/api/policies?page=1&pageSize=5"
check_contains "Policies response has items" "$API/api/policies" "items"
check_contains "Policies has totalCount" "$API/api/policies" "totalCount"

# Get first policy ID and check detail
POLICY_ID=$(curl -s --max-time 15 "$API/api/policies" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
if [ -n "$POLICY_ID" ]; then
    check_status "Get Policy by ID" "$API/api/policies/$POLICY_ID"
    check_contains "Policy has policyNumber" "$API/api/policies/$POLICY_ID" "policyNumber"
fi
check_status "Get Non-existent Policy" "$API/api/policies/00000000-0000-0000-0000-000000000099" "404"

# --- API Claims ---
echo ""
echo "--- API: Claims ---"
check_status "List Claims" "$API/api/claims"
check_status "List Claims (paged)" "$API/api/claims?page=1&pageSize=5"
check_contains "Claims response has items" "$API/api/claims" "items"
check_contains "Claims has totalCount" "$API/api/claims" "totalCount"

# Get first claim and verify detail
CLAIM_ID=$(curl -s --max-time 15 "$API/api/claims" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
if [ -n "$CLAIM_ID" ]; then
    check_status "Get Claim by ID" "$API/api/claims/$CLAIM_ID"
    check_contains "Claim has status" "$API/api/claims/$CLAIM_ID" "status"
fi
check_status "Get Non-existent Claim" "$API/api/claims/00000000-0000-0000-0000-000000000099" "404"

# Create a new claim (requires active policy - status:1)
ACTIVE_POLICY_ID=$(curl -s --max-time 15 "$API/api/policies" | grep -o '"id":"[^"]*","policyNumber":"[^"]*","type":[0-9]*,"status":1' | head -1 | grep -o '"id":"[^"]*"' | cut -d'"' -f4)
if [ -n "$ACTIVE_POLICY_ID" ]; then
    check_status "Create Claim" "$API/api/claims" "201" "POST" "{\"policyId\":\"$ACTIVE_POLICY_ID\",\"description\":\"E2E test claim ${UNIQ}\",\"amount\":500.00,\"incidentDate\":\"2025-06-15T10:00:00\"}"
else
    echo "  SKIP: Create Claim (no active policy found)"
fi

# --- API Quotes ---
echo ""
echo "--- API: Quotes ---"
check_status "List Quotes" "$API/api/quotes"
check_status "List Quotes (paged)" "$API/api/quotes?page=1&pageSize=5"
check_contains "Quotes response has items" "$API/api/quotes" "items"
check_contains "Quotes has totalCount" "$API/api/quotes" "totalCount"

# Get first quote and verify detail
QUOTE_ID=$(curl -s --max-time 15 "$API/api/quotes" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
if [ -n "$QUOTE_ID" ]; then
    check_status "Get Quote by ID" "$API/api/quotes/$QUOTE_ID"
    check_contains "Quote has estimatedPremium" "$API/api/quotes/$QUOTE_ID" "estimatedPremium"
fi
check_status "Get Non-existent Quote" "$API/api/quotes/00000000-0000-0000-0000-000000000099" "404"

# Create a new quote (type 0=Auto, 1=Home, 2=Life)
if [ -n "$CUST_ID" ]; then
    check_status "Create Quote" "$API/api/quotes" "201" "POST" "{\"customerId\":\"$CUST_ID\",\"type\":1,\"coverageAmount\":250000}"
fi

# --- Web Content Verification ---
echo ""
echo "--- Web Content ---"
check_contains "Home has brand name" "$WEB/" "Contoso"
check_contains "Dashboard has metrics" "$WEB/dashboard" "dashboard"
check_contains "Customers page has table" "$WEB/customers" "customer"
check_contains "Policies page has content" "$WEB/policies" "polic"
check_contains "Claims page has content" "$WEB/claims" "claim"

# --- Cross-cutting ---
echo ""
echo "--- Cross-cutting ---"
check_header "API Version header" "$API/api/customers" "X-Api-Version"
check_header "API Content-Type JSON" "$API/api/customers" "application/json"

# Verify pagination works correctly
PAGE_RESP=$(curl -s --max-time 15 "$API/api/customers?page=1&pageSize=2")
PAGE_SIZE=$(echo "$PAGE_RESP" | grep -o '"pageSize":2')
HAS_NEXT=$(echo "$PAGE_RESP" | grep -o '"hasNext":true')
if [ -n "$PAGE_SIZE" ]; then
    PASSED=$((PASSED + 1))
    echo "  PASS: Pagination pageSize respected"
else
    FAILED=$((FAILED + 1))
    FAILURES="${FAILURES}\n  - Pagination pageSize: expected pageSize:2 in response"
    echo "  FAIL: Pagination pageSize not respected"
fi
if [ -n "$HAS_NEXT" ]; then
    PASSED=$((PASSED + 1))
    echo "  PASS: Pagination hasNext=true with more data"
else
    FAILED=$((FAILED + 1))
    FAILURES="${FAILURES}\n  - Pagination hasNext: expected true with >2 customers"
    echo "  FAIL: Pagination hasNext not true"
fi

# --- Summary ---
echo ""
echo "========================================"
echo "Results: $PASSED passed, $FAILED failed"
if [ $FAILED -gt 0 ]; then
    echo ""
    echo "FAILURES:"
    printf "$FAILURES\n"
    exit 1
else
    echo "All tests passed!"
    exit 0
fi
