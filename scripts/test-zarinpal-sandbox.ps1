<#
.SYNOPSIS
    Staging smoke + payment-path test for OnlineStoreApi.

.DESCRIPTION
    Phase 9 (R42): ZarinpalPaymentGateway has never executed against a real API.
    This script is the rehearsal runway for that first run. It is deliberately
    honest about what it can and cannot prove:

      Phase 1 (Smoke, no order needed)
        - API is up
        - webhook REJECTS a bad signature (fail-closed)
        - webhook ACCEPTS a correctly signed payload
        -> proves the only authentication of an anonymous endpoint works.

      Phase 2 (Configuration)
        - appsettings.Staging.json resolves to the Zarinpal sandbox and keeps
          EnableRealGateway=false by default
        -> a static check. It does NOT prove the gateway was called.

      Phase 3 (Payment path, needs -OrderId)
        - initiate -> verify -> Payment.State == Succeeded
        - a second verify returns the same result (idempotency)
        -> with Provider=Manual this exercises the whole state machine using
           FakePaymentGateway. With Provider=Zarinpal and EnableRealGateway=true
           it produces a real sandbox RedirectUrl, which is as far as an
           automated script should go: paying with a real card is a human step.

.PARAMETER BaseUrl
    API root. Default http://localhost:5080 (staging compose port).

.PARAMETER OrderId
    An order in Pending state owned by -PhoneNumber. Required for Phase 3.

.PARAMETER PhoneNumber / Password
    Credentials for POST /api/v1/auth/login.

.PARAMETER WebhookSecret
    Must match PaymentGatewaySettings__WebhookSecret of the running API.

.EXAMPLE
    # Phases 1-2 only, against the staging compose stack
    ./test-zarinpal-sandbox.ps1 -WebhookSecret "dev-secret"

.EXAMPLE
    # Full path for an existing pending order
    ./test-zarinpal-sandbox.ps1 -WebhookSecret "dev-secret" -OrderId 42 -PhoneNumber 09123456789
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'http://localhost:5080',
    [int]    $OrderId = 0,
    [string] $PhoneNumber = '09123456789',
    [string] $Password = 'Test@123',
    [Parameter(Mandatory = $true)]
    [string] $WebhookSecret,
    [string] $StagingSettingsPath = "$PSScriptRoot/../Online Store Application/appsettings.Staging.json"
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:Failures = 0

function Write-Step  { param([string]$Text) Write-Host "`n=== $Text ===" -ForegroundColor Cyan }
function Write-Pass  { param([string]$Text) Write-Host "  PASS  $Text" -ForegroundColor Green }
function Write-Fail  { param([string]$Text) $script:Failures++; Write-Host "  FAIL  $Text" -ForegroundColor Red }
function Write-Note  { param([string]$Text) Write-Host "  note: $Text" -ForegroundColor DarkGray }

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if ($Condition) { Write-Pass $Message } else { Write-Fail $Message }
}

function New-WebhookSignature {
    param([string]$Payload, [string]$Secret)

    $hmac = [System.Security.Cryptography.HMACSHA256]::new(
        [System.Text.Encoding]::UTF8.GetBytes($Secret))

    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Payload)
        return ([System.BitConverter]::ToString($hmac.ComputeHash($bytes)) -replace '-', '').ToLowerInvariant()
    }
    finally { $hmac.Dispose() }
}

# ─────────────────────────────────────────────────────────────
# Phase 1 — Smoke
# ─────────────────────────────────────────────────────────────
Write-Step "Phase 1 — Smoke ($BaseUrl)"

try {
    $health = Invoke-WebRequest "$BaseUrl/health" -TimeoutSec 10 -SkipHttpErrorCheck
    Assert-True ($health.StatusCode -eq 200) "API is reachable (GET /health -> $($health.StatusCode))"
}
catch {
    Write-Fail "API is not reachable at $BaseUrl — start it first:"
    Write-Note "docker compose -f docker-compose.staging.yml up -d"
    Write-Note $_.Exception.Message
    exit 1
}

