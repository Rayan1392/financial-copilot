-- Feature 164 measurement: ConversationDbContext schema, generated 2026-11-20 with
--   dotnet ef migrations script --idempotent --context ConversationDbContext \
--     --project src/backend/FinancialCopilot.Infrastructure --startup-project src/backend/FinancialCopilot.API
-- (migrations up to 20260807120513_AddConversationTaskState). Unedited apart from this header.

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260527064948_InitialConversations') THEN
    CREATE TABLE "Conversations" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ActorId" uuid NOT NULL,
        "StartedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "MessageCount" integer NOT NULL,
        CONSTRAINT "PK_Conversations" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260527064948_InitialConversations') THEN
    CREATE TABLE "Messages" (
        "Id" uuid NOT NULL,
        "ConversationId" uuid NOT NULL,
        "Role" character varying(20) NOT NULL,
        "Content" text NOT NULL,
        "ScannerQueryPlanJson" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Messages" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260527064948_InitialConversations') THEN
    CREATE INDEX "IX_Conversations_TenantId_ActorId_UpdatedAt" ON "Conversations" ("TenantId", "ActorId", "UpdatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260527064948_InitialConversations') THEN
    CREATE INDEX "IX_Messages_ConversationId_CreatedAt" ON "Messages" ("ConversationId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260527064948_InitialConversations') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260527064948_InitialConversations', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601160253_AddConversationLifecycle') THEN
    ALTER TABLE "Messages" ADD "AssistantPayloadJson" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601160253_AddConversationLifecycle') THEN
    ALTER TABLE "Conversations" ADD "Title" character varying(160) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601160253_AddConversationLifecycle') THEN
    UPDATE "Conversations" SET "Title" = 'New conversation' WHERE "Title" = '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601160253_AddConversationLifecycle') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260601160253_AddConversationLifecycle', '10.0.13');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260807120513_AddConversationTaskState') THEN
    CREATE TABLE "ConversationTaskStates" (
        "ConversationId" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ActorId" uuid NOT NULL,
        "Version" bigint NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "LastCorrelationId" character varying(200),
        "StateJson" text NOT NULL,
        CONSTRAINT "PK_ConversationTaskStates" PRIMARY KEY ("ConversationId")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260807120513_AddConversationTaskState') THEN
    CREATE INDEX "IX_ConversationTaskStates_TenantId_ActorId_ExpiresAt" ON "ConversationTaskStates" ("TenantId", "ActorId", "ExpiresAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260807120513_AddConversationTaskState') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260807120513_AddConversationTaskState', '10.0.13');
    END IF;
END $EF$;
COMMIT;

