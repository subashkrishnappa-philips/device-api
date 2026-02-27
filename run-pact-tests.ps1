<#
.SYNOPSIS
    Enterprise Pact contract testing pipeline — zero manual intervention.

.DESCRIPTION
    Orchestrates the complete Pact + Swagger contract workflow:

        Step 0 – Restore & build the solution
        Step 1 – SwaggerPactGenerator
                    • Starts DeviceApi, fetches live swagger.json
                    • Generates skeleton consumer stubs for uncovered endpoints
                    • Writes contracts/consumer-notification.md
        Step 2 – Consumer tests
                    • Verifies consumer pact interactions against mock server
                    • Writes contracts/DeviceApi-Consumer-DeviceApi.json
        Step 3 – Provider tests
                    • Boots real DeviceApi (no WebApplicationFactory needed)
                    • DeviceApiProviderTests    — pact replay
                    • SwaggerContractValidatorTests — swagger drift + team notification
                    • SwaggerCoverageTests      — all endpoints covered by pacts
        Step 4 – (Optional) Baseline update
                    • Regenerates contracts/swagger-baseline.json from live API

    See docs/contract-change-runbook.md for the full workflow.
    Exits with code 0 on success, 1 on any failure.

.PARAMETER UpdateBaseline
    When supplied, deletes contracts/swagger-baseline.json before running
    provider tests, causing SwaggerContractValidatorTests to auto-regenerate it.
    Use this after an intentional API contract change that all consumer teams
    have acknowledged and updated their pacts for.

.PARAMETER GenerateOnly
    Runs only the SwaggerPactGenerator step without executing any tests.
    Useful to preview generated stubs or notify consumers before the full run.

.PARAMETER SkipGenerator
    Skips the SwaggerPactGenerator step. Use when the swagger has not changed
    and you only want to run pact tests.

.PARAMETER Configuration
    Build configuration. Default: Debug

.PARAMETER Verbosity
    MSBuild verbosity for dotnet test. Default: normal

.EXAMPLE
    # Standard CI run
    .\run-pact-tests.ps1

.EXAMPLE
    # After an intentional swagger change — regenerate stubs + baseline
    .\run-pact-tests.ps1 -UpdateBaseline

.EXAMPLE
    # Generate stubs and consumer notification report only (no tests)
    .\run-pact-tests.ps1 -GenerateOnly

.EXAMPLE
    # Release build, skip generator (swagger unchanged)
    .\run-pact-tests.ps1 -SkipGenerator -Configuration Release -Verbosity quiet
#>

[CmdletBinding()]
param(
    [switch] $UpdateBaseline,
    [switch] $GenerateOnly,
    [switch] $SkipGenerator,
    [string] $Configuration = "Debug",
    [string] $Verbosity      = "normal"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Paths ─────────────────────────────────────────────────────────────────────
$Root              = $PSScriptRoot
$SolutionFile      = Join-Path $Root "DeviceApi.sln"  # Provider team's solution (see device-api-consumer repo for consumer solution)
$ConsumerProject   = Join-Path $Root "tests"  "DeviceApi.Consumer.Tests"
$ProviderProject   = Join-Path $Root "tests"  "DeviceApi.Provider.Tests"
$GeneratorProject  = Join-Path $Root "tools"  "SwaggerPactGenerator"
$ContractsDir      = Join-Path $Root "contracts"
$BaselineFile      = Join-Path $ContractsDir "swagger-baseline.json"
$NotificationFile  = Join-Path $ContractsDir "consumer-notification.md"
$GeneratedStubsDir = Join-Path $Root "tests" "DeviceApi.Consumer.Tests" "Generated"
$ResultsDir        = Join-Path $Root "TestResults"
$DevApiProject     = Join-Path $Root "src" "DeviceApi"

# ── Helpers ───────────────────────────────────────────────────────────────────
function Write-Banner([string]$msg) {
    $line = "─" * 70
    Write-Host "`n$line"  -ForegroundColor Cyan
    Write-Host "  $msg"   -ForegroundColor Cyan
    Write-Host "$line`n"  -ForegroundColor Cyan
}

function Invoke-DotnetTest([string]$project, [string]$label, [string]$filter = "") {
    Write-Banner "Running $label"
    $logFile = Join-Path $ResultsDir "$label.trx"

    $testArgs = @(
        "test", $project,
        "--configuration", $Configuration,
        "--verbosity", $Verbosity,
        "--logger", "trx;LogFileName=$logFile",
        "--results-directory", $ResultsDir
    )

    if ($filter -ne "") {
        $testArgs += "--filter"
        $testArgs += $filter
    }

    & dotnet @testArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host "`n[FAIL] $label failed.  See: $logFile" -ForegroundColor Red
        exit 1
    }
    Write-Host "[PASS] $label passed." -ForegroundColor Green
}

# ── Pre-flight ────────────────────────────────────────────────────────────────
Write-Banner "DeviceApi — Enterprise Contract Testing Pipeline"
Write-Host "  Root             : $Root"
Write-Host "  Contracts dir    : $ContractsDir"
Write-Host "  Configuration    : $Configuration"
Write-Host "  Update baseline  : $UpdateBaseline"
Write-Host "  Generate only    : $GenerateOnly"
Write-Host "  Skip generator   : $SkipGenerator"

