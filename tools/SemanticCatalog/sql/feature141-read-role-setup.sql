-- PREPARATION ONLY: DBA-reviewed, explicit Development provisioning.
-- Replace the database placeholder, inspect the target identity, then run manually as its owner.
\set ON_ERROR_STOP on
\set expected_dev_database 'REPLACE_WITH_EXACT_DEVELOPMENT_DATABASE'

SELECT current_database() = :'expected_dev_database' AS correct_dev_database \gset
\if :correct_dev_database
\else
  \echo 'Refusing: connected database does not equal expected Development database.'
  \quit 3
\endif

DO $provision$
DECLARE role_oid oid;
DECLARE memberships integer;
DECLARE memberships_granted integer;
DECLARE existing_role record;
BEGIN
  SELECT * INTO existing_role FROM pg_roles WHERE rolname = 'feature141_reader';
  IF NOT FOUND THEN
    EXECUTE 'CREATE ROLE feature141_reader LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS';
  ELSIF NOT existing_role.rolcanlogin OR existing_role.rolsuper OR existing_role.rolcreatedb
        OR existing_role.rolcreaterole OR existing_role.rolinherit OR existing_role.rolreplication
        OR existing_role.rolbypassrls THEN
    RAISE EXCEPTION 'feature141_reader exists with unsafe role attributes; inspect and review it manually';
  END IF;
  SELECT oid INTO role_oid FROM pg_roles WHERE rolname = 'feature141_reader';
  SELECT count(*) INTO memberships FROM pg_auth_members WHERE member = role_oid;
  SELECT count(*) INTO memberships_granted FROM pg_auth_members WHERE roleid = role_oid;
  IF memberships > 0 OR memberships_granted > 0 THEN
    RAISE EXCEPTION 'feature141_reader participates in role memberships; inspect and review it manually';
  END IF;
END
$provision$;

ALTER ROLE feature141_reader SET default_transaction_read_only = on;
DO $connect$
BEGIN
  EXECUTE format('GRANT CONNECT ON DATABASE %I TO feature141_reader', current_database());
END
$connect$;
GRANT USAGE ON SCHEMA public TO feature141_reader;

GRANT SELECT ("Id", "ProviderName", "StatementType", "SourceItemId", "Unit")
  ON TABLE public."FinancialStatementSourceItems" TO feature141_reader;
GRANT SELECT ("Value", "FinancialStatementId", "SourceItemCatalogId")
  ON TABLE public."FinancialStatementLineItems" TO feature141_reader;
GRANT SELECT (
  "Id", "CompanyId", "ProviderName", "StatementType", "PeriodEnd", "PeriodType",
  "IsComposing", "IsAudited", "IsRepresented", "ExternalStatementId", "PeriodStart",
  "SourcePayloadChecksum", "PublishedAt", "LastSynchronizedAt", "WarningsJson",
  "LogicalVendor", "SourceMode"
) ON TABLE public."FinancialStatements" TO feature141_reader;
GRANT SELECT ("Id") ON TABLE public."Companies" TO feature141_reader;

-- Set the password with an interactive `psql` \password feature141_reader prompt.
-- Never provide it in this file, source control, command-line arguments, or shell history.