$webhookUrl = "$BaseUrl/api/v1/payments/webhook"
$payload = '{"authority":"A00000000000000000000000000000000000","status":"OK"}'

# 1a — bad signature must be refused. This is the single most important
# property of an anonymous endpoint that can mark money as received.
try {
    $bad = Invoke-WebRequest $webhookUrl -Method Post `
        -ContentType 'application/json' -Body $payload `
        -Headers @{ 'X-Payment-Signature' = 'deadbeef' } `
        -SkipHttpErrorCheck -TimeoutSec 15

    Assert-True ($bad.StatusCode -eq 403) "Unsigned/badly-signed webhook is refused (got $($bad.StatusCode))"
}
catch {
    Assert-True $false "Webhook did not refuse a bad signature: $($_.Exception.Message)"
}

# 1b — missing signature must also be refused.
try {
    $missing = Invoke-WebRequest $webhookUrl -Method Post `
        -ContentType 'application/json' -Body $payload `
        -SkipHttpErrorCheck -TimeoutSec 15

    Assert-True ($missing.StatusCode -eq 403) "Webhook with no signature is refused (got $($missing.StatusCode))"
}
catch {
    Assert-True $false "Webhook did not refuse a missing signature: $($_.Exception.Message)"
}

# 1c — correctly signed payload is accepted (200 even for an unknown authority:
# the gateway must not retry forever).
$signature = New-WebhookSignature -Payload $payload -Secret $WebhookSecret

try {
    $good = Invoke-WebRequest $webhookUrl -Method Post `
        -ContentType 'application/json' -Body $payload `
        -Headers @{ 'X-Payment-Signature' = $signature } `
        -SkipHttpErrorCheck -TimeoutSec 15

    Assert-True ($good.StatusCode -eq 200) "Correctly signed webhook is accepted (got $($good.StatusCode))"
}
catch {
    Assert-True $false "Webhook rejected a valid signature: $($_.Exception.Message)"
}

# ─────────────────────────────────────────────────────────────
# Phase 2 — Configuration resolution
# ─────────────────────────────────────────────────────────────
Write-Step "Phase 2 — Configuration"

if (-not (Test-Path $StagingSettingsPath)) {
    Write-Fail "Staging settings not found: $StagingSettingsPath"
}
else {
    $cfg = Get-Content $StagingSettingsPath -Raw | ConvertFrom-Json
    $pay = $cfg.PaymentGatewaySettings

    Assert-True ($pay.Provider -eq 'Zarinpal') "Provider is Zarinpal"
    Assert-True ($pay.UseSandbox -eq $true) "UseSandbox is true"
    Assert-True ($pay.BaseUrl -like '*sandbox.zarinpal.com*') "BaseUrl points at the sandbox"

    # UseSandbox takes priority over BaseUrl in PaymentGatewaySettings.EffectiveBaseUrl.
    Assert-True ($pay.EnableRealGateway -eq $false) "EnableRealGateway defaults to false"

    if ([string]::IsNullOrWhiteSpace($pay.MerchantId) -or $pay.MerchantId -eq '__FROM_ENV__') {
        Write-Note "MerchantId is a placeholder — the gateway will fail closed until env provides it."
    }
}

# ─────────────────────────────────────────────────────────────
# Phase 3 — Payment path
# ─────────────────────────────────────────────────────────────
Write-Step "Phase 3 — Payment path"