New-Item -ItemType Directory -Path $ContractsDir      -Force | Out-Null
New-Item -ItemType Directory -Path $ResultsDir        -Force | Out-Null
New-Item -ItemType Directory -Path $GeneratedStubsDir -Force | Out-Null

# ── Handle --update-baseline ─────────────────────────────────────────────────
if ($UpdateBaseline -and (Test-Path $BaselineFile)) {
    Write-Host "`n[INFO] Removing swagger-baseline.json for regeneration..." `
        -ForegroundColor Yellow
    Remove-Item $BaselineFile -Force
}

# ── Step 0: Restore + Build ───────────────────────────────────────────────────
if (-not $GenerateOnly) {
    Write-Banner "Step 0 — Restore & Build"

    dotnet restore $SolutionFile --verbosity quiet
    if ($LASTEXITCODE -ne 0) { Write-Host "[FAIL] Restore failed." -ForegroundColor Red; exit 1 }

    dotnet build $SolutionFile `
        --configuration $Configuration `
        --no-restore    `
        --verbosity     $Verbosity
    if ($LASTEXITCODE -ne 0) { Write-Host "[FAIL] Build failed." -ForegroundColor Red; exit 1 }
    Write-Host "[PASS] Build succeeded." -ForegroundColor Green
}

# ── Step 1: SwaggerPactGenerator ─────────────────────────────────────────────
if (-not $SkipGenerator) {
    Write-Banner "Step 1 — SwaggerPactGenerator: Detect uncovered endpoints + notify consumers"

    dotnet build $GeneratorProject --configuration $Configuration --verbosity quiet | Out-Null

    $pactFile  = Join-Path $ContractsDir "DeviceApi-Consumer-DeviceApi.json"
    $devApiPort = 5099

    Write-Host "[INFO] Starting DeviceApi on port $devApiPort to fetch live swagger..." `
        -ForegroundColor Yellow

    $devApiProcess = Start-Process -FilePath "dotnet" `
        -ArgumentList "run --project `"$DevApiProject`" --configuration $Configuration --urls http://localhost:$devApiPort" `
        -NoNewWindow -PassThru

    $swaggerUrl = "http://localhost:$devApiPort/swagger/v1/swagger.json"
    $ready = $false
    for ($i = 0; $i -lt 15; $i++) {
        Start-Sleep -Seconds 1
        try {
            $null = Invoke-WebRequest -Uri $swaggerUrl -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
            $ready = $true
            break
        } catch { }
    }

    if (-not $ready) {
        $devApiProcess | Stop-Process -Force -ErrorAction SilentlyContinue
        Write-Host "[WARN] DeviceApi did not start in time. Skipping generator." `
            -ForegroundColor Yellow
    } else {
        Write-Host "[INFO] Fetching swagger from $swaggerUrl" -ForegroundColor Green

        $genArgs = @("--swagger-url", $swaggerUrl,
                     "--consumer-output", $GeneratedStubsDir,
                     "--notification", $NotificationFile)

        if (Test-Path $pactFile) {
            $genArgs += "--pact-file"
            $genArgs += $pactFile
        }

        dotnet run --project $GeneratorProject --no-build -- @genArgs

        $devApiProcess | Stop-Process -Force -ErrorAction SilentlyContinue

        if (Test-Path $NotificationFile) {
            Write-Host "`n[INFO] Consumer notification report: $NotificationFile" `
                -ForegroundColor Yellow
            Write-Host "       Share this with consumer teams listed in contracts/consumers.json."
        }
    }

    if ($GenerateOnly) {
        Write-Banner "Generate-only mode complete"
        Write-Host "  Generated stubs : $GeneratedStubsDir"
        Write-Host "  Notification    : $NotificationFile"
        exit 0
    }
}

# ── Step 2: Consumer tests – generate pact file ───────────────────────────────
#   MUST run before provider tests.
#   Writes: contracts/DeviceApi-Consumer-DeviceApi.json
Invoke-DotnetTest -project $ConsumerProject -label "DeviceApi.Consumer.Tests"

$pactFiles = Get-ChildItem $ContractsDir -Filter "*.json" `
    -Exclude "swagger-baseline.json","consumers.json" -ErrorAction SilentlyContinue
if ($null -eq $pactFiles -or $pactFiles.Count -eq 0) {
    Write-Host "`n[FAIL] No pact file found in $ContractsDir after consumer tests." `
        -ForegroundColor Red
    exit 1
}
Write-Host "`n[INFO] Pact files generated:" -ForegroundColor Green
$pactFiles | ForEach-Object { Write-Host "  - $($_.Name)" }

# ── Step 3: Provider tests ────────────────────────────────────────────────────
#   Runs three test classes against the live API:
#     DeviceApiProviderTests         — pact replay
#     SwaggerContractValidatorTests  — swagger drift + consumer notification
#     SwaggerCoverageTests           — all endpoints covered by pacts
Invoke-DotnetTest -project $ProviderProject -label "DeviceApi.Provider.Tests"

# ── Done ──────────────────────────────────────────────────────────────────────
Write-Banner "All contract tests PASSED"
Write-Host "  Consumer pacts  : $ContractsDir"
Write-Host "  Test results    : $ResultsDir"
Write-Host "  Runbook         : docs/contract-change-runbook.md"
Write-Host ""
exit 0
