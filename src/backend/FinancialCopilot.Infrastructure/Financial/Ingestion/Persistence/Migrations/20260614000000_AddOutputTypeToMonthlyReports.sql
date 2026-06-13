START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260613000000_AddProvenanceToStockMarketSyncState') THEN
    ALTER TABLE "StockMarketSyncStates" ADD "LogicalVendor" character varying(64);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260613000000_AddProvenanceToStockMarketSyncState') THEN
    ALTER TABLE "StockMarketSyncStates" ADD "PhysicalSource" character varying(64);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260613000000_AddProvenanceToStockMarketSyncState') THEN
    ALTER TABLE "StockMarketSyncStates" ADD "SourceMode" character varying(32);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260613000000_AddProvenanceToStockMarketSyncState') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260613000000_AddProvenanceToStockMarketSyncState', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260614000000_AddOutputTypeToMonthlyReports') THEN
    ALTER TABLE "MonthlyReports" ADD "OutputType" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260614000000_AddOutputTypeToMonthlyReports') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260614000000_AddOutputTypeToMonthlyReports', '10.0.4');
    END IF;
END $EF$;
COMMIT;

