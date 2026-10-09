-- Read-only privilege audit. Run as DBA in the designated Development database.
\set ON_ERROR_STOP on
\set expected_dev_database 'REPLACE_WITH_EXACT_DEVELOPMENT_DATABASE'
SELECT current_database() = :'expected_dev_database' AS correct_dev_database \gset
\if :correct_dev_database
\else
  \echo 'Refusing: connected database does not equal expected Development database.'
  \quit 3
\endif

SELECT current_database() AS connected_database, current_user AS verifier;
SELECT rolname, rolcanlogin, rolsuper, rolcreatedb, rolcreaterole, rolinherit,
       rolreplication, rolbypassrls, rolconfig
FROM pg_roles WHERE rolname = 'feature141_reader';

SELECT granted.rolname AS granted_role, member.rolname AS member_role
FROM pg_auth_members m
JOIN pg_roles granted ON granted.oid = m.roleid
JOIN pg_roles member ON member.oid = m.member
WHERE granted.rolname = 'feature141_reader' OR member.rolname = 'feature141_reader';

WITH required(table_name, column_name) AS (VALUES
  ('FinancialStatementSourceItems','Id'), ('FinancialStatementSourceItems','ProviderName'),
  ('FinancialStatementSourceItems','StatementType'), ('FinancialStatementSourceItems','SourceItemId'),
  ('FinancialStatementSourceItems','Unit'),
  ('FinancialStatementLineItems','Value'), ('FinancialStatementLineItems','FinancialStatementId'),
  ('FinancialStatementLineItems','SourceItemCatalogId'),
  ('FinancialStatements','Id'), ('FinancialStatements','CompanyId'),
  ('FinancialStatements','ProviderName'), ('FinancialStatements','StatementType'),
  ('FinancialStatements','PeriodEnd'), ('FinancialStatements','PeriodType'),
  ('FinancialStatements','IsComposing'), ('FinancialStatements','IsAudited'),
  ('FinancialStatements','IsRepresented'), ('FinancialStatements','ExternalStatementId'),
  ('FinancialStatements','PeriodStart'), ('FinancialStatements','SourcePayloadChecksum'),
  ('FinancialStatements','PublishedAt'), ('FinancialStatements','LastSynchronizedAt'),
  ('FinancialStatements','WarningsJson'), ('FinancialStatements','LogicalVendor'),
  ('FinancialStatements','SourceMode'), ('Companies','Id')
)
SELECT table_name, column_name,
       has_column_privilege('feature141_reader', format('public.%I', table_name), column_name, 'SELECT') AS select_allowed
FROM required ORDER BY table_name, column_name;

-- Complete effective SELECT inventory: only the reviewed rows above should be true.
WITH required(table_name, column_name) AS (VALUES
  ('FinancialStatementSourceItems','Id'), ('FinancialStatementSourceItems','ProviderName'),
  ('FinancialStatementSourceItems','StatementType'), ('FinancialStatementSourceItems','SourceItemId'),
  ('FinancialStatementSourceItems','Unit'),
  ('FinancialStatementLineItems','Value'), ('FinancialStatementLineItems','FinancialStatementId'),
  ('FinancialStatementLineItems','SourceItemCatalogId'),
  ('FinancialStatements','Id'), ('FinancialStatements','CompanyId'),
  ('FinancialStatements','ProviderName'), ('FinancialStatements','StatementType'),
  ('FinancialStatements','PeriodEnd'), ('FinancialStatements','PeriodType'),
  ('FinancialStatements','IsComposing'), ('FinancialStatements','IsAudited'),
  ('FinancialStatements','IsRepresented'), ('FinancialStatements','ExternalStatementId'),
  ('FinancialStatements','PeriodStart'), ('FinancialStatements','SourcePayloadChecksum'),
  ('FinancialStatements','PublishedAt'), ('FinancialStatements','LastSynchronizedAt'),
  ('FinancialStatements','WarningsJson'), ('FinancialStatements','LogicalVendor'),
  ('FinancialStatements','SourceMode'), ('Companies','Id')
)
SELECT c.relname AS table_name, a.attname AS column_name,
       (r.column_name IS NOT NULL) AS reviewed_column,
       has_column_privilege('feature141_reader', c.oid, a.attnum, 'SELECT') AS select_allowed
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
LEFT JOIN required r ON r.table_name = c.relname AND r.column_name = a.attname
WHERE n.nspname = 'public'
  AND c.relname IN ('FinancialStatementSourceItems','FinancialStatementLineItems','FinancialStatements','Companies')
ORDER BY c.relname, a.attnum;

-- Effective write/schema/database capability checks must all be false except CONNECT/USAGE.
SELECT has_database_privilege('feature141_reader', current_database(), 'CONNECT') AS connect_allowed,
       has_schema_privilege('feature141_reader', 'public', 'USAGE') AS public_usage_allowed,
       has_schema_privilege('feature141_reader', 'public', 'CREATE') AS public_create_allowed,
       has_database_privilege('feature141_reader', current_database(), 'CREATE') AS database_create_allowed,
       has_database_privilege('feature141_reader', current_database(), 'TEMP') AS database_temp_allowed;
SELECT table_name,
       has_table_privilege('feature141_reader', format('public.%I', table_name), 'INSERT') AS insert_allowed,
       has_table_privilege('feature141_reader', format('public.%I', table_name), 'UPDATE') AS update_allowed,
       has_table_privilege('feature141_reader', format('public.%I', table_name), 'DELETE') AS delete_allowed,
       has_table_privilege('feature141_reader', format('public.%I', table_name), 'TRUNCATE') AS truncate_allowed,
       has_table_privilege('feature141_reader', format('public.%I', table_name), 'REFERENCES') AS references_allowed,
       has_table_privilege('feature141_reader', format('public.%I', table_name), 'TRIGGER') AS trigger_allowed
FROM (VALUES ('FinancialStatementSourceItems'), ('FinancialStatementLineItems'),
             ('FinancialStatements'), ('Companies')) AS t(table_name);

-- Detect table-wide SELECT ACL entries granted directly to reader or PUBLIC.
SELECT n.nspname, c.relname, grantee.rolname AS grantee, x.privilege_type
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
CROSS JOIN LATERAL aclexplode(COALESCE(c.relacl, acldefault('r', c.relowner))) x
LEFT JOIN pg_roles grantee ON grantee.oid = x.grantee
WHERE n.nspname = 'public'
  AND c.relname IN ('FinancialStatementSourceItems','FinancialStatementLineItems','FinancialStatements','Companies')
  AND x.privilege_type = 'SELECT'
  AND (x.grantee = 0 OR grantee.rolname = 'feature141_reader');

-- RLS must be explicitly reviewed; no SELECT policy means a policy based on another identity may deny data.
SELECT c.relname, c.relrowsecurity, c.relforcerowsecurity
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public'
  AND c.relname IN ('FinancialStatementSourceItems','FinancialStatementLineItems','FinancialStatements','Companies');
SELECT schemaname, tablename, policyname, roles, cmd, qual, with_check
FROM pg_policies WHERE schemaname = 'public'
  AND tablename IN ('FinancialStatementSourceItems','FinancialStatementLineItems','FinancialStatements','Companies');

-- Separately connect AS feature141_reader and run this identity/read-only probe:
-- BEGIN READ ONLY;
-- SELECT current_database(), current_user, current_setting('default_transaction_read_only'), current_setting('search_path');
-- ROLLBACK;
