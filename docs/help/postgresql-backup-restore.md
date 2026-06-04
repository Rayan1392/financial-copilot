# PostgreSQL Backup & Restore — financial_copilot

PostgreSQL 17 tools are at `C:\Program Files\PostgreSQL\17\bin\`.  
Add that directory to your `PATH` or prefix every command with the full path.

**Connection details** (read from `appsettings.json` / environment):

| Setting  | Value             |
|----------|-------------------|
| Host     | localhost         |
| Port     | 5432              |
| Database | financial_copilot |
| User     | maahfit           |

**Backup output folder:** all commands below save files to `C:\Backups\financial_copilot\`.  
Change `$backupDir` to any path you prefer. The folder is created automatically if it does not exist.

Set the password once per session to avoid prompts:

```powershell
$env:PGPASSWORD = "Rayan1392!"
```

---

## Backup

### Recommended — custom format (smallest, supports partial restore)

```powershell
$env:PGPASSWORD = "Rayan1392!"
$backupDir = "C:\Backups\financial_copilot"
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

& "C:\Program Files\PostgreSQL\17\bin\pg_dump.exe" `
    --host localhost --port 5432 --username maahfit `
    --format custom --compress 9 `
    --file "$backupDir\financial_copilot_$(Get-Date -Format 'yyyyMMdd_HHmmss').dump" `
    financial_copilot
```

The `.dump` file is cross-platform and can restore individual tables.  
Example output path: `C:\Backups\financial_copilot\financial_copilot_20260604_143022.dump`

### Plain SQL (human-readable, easy to inspect)

```powershell
$env:PGPASSWORD = "Rayan1392!"
$backupDir = "C:\Backups\financial_copilot"
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

& "C:\Program Files\PostgreSQL\17\bin\pg_dump.exe" `
    --host localhost --port 5432 --username maahfit `
    --format plain `
    --file "$backupDir\financial_copilot_$(Get-Date -Format 'yyyyMMdd_HHmmss').sql" `
    financial_copilot
```

### Directory format (parallel dump, fastest for large databases)

```powershell
$env:PGPASSWORD = "Rayan1392!"
$backupDir = "C:\Backups\financial_copilot"
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

& "C:\Program Files\PostgreSQL\17\bin\pg_dump.exe" `
    --host localhost --port 5432 --username maahfit `
    --format directory --jobs 4 `
    --file "$backupDir\financial_copilot_$(Get-Date -Format 'yyyyMMdd_HHmmss')" `
    financial_copilot
```

The output is a **folder**, not a single file (e.g. `C:\Backups\financial_copilot\financial_copilot_20260604_143022\`).

### Schema only (no data — useful before migrations)

```powershell
$env:PGPASSWORD = "Rayan1392!"
$backupDir = "C:\Backups\financial_copilot"
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

& "C:\Program Files\PostgreSQL\17\bin\pg_dump.exe" `
    --host localhost --port 5432 --username maahfit `
    --schema-only `
    --file "$backupDir\financial_copilot_schema_$(Get-Date -Format 'yyyyMMdd_HHmmss').sql" `
    financial_copilot
```

---

## Restore

### From custom format

```powershell
$env:PGPASSWORD = "Rayan1392!"
& "C:\Program Files\PostgreSQL\17\bin\pg_restore.exe" `
    --host localhost --port 5432 --username maahfit `
    --dbname financial_copilot `
    --clean --if-exists `
    "C:\Backups\financial_copilot\financial_copilot_20260604_143022.dump"
```

`--clean --if-exists` drops existing objects before recreating them.  
Omit `--clean` to restore into a fresh empty database.

### From plain SQL

```powershell
$env:PGPASSWORD = "Rayan1392!"
& "C:\Program Files\PostgreSQL\17\bin\psql.exe" `
    --host localhost --port 5432 --username maahfit `
    --dbname financial_copilot `
    --file "C:\Backups\financial_copilot\financial_copilot_20260604_143022.sql"
```

### Restore a single table (custom format only)

```powershell
$env:PGPASSWORD = "Rayan1392!"
& "C:\Program Files\PostgreSQL\17\bin\pg_restore.exe" `
    --host localhost --port 5432 --username maahfit `
    --dbname financial_copilot `
    --table billing_subscription_plans `
    "C:\Backups\financial_copilot\financial_copilot_20260604_143022.dump"
```

---

## Restore to a fresh database

If you need a clean target database first:

```powershell
$env:PGPASSWORD = "Rayan1392!"
& "C:\Program Files\PostgreSQL\17\bin\psql.exe" `
    --host localhost --port 5432 --username maahfit `
    --dbname postgres `
    --command "DROP DATABASE IF EXISTS financial_copilot; CREATE DATABASE financial_copilot OWNER maahfit;"

& "C:\Program Files\PostgreSQL\17\bin\pg_restore.exe" `
    --host localhost --port 5432 --username maahfit `
    --dbname financial_copilot `
    "C:\Backups\financial_copilot\financial_copilot_20260604_143022.dump"
```

---

## Scheduled daily backup (Windows Task Scheduler)

Save the script below as `C:\Backups\backup-financial-copilot.ps1`, then register it.

**Script:**

```powershell
# backup-financial-copilot.ps1
$env:PGPASSWORD = "Rayan1392!"
$backupDir = "C:\Backups\financial_copilot"
$file      = "$backupDir\financial_copilot_$(Get-Date -Format 'yyyyMMdd_HHmmss').dump"

New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

& "C:\Program Files\PostgreSQL\17\bin\pg_dump.exe" `
    --host localhost --port 5432 --username maahfit `
    --format custom --compress 9 `
    --file $file `
    financial_copilot

# Keep the last 7 days of backups
Get-ChildItem $backupDir -Filter "*.dump" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -Skip 7 |
    Remove-Item -Force
```

**Register the task (run once as Administrator):**

```powershell
$action  = New-ScheduledTaskAction `
    -Execute "powershell.exe" `
    -Argument "-NonInteractive -File C:\Backups\backup-financial-copilot.ps1"

$trigger = New-ScheduledTaskTrigger -Daily -At "02:00AM"

Register-ScheduledTask `
    -TaskName "financial_copilot daily backup" `
    -Action $action -Trigger $trigger `
    -RunLevel Highest -Force
```

---

## Before applying EF Core migrations

Always take a backup before running `dotnet ef database update`, especially for
migrations that contain `TRUNCATE` statements (e.g. `AddStatementTypeAndFixUniqueKey`
in `FinancialIngestionDbContext`).

```powershell
$env:PGPASSWORD = "Rayan1392!"
$backupDir = "C:\Backups\financial_copilot"
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

& "C:\Program Files\PostgreSQL\17\bin\pg_dump.exe" `
    --host localhost --port 5432 --username maahfit `
    --format custom --compress 9 `
    --file "$backupDir\pre_migration_$(Get-Date -Format 'yyyyMMdd_HHmmss').dump" `
    financial_copilot
```

Verify the file exists and is non-zero before running migrations:

```powershell
Get-Item "$backupDir\pre_migration_*.dump" | Select-Object Name, Length | Sort-Object Name -Descending | Select-Object -First 1
```

---

## Add pg_dump to PATH permanently

```powershell
# Run as Administrator
[Environment]::SetEnvironmentVariable(
    "Path",
    $env:Path + ";C:\Program Files\PostgreSQL\17\bin",
    [EnvironmentVariableTarget]::Machine
)
```

After reopening your terminal, all `pg_dump`, `pg_restore`, and `psql` commands
work without the full path prefix.
