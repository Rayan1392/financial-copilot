[CmdletBinding()]
param(
    [string] $RepositoryRoot = ""
)

$ErrorActionPreference = "Stop"
$RepositoryRoot = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
} else {
    (Resolve-Path $RepositoryRoot).Path
}
$failures = [System.Collections.Generic.List[string]]::new()

function Assert-Gate([bool] $condition, [string] $message) {
    if ($condition) { Write-Host "PASS: $message" -ForegroundColor Green }
    else { $failures.Add($message); Write-Host "FAIL: $message" -ForegroundColor Red }
}

$gatewayProject = Join-Path $RepositoryRoot "src/backend/FinancialCopilot.TelegramGateway/FinancialCopilot.TelegramGateway.csproj"
$gatewayCompose = Join-Path $RepositoryRoot "docker/telegram-gateway.compose.yml"
$gatewayDockerfile = Join-Path $RepositoryRoot "docker/telegram-gateway.Dockerfile"
$runbook = Join-Path $RepositoryRoot "specs/124-telegram-gateway-vps-isolation/deployment.md"
$security = Join-Path $RepositoryRoot "specs/124-telegram-gateway-vps-isolation/security.md"
$tasks = Join-Path $RepositoryRoot "specs/124-telegram-gateway-vps-isolation/tasks.md"

Assert-Gate (Test-Path $gatewayProject) "Dedicated Gateway project exists."
Assert-Gate (Test-Path $gatewayCompose) "Gateway Compose deployment exists."
Assert-Gate (Test-Path $gatewayDockerfile) "Gateway Dockerfile exists."
Assert-Gate (Test-Path $runbook) "Deployment and rollback runbook exists."
Assert-Gate (Test-Path $security) "Security and rotation runbook exists."

if (Test-Path $gatewayProject) {
    $projectText = Get-Content $gatewayProject -Raw
    Assert-Gate ($projectText -notmatch "FinancialCopilot\.(Infrastructure|API|Worker)") "Gateway has no API, Worker, or Infrastructure project dependency."
}

$workerProgram = Get-Content (Join-Path $RepositoryRoot "src/backend/FinancialCopilot.Worker/Program.cs") -Raw
Assert-Gate ($workerProgram -notmatch "AddHostedService<TelegramDevPollingWorker>") "Legacy Worker Telegram polling is not registered."

foreach ($settingsPath in @(
    (Join-Path $RepositoryRoot "src/backend/FinancialCopilot.API/appsettings.json"),
    (Join-Path $RepositoryRoot "src/backend/FinancialCopilot.Worker/appsettings.json")
)) {
    $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
    $telegram = $settings.Telegram
    $tokenValues = @(
        $telegram.Notifications.BotToken,
        $telegram.DevPolling.BotToken
    ) | Where-Object { $_ -is [string] -and -not [string]::IsNullOrWhiteSpace($_) }
    Assert-Gate ($tokenValues.Count -eq 0) "No Telegram Bot Token is stored in $([IO.Path]::GetFileName($settingsPath))."
}

$composeText = Get-Content $gatewayCompose -Raw
Assert-Gate ($composeText -match "telegram_gateway_state") "Gateway offset/idempotency state uses a persistent volume."
Assert-Gate ($composeText -match "read_only: true") "Gateway container filesystem is read-only except for its state volume."
Assert-Gate ($composeText -match "127\.0\.0\.1") "Gateway container is not directly exposed as a public plaintext service."

$taskText = Get-Content $tasks -Raw
Assert-Gate ($taskText -match "Disable the old polling/transport path only after end-to-end verification") "Cutover checklist contains the old-poller shutdown gate."

if ($failures.Count -gt 0) {
    Write-Error ("Telegram Gateway completion gate failed: {0} check(s)." -f $failures.Count)
    exit 1
}

Write-Host "Telegram Gateway repository completion gate passed. Live VPS/test-bot evidence is still required before production completion." -ForegroundColor Green