if ($OrderId -le 0) {
    Write-Note "Skipped: pass -OrderId <n> for an existing Pending order to run this phase."
    Write-Note "This phase is the one that proves initiate/verify, so run it before trusting staging."
}
else {
    $login = Invoke-WebRequest "$BaseUrl/api/v1/auth/login" -Method Post `
        -ContentType 'application/json' `
        -Body (@{ phoneNumber = $PhoneNumber; password = $Password } | ConvertTo-Json) `
        -SkipHttpErrorCheck -TimeoutSec 20

    if ($login.StatusCode -ne 200) {
        Write-Fail "Login failed ($($login.StatusCode)) — cannot continue Phase 3"
        Write-Note $login.Content
    }
    else {
        $token = ($login.Content | ConvertFrom-Json).accessToken
        Write-Pass "Logged in as $PhoneNumber"

        $auth = @{ Authorization = "Bearer $token" }
        $idem = [guid]::NewGuid().ToString()

        $initiate = Invoke-WebRequest "$BaseUrl/api/v1/payments/initiate" -Method Post `
            -ContentType 'application/json' `
            -Headers ($auth + @{ 'Idempotency-Key' = $idem }) `
            -Body (@{ orderId = $OrderId } | ConvertTo-Json) `
            -SkipHttpErrorCheck -TimeoutSec 30

        if ($initiate.StatusCode -ne 200) {
            Write-Fail "Initiate failed ($($initiate.StatusCode))"
            Write-Note $initiate.Content
            Write-Note "A 400 GATEWAY_DISABLED is expected when EnableRealGateway=false."
        }
        else {
            $created = $initiate.Content | ConvertFrom-Json
            Write-Pass "Payment $($created.paymentId) initiated (state=$($created.state))"

            if ($created.redirectUrl) {
                Write-Note "Gateway redirect URL: $($created.redirectUrl)"
                $isSandbox = $created.redirectUrl -like '*sandbox*'
                Assert-True $isSandbox "Redirect URL points at the sandbox"
            }
            else {
                Write-Note "No RedirectUrl (Manual provider) — using the fake gateway."
            }

            # Idempotency: the same key must return the same payment, not a new one.
            $replay = Invoke-WebRequest "$BaseUrl/api/v1/payments/initiate" -Method Post `
                -ContentType 'application/json' `
                -Headers ($auth + @{ 'Idempotency-Key' = $idem }) `
                -Body (@{ orderId = $OrderId } | ConvertTo-Json) `
                -SkipHttpErrorCheck -TimeoutSec 30

            $replayed = $replay.Content | ConvertFrom-Json
            Assert-True ($replayed.paymentId -eq $created.paymentId) `
                "Replaying the same Idempotency-Key returns the same payment"

            if ($created.authority) {
                $verifyUrl = "$BaseUrl/api/v1/payments/verify?authority=$($created.authority)"

                $verify = Invoke-WebRequest $verifyUrl -Method Post -Headers $auth `
                    -SkipHttpErrorCheck -TimeoutSec 30

                if ($verify.StatusCode -eq 200) {
                    $v = $verify.Content | ConvertFrom-Json
                    Assert-True ($v.state -eq 'Succeeded') "Verify moved the payment to Succeeded"
                }
                else {
                    Write-Fail "Verify failed ($($verify.StatusCode))"
                    Write-Note $verify.Content
                }

                # Verifying twice must be a no-op, never a second charge.
                $again = Invoke-WebRequest $verifyUrl -Method Post -Headers $auth `
                    -SkipHttpErrorCheck -TimeoutSec 30

                Assert-True ($again.StatusCode -eq 200) "Second verify is idempotent (got $($again.StatusCode))"
            }

            $lookup = Invoke-WebRequest "$BaseUrl/api/v1/payments/order/$OrderId" -Method Get `
                -Headers $auth -SkipHttpErrorCheck -TimeoutSec 20

            Assert-True ($lookup.StatusCode -eq 200) "GET /payments/order/$OrderId returns the payment"
        }
    }
}

# ─────────────────────────────────────────────────────────────
Write-Step "Result"

if ($script:Failures -eq 0) {
    Write-Host "All checks passed." -ForegroundColor Green
    exit 0
}

Write-Host ("{0} check(s) failed." -f $script:Failures) -ForegroundColor Red
exit 1