# Database Migration Update Help

Use this guide when applying EF Core migrations to the local PostgreSQL `financial_copilot` database.

## Prerequisites

Run commands from the repository root:

```powershell
cd D:\Source\TahlilApp-AI
```

Verify the solution builds before touching the database:

```powershell
dotnet build src/backend/FinancialCopilot.sln --configuration Release
```

Verify `dotnet-ef` is available:

```powershell
dotnet ef --version
```

If it is missing:

```powershell
dotnet tool install --global dotnet-ef
```

## Connection

The API and migration startup project read `ConnectionStrings:FinancialCopilot`.
For local development this is normally configured in `src/backend/FinancialCopilot.API/appsettings.json` or environment configuration.

To override it for one PowerShell session:

```powershell
$env:ConnectionStrings__FinancialCopilot = "Server=localhost;Port=5432;Database=financial_copilot;User Id=maahfit;Password=<password>"
```

## Always Back Up First

Before applying migrations to a database with real data, take a backup.
See [PostgreSQL Backup & Restore](postgresql-backup-restore.md).

At minimum, create a custom-format backup:

```powershell
$env:PGPASSWORD = "<password>"
$backupDir = "C:\Backups\financial_copilot"
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

& "C:\Program Files\PostgreSQL\17\bin\pg_dump.exe" `
    --host localhost --port 5432 --username maahfit `
    --format custom --compress 9 `
    --file "$backupDir\pre_migration_$(Get-Date -Format 'yyyyMMdd_HHmmss').dump" `
    financial_copilot
```

## Apply All Known DbContexts

This repository has multiple EF Core contexts. Apply migrations context by context so failures are easy to identify.

```powershell
$project = "src/backend/FinancialCopilot.Infrastructure"
$startup = "src/backend/FinancialCopilot.API"

dotnet ef database update --project $project --startup-project $startup --context AuthDbContext
dotnet ef database update --project $project --startup-project $startup --context BillingDbContext
dotnet ef database update --project $project --startup-project $startup --context SemanticCatalogDbContext
dotnet ef database update --project $project --startup-project $startup --context FinancialProviderDbContext
dotnet ef database update --project $project --startup-project $startup --context FinancialIngestionDbContext
dotnet ef database update --project $project --startup-project $startup --context ConversationDbContext
dotnet ef database update --project $project --startup-project $startup --context MemoryDbContext
```

## Apply One Context

For a migration that only affects Billing:

```powershell
dotnet ef database update `
    --project src/backend/FinancialCopilot.Infrastructure `
    --startup-project src/backend/FinancialCopilot.API `
    --context BillingDbContext
```

For a migration that only affects normalized financial data:

```powershell
dotnet ef database update `
    --project src/backend/FinancialCopilot.Infrastructure `
    --startup-project src/backend/FinancialCopilot.API `
    --context FinancialIngestionDbContext
```

## List Migrations

Use this before updating when you need to confirm pending migrations:

```powershell
dotnet ef migrations list `
    --project src/backend/FinancialCopilot.Infrastructure `
    --startup-project src/backend/FinancialCopilot.API `
    --context BillingDbContext
```

Replace `BillingDbContext` with the target context.

## Generate SQL Before Applying

For review or production change control, generate an idempotent script:

```powershell
dotnet ef migrations script --idempotent `
    --project src/backend/FinancialCopilot.Infrastructure `
    --startup-project src/backend/FinancialCopilot.API `
    --context BillingDbContext `
    --output artifacts/billing-migrations.sql
```

Create the `artifacts` folder first if it does not exist:

```powershell
New-Item -ItemType Directory -Force -Path artifacts | Out-Null
```

## Important Operational Notes

- Stop the API and Worker before applying migrations that change tables used by background jobs.
- Read migration files before applying them. Some historical migrations intentionally truncate tables.
- Apply `AuthDbContext` and `BillingDbContext` before using admin or authenticated billing endpoints.
- After `FinancialIngestionDbContext` migrations that reset or alter source data, rerun the relevant data sync jobs.
- Do not manually edit `__EFMigrationsHistory` unless you are repairing a database from backup and know exactly which migrations were applied.

## Verify After Update

Run the backend tests:

```powershell
dotnet test src/backend/FinancialCopilot.sln --configuration Release --no-restore
```

Start the API and check health:

```powershell
dotnet run --project src/backend/FinancialCopilot.API
```

In another shell:

```powershell
Invoke-RestMethod http://localhost:5074/health
```

## Common Failures

### `dotnet-ef does not exist`

Install the tool:

```powershell
dotnet tool install --global dotnet-ef
```

Restart the terminal if the global tool path is not picked up.

### `Connection string 'FinancialCopilot' is required`

Set `ConnectionStrings__FinancialCopilot` or verify `appsettings.json`.

### `relation already exists` or duplicate object errors

The database may have schema objects created outside EF or from a partially applied migration. Restore from backup if this happened during a failed update, or generate an idempotent SQL script and inspect the exact failing statement.

### Migration times out or locks

Stop the API and Worker, verify no ingestion or scheduler job is running, then retry. For large data migrations, use a maintenance window.
