param(
    [string] $PostgreSqlBin = 'D:\Program Files\PostgreSQL\17\bin',
    [string] $TestFilter = 'FullyQualifiedName~MonthlyStructuredQueryPostgreSqlTests',
    [switch] $EnableLiveLlm
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$runRoot = Join-Path $tempRoot ('feature141-pg-' + [guid]::NewGuid().ToString('N'))
$dataDirectory = Join-Path $runRoot 'data'
$logPath = Join-Path $runRoot 'postgres.log'
$pgCtl = Join-Path $PostgreSqlBin 'pg_ctl.exe'
$initDb = Join-Path $PostgreSqlBin 'initdb.exe'
$serverStarted = $false
$testExitCode = 1

if (-not (Test-Path -LiteralPath $pgCtl -PathType Leaf) -or -not (Test-Path -LiteralPath $initDb -PathType Leaf)) {
    throw "PostgreSQL 17 tools were not found in '$PostgreSqlBin'."
}
if (-not $runRoot.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to create a test cluster outside the system temporary directory: '$runRoot'."
}

try {
    New-Item -ItemType Directory -Path $runRoot | Out-Null
    & $initDb -D $dataDirectory -U postgres -A trust --no-locale --encoding=UTF8
    if ($LASTEXITCODE -ne 0) { throw "initdb failed with exit code $LASTEXITCODE." }

    $portReservation = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $portReservation.Start()
    $port = ([System.Net.IPEndPoint]$portReservation.LocalEndpoint).Port
    $portReservation.Stop()

    $serverOptions = "-h 127.0.0.1 -p $port -c listen_addresses=127.0.0.1"
    & $pgCtl -D $dataDirectory -l $logPath -o $serverOptions -w start
    if ($LASTEXITCODE -ne 0) {
        if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Tail 40 }
        throw "pg_ctl could not start the test-owned server (exit code $LASTEXITCODE)."
    }
    $serverStarted = $true

    $env:FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING = "Host=127.0.0.1;Port=$port;Username=postgres;Database=postgres;Pooling=false;Timeout=5;Command Timeout=60"
    if ($EnableLiveLlm) {
        $env:FEATURE141_RUN_LIVE_LLM_POSTGRES = '1'
        $env:FEATURE141_RUN_LIVE_LLM_MATRIX = '1'
    }

    Push-Location $repoRoot
    try {
        & dotnet test 'tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj' -c Release --filter $TestFilter --logger 'console;verbosity=minimal'
        $testExitCode = $LASTEXITCODE
    }
    finally { Pop-Location }
}
finally {
    Remove-Item Env:FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $dataDirectory) {
        & $pgCtl -D $dataDirectory status *> $null
        if ($LASTEXITCODE -eq 0) {
            & $pgCtl -D $dataDirectory -m fast -w stop
            if ($LASTEXITCODE -ne 0) { throw "Could not stop the test-owned PostgreSQL server for '$dataDirectory'." }
        }
        $remaining = Get-CimInstance Win32_Process -Filter "Name = 'postgres.exe'" |
            Where-Object { $_.CommandLine -and $_.CommandLine.Contains($dataDirectory, [System.StringComparison]::OrdinalIgnoreCase) }
        if ($remaining) { throw "Test-owned PostgreSQL process remains after pg_ctl stop: $($remaining.ProcessId -join ', ')." }
    }

    if (Test-Path -LiteralPath $runRoot) {
        $resolvedRunRoot = (Resolve-Path -LiteralPath $runRoot).Path
        if (-not $resolvedRunRoot.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing cleanup outside the system temporary directory: '$resolvedRunRoot'."
        }
        try { Remove-Item -LiteralPath $resolvedRunRoot -Recurse -Force -ErrorAction Stop }
        catch { throw "Cleanup failed for the scoped Feature 141 test directory '$resolvedRunRoot': $($_.Exception.Message)" }
    }
    Write-Output 'Feature 141 PostgreSQL fixture cleanup completed: test-owned server stopped and scoped directory removed.'
}

if ($testExitCode -ne 0) { exit $testExitCode }
