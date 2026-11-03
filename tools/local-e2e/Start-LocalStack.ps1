[CmdletBinding()]
param([switch]$NoBrowser)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$localState = Join-Path $repo 'tools/local-state'
$secretPath = Join-Path $localState 'feature139-secrets.json'
$keyRing = Join-Path $localState 'dataprotection'
$pfxPath = Join-Path $localState 'localhost-dev.pfx'
New-Item -ItemType Directory -Force -Path $localState,$keyRing | Out-Null

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker Desktop with the Linux engine is required for the isolated local PostgreSQL database.' }
docker compose -f (Join-Path $PSScriptRoot 'docker-compose.yml') up -d --wait
if ($LASTEXITCODE -ne 0) { throw 'The isolated Feature 139 PostgreSQL service did not start.' }

dotnet dev-certs https --trust | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not trust the local ASP.NET Core development certificate.' }
$pfxPassword = 'local-feature139-dev-cert'
dotnet dev-certs https --export-path $pfxPath --password $pfxPassword | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not export the local HTTPS development certificate for Vite.' }

if (-not (Test-Path $secretPath)) {
    $bytes = [byte[]]::new(32)
    [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $secret = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_')
    @{ ApiKey = $secret; PfxPassword = $pfxPassword } | ConvertTo-Json | Set-Content -LiteralPath $secretPath -Encoding UTF8
}
$secrets = Get-Content -LiteralPath $secretPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($secrets.ApiKey) -or $secrets.ApiKey.Length -lt 40) { throw 'The local development API key is missing or too short.' }
$env:FEATURE139_SECRET_FILE = $secretPath
$env:FEATURE139_REPO_ROOT = $repo
$helper = Join-Path $PSScriptRoot 'Run-LocalService.ps1'
$proc = (Get-Process -Id $PID).Path
$started = @()
foreach ($service in @('mock','api','frontend')) {
    $started += (Start-Process -FilePath $proc -PassThru -WindowStyle Hidden -ArgumentList @('-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',"`"$helper`"",'-Service',$service)).Id
}
@{ ProcessIds = $started } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $localState 'service-pids.json') -Encoding UTF8
Write-Host 'Feature 139 local stack started: mock https://localhost:7443, API https://localhost:7044, frontend https://localhost:5173.'
Write-Host "Local API key is stored only in ignored local state: $secretPath"
Write-Host 'Run .\tools\local-e2e\Test-MockContract.ps1 after the mock health endpoint responds.'
foreach ($endpoint in @('https://localhost:7443/health','https://localhost:7044/health','https://localhost:5173/')) {
    $ready = $false
    for ($attempt = 0; $attempt -lt 90 -and -not $ready; $attempt++) {
        try { Invoke-WebRequest -Uri $endpoint -TimeoutSec 2 | Out-Null; $ready = $true }
        catch { Start-Sleep -Seconds 1 }
    }
    if (-not $ready) { throw "Local service did not become ready: $endpoint. Check tools/local-state logs." }
}
if (-not $NoBrowser) { Start-Process 'https://localhost:7443' }
