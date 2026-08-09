# Completion Gate Evidence

Run the repository checks from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/verify-telegram-gateway-completion.ps1
dotnet build src/backend/FinancialCopilot.sln --configuration Release --no-restore
docker compose --env-file docker/telegram-gateway.env.example -f docker/telegram-gateway.compose.yml config --quiet
```

The script verifies that the Gateway is separately deployable, has no primary data-plane project
dependency, has persistent transport state, is not publicly exposed as plaintext, has no Bot Token
in VPS1 settings, and that the legacy Worker polling service is not registered.

The following evidence must be attached from the target environments before declaring the feature
production-complete:

- VPS2 Gateway health, TLS, restart, and persisted-offset evidence.
- VPS1-to-VPS2 signed request and dedicated API-key evidence.
- Test-bot evidence for message, callback, `/start` linking, membership, notification, retry, and
  restart behavior.
- Firewall/security-group evidence that VPS2 cannot reach PostgreSQL, Redis, or RabbitMQ.
- Confirmation that the old poller is stopped, followed by Bot Token rotation.
