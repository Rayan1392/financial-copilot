<#
.SYNOPSIS
Starts, stops, or inspects a disposable local PostgreSQL instance for the Feature 141 isolated
engine end-to-end runner when Docker/Testcontainers is unavailable.

.DESCRIPTION
The instance is initialised with its own data directory and its own administrative role, listens
only on 127.0.0.1 on a non-default port, and never touches the application PostgreSQL service
(port 5432) or any existing database. Its administrative password is generated per data directory,
written to an ACL-restricted file next to the data directory, and never printed. The connection
string written to `connection.txt` is consumed by the integration test fixture through the
FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING environment variable.

.EXAMPLE
pwsh tools/SemanticCatalog/scripts/Feature141DisposablePostgres.ps1 -Action start
$env:FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING = Get-Content "$env:LOCALAPPDATA\feature141-disposable-postgres\connection.txt" -Raw
#>
[CmdletBinding()]
param(
    [ValidateSet('start', 'stop', 'status', 'destroy')]
    [string]$Action = 'start',
    [string]$Root = (Join-Path $env:LOCALAPPDATA 'feature141-disposable-postgres'),
    [ValidateRange(1024, 65535)]
    [int]$Port = 55441,
    [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin',
    [string]$AdminRole = 'feature141_e2e_admin'
)

$ErrorActionPreference = 'Stop'
$dataDirectory = Join-Path $Root 'data'
$logFile = Join-Path $Root 'postgres.log'
$passwordFile = Join-Path $Root 'admin.pwfile'
$connectionFile = Join-Path $Root 'connection.txt'
$initdb = Join-Path $PgBin 'initdb.exe'
$pgCtl = Join-Path $PgBin 'pg_ctl.exe'
$pgIsReady = Join-Path $PgBin 'pg_isready.exe'

foreach ($tool in @($initdb, $pgCtl, $pgIsReady)) {
    if (-not (Test-Path $tool)) { throw "PostgreSQL binary not found: $tool. Pass -PgBin." }
}
if ($Port -eq 5432 -or $Port -eq 5433) {
    throw 'Refusing to use a port that may belong to the application PostgreSQL services.'
}

function Restrict-ToCurrentUser([string]$path) {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    & icacls $path /inheritance:r /grant:r "${identity}:(F)" | Out-Null
}

function New-RandomPassword {
    $bytes = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return ([System.Convert]::ToBase64String($bytes) -replace '[^A-Za-z0-9]', '').Substring(0, 32)
}

function Get-Status {
    & $pgCtl -D $dataDirectory status 2>&1 | Out-String
    return $LASTEXITCODE
}

switch ($Action) {
    'status' {
        if (-not (Test-Path $dataDirectory)) { Write-Host "No disposable instance at $Root."; exit 0 }
        $null = Get-Status
        & $pgIsReady -h 127.0.0.1 -p $Port
        exit $LASTEXITCODE
    }
    'stop' {
        if (-not (Test-Path $dataDirectory)) { Write-Host "No disposable instance at $Root."; exit 0 }
        & $pgCtl -D $dataDirectory -m fast stop
        exit $LASTEXITCODE
    }
    'destroy' {
        if (Test-Path $dataDirectory) {
            try { & $pgCtl -D $dataDirectory -m fast stop | Out-Null } catch { }
        }
        if (Test-Path $Root) { Remove-Item -Recurse -Force $Root }
        Write-Host "Removed disposable instance at $Root."
        exit 0
    }
    'start' {
        New-Item -ItemType Directory -Force $Root | Out-Null
        if (-not (Test-Path $dataDirectory)) {
            $password = New-RandomPassword
            Set-Content -Path $passwordFile -Value $password -Encoding ascii -NoNewline
            Restrict-ToCurrentUser $passwordFile
            & $initdb -D $dataDirectory -U $AdminRole --pwfile=$passwordFile -E UTF8 --locale=C `
                --auth-host=scram-sha-256 --auth-local=scram-sha-256 | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'initdb failed; see console output.' }
            Add-Content -Path (Join-Path $dataDirectory 'postgresql.conf') -Value @"

# Feature 141 disposable instance: loopback only, non-default port.
listen_addresses = '127.0.0.1'
port = $Port
max_connections = 40
"@
            Set-Content -Path $connectionFile -Encoding ascii -NoNewline -Value `
                "Host=127.0.0.1;Port=$Port;Database=postgres;Username=$AdminRole;Password=$password;Pooling=false"
            Restrict-ToCurrentUser $connectionFile
        }
        $null = Get-Status
        if ($LASTEXITCODE -ne 0) {
            # Start through a detached hidden process so the server does not inherit this console.
            # Do not use -Wait: on Windows it waits for the whole process tree, i.e. the server itself.
            Start-Process -FilePath $pgCtl -WindowStyle Hidden `
                -ArgumentList @('-D', "`"$dataDirectory`"", '-l', "`"$logFile`"", 'start') | Out-Null
            $deadline = (Get-Date).AddSeconds(60)
            do {
                Start-Sleep -Milliseconds 500
                & $pgIsReady -h 127.0.0.1 -p $Port -q
            } while ($LASTEXITCODE -ne 0 -and (Get-Date) -lt $deadline)
        }
        & $pgIsReady -h 127.0.0.1 -p $Port | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "PostgreSQL is not accepting connections on 127.0.0.1:$Port; see $logFile." }
        Write-Host "Disposable PostgreSQL ready on 127.0.0.1:$Port (data: $dataDirectory)."
        Write-Host "Connection string file (ACL-restricted, not printed): $connectionFile"
        exit 0
    }
}
