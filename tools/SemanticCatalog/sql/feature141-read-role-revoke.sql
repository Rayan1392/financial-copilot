-- Manual, DBA-reviewed revocation in the same designated Development database.
\set ON_ERROR_STOP on
\set expected_dev_database 'REPLACE_WITH_EXACT_DEVELOPMENT_DATABASE'
SELECT current_database() = :'expected_dev_database' AS correct_dev_database \gset
\if :correct_dev_database
\else
  \echo 'Refusing: connected database does not equal expected Development database.'
  \quit 3
\endif

REVOKE SELECT ("Id", "ProviderName", "StatementType", "SourceItemId", "Unit")
  ON TABLE public."FinancialStatementSourceItems" FROM feature141_reader;
REVOKE SELECT ("Value", "FinancialStatementId", "SourceItemCatalogId")
  ON TABLE public."FinancialStatementLineItems" FROM feature141_reader;
REVOKE SELECT (
  "Id", "CompanyId", "ProviderName", "StatementType", "PeriodEnd", "PeriodType",
  "IsComposing", "IsAudited", "IsRepresented", "ExternalStatementId", "PeriodStart",
  "SourcePayloadChecksum", "PublishedAt", "LastSynchronizedAt", "WarningsJson",
  "LogicalVendor", "SourceMode"
) ON TABLE public."FinancialStatements" FROM feature141_reader;
REVOKE SELECT ("Id") ON TABLE public."Companies" FROM feature141_reader;
REVOKE USAGE ON SCHEMA public FROM feature141_reader;
DO $disconnect$
BEGIN
  EXECUTE format('REVOKE CONNECT ON DATABASE %I FROM feature141_reader', current_database());
END
$disconnect$;
ALTER ROLE feature141_reader RESET default_transaction_read_only;

-- Remove Feature141:QueryExecution secrets/config separately, then verify no remaining ACLs,
-- role memberships, owned objects, or active sessions. Do not DROP ROLE automatically.
-- Only after owner review, inspect pg_shdepend/pg_auth_members and run DROP OWNED / DROP ROLE manually.
