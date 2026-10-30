-- Manual, DBA-reviewed revocation in the same designated database:
--   psql -v expected_database=<exact database name> -f feature141-read-role-revoke.sql
\set ON_ERROR_STOP on
\if :{?expected_database}
\else
  \echo 'Refusing: pass -v expected_database=<exact target database name>.'
  \quit 3
\endif
SELECT current_database() = :'expected_database' AS correct_database \gset
\if :correct_database
\else
  \echo 'Refusing: connected database does not equal expected_database.'
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
REVOKE SELECT ("ExternalCompanyId") ON TABLE public."FinancialStatements" FROM feature141_reader;
REVOKE SELECT ("Id", "ProviderName", "ExternalCompanyId", "Name", "Ticker", "CompanySymbol") ON TABLE public."Companies" FROM feature141_reader;
REVOKE SELECT (
  "Id", "ProviderName", "ExternalCompanyId", "PeriodStart", "PeriodEnd", "ReportType", "OutputType",
  "IsAccepted", "RevisionStatus", "ExternalReportId"
) ON TABLE public."MonthlyReports" FROM feature141_reader;
REVOKE SELECT (
  "MonthlyReportId", "ProductCode", "Title", "Unit", "ProductionQuantity", "SalesQuantity", "SalesAmount",
  "SourceRowFingerprint", "SourceMultiplicity"
) ON TABLE public."MonthlyReportLineItems" FROM feature141_reader;
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
