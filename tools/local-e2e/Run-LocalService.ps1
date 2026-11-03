param([Parameter(Mandatory)][ValidateSet('mock','api','frontend')][string]$Service)
$ErrorActionPreference = 'Stop'
$repo = $env:FEATURE139_REPO_ROOT
$secrets = Get-Content -LiteralPath $env:FEATURE139_SECRET_FILE -Raw | ConvertFrom-Json
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = if ($Service -eq 'mock') { 'https://localhost:7443' } else { 'https://localhost:7044' }
$env:TAHLILAPP_EXCHANGE_API_KEY = $secrets.ApiKey
$env:Mock__ServiceApiKey = $secrets.ApiKey
$env:Mock__SapioFrontendOrigin = 'https://localhost:5173'
$env:ConnectionStrings__FinancialCopilot = 'Host=127.0.0.1;Port=55439;Database=sapioai_feature139_local;Username=sapioai_local;Password=local_feature139_only;Timeout=5;Command Timeout=30'
$env:ConnectionStrings__Feature141ReadOnly = ''
$env:Database__ApplyMigrationsOnStartup = 'true'
$env:ConnectionStrings__Redis = ''
$env:ScannerCache__UseRedis = 'false'
$env:CyclicalWavesDataAcquisition__Enabled = 'false'
$env:Feature141__Enabled = 'false'
$env:Cors__AllowedOrigins__0 = 'https://localhost:5173'
$env:Cors__AllowedOrigins__1 = 'https://127.0.0.1:5173'
$env:QuarterlyFinancialReports__BankingIndustryGroupId = 'c96520f9-1d2e-444a-ab46-5bdffd3797d2'
$env:Authentication__JwtBearer__SigningKey = 'local-only-signing-key-Feature139-32-bytes-minimum'
$env:Authentication__TahlilApp__Enabled = 'true'
$env:Authentication__TahlilApp__BaseUrl = 'https://localhost:7443'
$env:Authentication__TahlilApp__ApiKeyEnvironmentVariable = 'TAHLILAPP_EXCHANGE_API_KEY'
$env:Authentication__TahlilApp__DataProtectionKeyRingPath = Join-Path $repo 'tools/local-state/dataprotection'
$env:DataProtection__KeyRingPath = Join-Path $repo 'tools/local-state/dataprotection'
$env:Authentication__TahlilApp__Revalidation__Enabled = 'true'
$env:Authentication__TahlilApp__Revalidation__PollSeconds = '10'
$env:Authentication__TahlilApp__Revalidation__StatusTtlMinutes = '1'
$env:Authentication__TahlilApp__Revalidation__StaleGraceHours = '6'
$logDir = Join-Path $repo 'tools/local-state'
New-Item -ItemType Directory -Force $logDir | Out-Null
Set-Location $repo
if ($Service -eq 'mock') {
    dotnet run --project tools/TahlilApp.MockServer/TahlilApp.MockServer.csproj --no-launch-profile --urls $env:ASPNETCORE_URLS *> (Join-Path $logDir 'mock.log')
} elseif ($Service -eq 'api') {
    Set-Location (Join-Path $repo 'src/backend/FinancialCopilot.API')
    dotnet run --no-launch-profile --urls $env:ASPNETCORE_URLS *> (Join-Path $logDir 'api.log')
} else {
    $env:ASPNETCORE_URLS = $null
    $env:VITE_FINANCIAL_COPILOT_API_BASE_URL = 'https://localhost:7044'
    $env:VITE_DEV_CERT_PFX = Join-Path $repo 'tools/local-state/localhost-dev.pfx'
    $env:VITE_DEV_CERT_PASSWORD = $secrets.PfxPassword
    Set-Location (Join-Path $repo 'src/frontend')
    npm run dev -- --host 127.0.0.1 --port 5173 *> (Join-Path $logDir 'frontend.log')
}
