[CmdletBinding()]
param([string]$BaseUrl = 'https://localhost:7443', [string]$ApiKey)
$ErrorActionPreference = 'Stop'
$secretPath = Join-Path $PSScriptRoot '../local-state/feature139-secrets.json'
if ([string]::IsNullOrWhiteSpace($ApiKey)) { $ApiKey = (Get-Content -LiteralPath $secretPath -Raw | ConvertFrom-Json).ApiKey }
$headers = @{ 'X-Api-Key' = $ApiKey; 'X-Correlation-Id' = [guid]::NewGuid().ToString(); 'X-SapioAI-Contract-Version' = '2' }
function Assert($condition, $message) { if (-not $condition) { throw "FAIL: $message" }; Write-Host "PASS: $message" }
function Issue($id, $simulation='Normal') {
    Invoke-RestMethod -Uri "$BaseUrl/api/financial-copilot/launch" -Method Post -ContentType 'application/json' -Body (@{ externalUserId=$id; simulation=$simulation } | ConvertTo-Json)
}
foreach ($plan in @('Free','Plus','Pro')) {
    $state = Invoke-RestMethod -Uri "$BaseUrl/dev/state"
    $u = $state.users[0]
    Invoke-RestMethod -Uri "$BaseUrl/dev/users/$($u.externalUserId)" -Method Put -ContentType 'application/json' -Body (@{ plan=$plan; isActive=($plan -ne 'Free'); status='Active'; effectiveFrom=[DateTimeOffset]::UtcNow.AddDays(-1).ToString('o'); expiresAt=[DateTimeOffset]::UtcNow.AddDays(29).ToString('o'); revision=42 } | ConvertTo-Json) | Out-Null
    $launch = Issue $u.externalUserId
    $code = ([uri]$launch.launchUrl).Query -replace '^\?code=',''
    $exchange = Invoke-RestMethod -Uri "$BaseUrl/api/financial-copilot/session-exchange" -Method Post -Headers $headers -ContentType 'application/json' -Body (@{code=[uri]::UnescapeDataString($code)} | ConvertTo-Json)
    Assert ($exchange.subscription.plan -eq $plan -and $exchange.subscription.revision -eq 42 -and $exchange.subscription.effectiveFrom) "v2 $plan exchange contains effectiveFrom/revision"
}
$badKey = @{ 'X-Api-Key'='incorrect'; 'X-SapioAI-Contract-Version'='2' }
$bad = $false; try { Invoke-WebRequest -Uri "$BaseUrl/api/financial-copilot/users/dev-user-001/status" -Headers $badKey | Out-Null } catch { $bad=$_.Exception.Response.StatusCode.value__ -eq 401 }
Assert $bad 'incorrect service API key rejected'
$missingVersion = $false; try { Invoke-WebRequest -Uri "$BaseUrl/api/financial-copilot/users/dev-user-001/status" -Headers @{ 'X-Api-Key'=$ApiKey } | Out-Null } catch { $missingVersion=$_.Exception.Response.StatusCode.value__ -eq 406 }
Assert $missingVersion 'missing v2 negotiation rejected'
$status = Invoke-RestMethod -Uri "$BaseUrl/api/financial-copilot/users/dev-user-001/status" -Headers $headers
Assert (-not $status.user.PSObject.Properties['nationalId'] -and -not $status.user.PSObject.Properties['mobile']) 'status response carries no profile PII'
$expiredLaunch = Issue 'dev-user-001' 'Expired'
$expiredUri = [System.Uri]$expiredLaunch.launchUrl
$expiredCode = [System.Uri]::UnescapeDataString($expiredUri.Query.Substring(6))
$expired = $false; try { Invoke-WebRequest -Uri "$BaseUrl/api/financial-copilot/session-exchange" -Method Post -Headers $headers -ContentType 'application/json' -Body (@{code=$expiredCode}|ConvertTo-Json) | Out-Null } catch { $expired=$_.Exception.Response.StatusCode.value__ -eq 400 }
Assert $expired 'expired LaunchCode rejected'
$reusedLaunch = Issue 'dev-user-001' 'Reused'
$reusedUri = [System.Uri]$reusedLaunch.launchUrl
$reusedCode = [System.Uri]::UnescapeDataString($reusedUri.Query.Substring(6))
$reused = $false; try { Invoke-WebRequest -Uri "$BaseUrl/api/financial-copilot/session-exchange" -Method Post -Headers $headers -ContentType 'application/json' -Body (@{code=$reusedCode}|ConvertTo-Json) | Out-Null } catch { $reused=$_.Exception.Response.StatusCode.value__ -eq 400 }
Assert $reused 'reused LaunchCode rejected'
$invalidLaunch = Issue 'dev-user-001' 'Invalid'
$invalidUri = [System.Uri]$invalidLaunch.launchUrl
$invalidCode = [System.Uri]::UnescapeDataString($invalidUri.Query.Substring(6))
$invalid = $false; try { Invoke-WebRequest -Uri "$BaseUrl/api/financial-copilot/session-exchange" -Method Post -Headers $headers -ContentType 'application/json' -Body (@{code=$invalidCode}|ConvertTo-Json) | Out-Null } catch { $invalid=$_.Exception.Response.StatusCode.value__ -eq 400 }
Assert $invalid 'unknown LaunchCode rejected'
$oneTimeLaunch = Issue 'dev-user-001'
$oneTimeUri = [System.Uri]$oneTimeLaunch.launchUrl
$oneTimeCode = [System.Uri]::UnescapeDataString($oneTimeUri.Query.Substring(6))
$exchangeUri = "$BaseUrl/api/financial-copilot/session-exchange"
$payload = @{code=$oneTimeCode}|ConvertTo-Json
Invoke-RestMethod -Uri $exchangeUri -Method Post -Headers $headers -ContentType 'application/json' -Body $payload | Out-Null
$replay = $false; try { Invoke-WebRequest -Uri $exchangeUri -Method Post -Headers $headers -ContentType 'application/json' -Body $payload | Out-Null } catch { $replay=$_.Exception.Response.StatusCode.value__ -eq 400 }
Assert $replay 'a successfully exchanged LaunchCode cannot be exchanged a second time'
$failureSettings = @{ exchange=@{statusCode=200;delayMilliseconds=0}; status=@{statusCode=503;delayMilliseconds=0} } | ConvertTo-Json -Depth 4
Invoke-RestMethod -Uri "$BaseUrl/dev/failures" -Method Put -ContentType 'application/json' -Body $failureSettings | Out-Null
$failed = $false; try { Invoke-WebRequest -Uri "$BaseUrl/api/financial-copilot/users/dev-user-001/status" -Headers $headers | Out-Null } catch { $failed=$_.Exception.Response.StatusCode.value__ -eq 503 }
Invoke-RestMethod -Uri "$BaseUrl/dev/failures" -Method Put -ContentType 'application/json' -Body (@{exchange=@{statusCode=200;delayMilliseconds=0};status=@{statusCode=200;delayMilliseconds=0}}|ConvertTo-Json -Depth 4) | Out-Null
Assert $failed 'HTTP failure simulation returns configured status'
Write-Host 'Mock contract smoke test completed.'
