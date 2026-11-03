$ErrorActionPreference = 'Stop'
$pidFile = Join-Path $PSScriptRoot '../local-state/service-pids.json'
if (Test-Path $pidFile) {
    $processIds = (Get-Content -LiteralPath $pidFile -Raw | ConvertFrom-Json).ProcessIds
    foreach ($processId in $processIds) { & taskkill.exe /PID $processId /T /F 2>$null | Out-Null }
    Remove-Item -LiteralPath $pidFile -Force
}
docker compose -f (Join-Path $PSScriptRoot 'docker-compose.yml') down
