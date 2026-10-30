-- READ ONLY: emits the Feature 141 shared public company scope as docker-compose .env lines.
-- One tenant-level discovery scope over every eligible listed company (the NoavaranEligibleCompanies view, the
-- product's own definition of eligible companies). There are no per-user company lists.
--   psql -X -q -A -t -v expected_database=<db> -v tenant_id=<tenant guid> \
--        -v approval_reference=<owner approval reference> -f feature141-public-company-scope-env.sql >> .env
-- tenant_id: Authentication:OwnedIdentity:DefaultTenantId for web, Telegram and TahlilApp users.
-- approval_reference: supplied by the product owner; it must not contain '|'. Re-run after the eligible list changes,
-- replacing the previous block between its BEGIN/END markers.
-- docker-compose.yml maps FEATURE141_PUBLIC_SCOPE_* to Feature141:CompanyDiscoveryScopes:0; the Feature141__QueryExecution__*
-- lines reach the api container directly through env_file.
\set ON_ERROR_STOP on
\if :{?expected_database}
\else
  \echo 'Refusing: pass -v expected_database=<exact target database name>.'
  \quit 3
\endif
\if :{?tenant_id}
\else
  \echo 'Refusing: pass -v tenant_id=<tenant guid>.'
  \quit 3
\endif
\if :{?approval_reference}
\else
  \echo 'Refusing: pass -v approval_reference=<owner approval reference>.'
  \quit 3
\endif
SELECT current_database() = :'expected_database' AND :'tenant_id' ~ '^[0-9a-fA-F-]{36}$'
       AND position('|' IN :'approval_reference') = 0 AND length(btrim(:'approval_reference')) > 0 AS valid_input \gset
\if :valid_input
\else
  \echo 'Refusing: wrong database, malformed tenant_id, or an empty approval_reference / one containing |.'
  \quit 3
\endif

\set scope_id 'public-listed-companies'
BEGIN READ ONLY;
SELECT line FROM (
  SELECT 0 AS part, 0 AS ordinal, '# BEGIN Feature 141 public company scope (generated '
      || to_char(now(), 'YYYY-MM-DD') || '; replace this whole block when regenerating)' AS line
  UNION ALL SELECT 0, 1, 'FEATURE141_PUBLIC_SCOPE_TENANT_ID=' || :'tenant_id'
  UNION ALL SELECT 0, 2, 'FEATURE141_PUBLIC_SCOPE_ID=' || :'scope_id'
  UNION ALL SELECT 0, 3, 'FEATURE141_PUBLIC_SCOPE_APPROVAL_REFERENCE=' || :'approval_reference'
  UNION ALL SELECT 0, 4, 'Feature141__QueryExecution__ReviewedTenantCompanyDiscoveryScopes__0='
      || :'tenant_id' || '|' || :'scope_id' || '|' || :'approval_reference'
  UNION ALL
  SELECT 1, (row_number() OVER (ORDER BY "Id"))::int - 1,
         'Feature141__QueryExecution__ReviewedTenantCompanyBindings__'
             || ((row_number() OVER (ORDER BY "Id")) - 1) || '=' || :'tenant_id' || ':' || "Id"
  FROM public."NoavaranEligibleCompanies"
  UNION ALL SELECT 2, 0, '# END Feature 141 public company scope'
) lines ORDER BY part, ordinal;
ROLLBACK;
