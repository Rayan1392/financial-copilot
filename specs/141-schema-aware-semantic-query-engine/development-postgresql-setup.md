# Feature 141 Development PostgreSQL Read Role

Status: preparation instructions only. No database role was created or changed in this work. Run this only in the designated Development database, by its owner/DBA, after the four-table/column scope and the Feature 141 catalog hash have been reviewed. Do not point this principal at Production or reuse the application connection string.

## Create a dedicated principal

Replace `<development_database>` with the exact Dev database name. Provision the password through the team's secret manager or an interactive `psql` `\\password feature141_reader` prompt; do not place it in source, shell history, appsettings, or command arguments.

```sql
-- See tools/SemanticCatalog/sql/feature141-read-role-setup.sql
```

Do not grant table-wide `SELECT`, sequence access, schema creation, role membership, write privileges, or access to other tables. The grants above cover the current direct-source-item compiler projection; any future compiler columns require a separate reviewed grant change.

## Verify the role before configuring the executor

Run `tools/SemanticCatalog/sql/feature141-read-role-verify.sql` as a database owner/DBA. Confirm the role has no memberships in either direction, is not a superuser, and can read only the listed columns through direct grants. Also verify that it has no table-level `SELECT` or write privileges inherited from `PUBLIC` or another role. The script has an exact Development database guard and displays RLS policies. The setup and verification scripts are preparation only; neither has been executed.

Connect with this role and verify `current_database()`, `current_user`, `current_setting('default_transaction_read_only')`, and `current_setting('search_path')`. In a disposable Development transaction, confirm `SET TRANSACTION READ ONLY` succeeds and a write attempt is rejected. Review the RLS flags and policies rather than assuming that column-level SELECT grants isolate companies. The application grant is bound to exact approved company UUIDs, and the sealed executor query filters on that UUID; if the required tenant/company authorization cannot be demonstrated for the intended scope, do not issue a grant. The executor independently confirms database/user identity and read-only transaction state, fixes transaction-local `search_path`, and applies a bounded statement timeout, cancellation, row/byte limits, and concurrency cap.

## Application-side authorization

After the role is separately reviewed, configure the following keys under `Feature141:QueryExecution` only in Development. Use API User Secrets or the approved secret manager for `ConnectionString`; configure remaining values in the same secure Development configuration source:

`Enabled`, `ExplicitDevelopmentOptIn`, `EnvironmentName`, `ConnectionString`, `DesignatedDatabase`, `ReadOnlyRole`, `CommandTimeoutSeconds`, `MaximumRows`, `MaximumResultBytes`, `MaximumConcurrency`, `DevelopmentScopeId`, `AuthorizationProvenance`, `ReviewedSourceCatalogIds`, `ReviewedRelationshipCatalogIds`, `ReviewedPhysicalObjectCatalogIds`, `ReviewedTenantCompanyBindings`, and `ReviewedOperations`.

Use exact `tenant-guid:company-guid` entries in `ReviewedTenantCompanyBindings` (a pair allowlist, not separate tenant and company lists, which would authorize their Cartesian product). The current target operation is only `DirectValueRetrieval`. Set the source, three relationship, and four physical-object IDs to the values in the review package. Do not copy the general `ConnectionStrings:FinancialCopilot` value. Startup validation rejects missing credentials, mismatched database/role names, production enablement, missing review scope, absent tenant/company pairs, unbounded settings, and unsupported operations. There is no fallback to the general application connection.

The compiler carries the resolved company UUID and operation into the sealed query. Both grant issuance and execution require that exact company and operation, in addition to tenant, catalog hash, source, relationship, and physical-object scope. The executor fixes transaction-local `search_path` to `pg_catalog, public`. This is application-side tenant scoping for the current shared-company schema; it does not substitute for database RLS. Confirm whether the Development schema has row-level policies and document the approved single-company test boundary before granting the role access.

Do not approve or modify canonical `queryAuthorization` fields as a side effect of configuring the Development test grant. The compiler preserves approval metadata from both the plan and canonical source/relationship entries, and the authorizer rejects a compiled query unless semantic review and query authorization are both approved. Current item-161 catalog metadata remains semantically pending and non-queryable, and the line-item/header relationship remains non-joinable until reviewed. The Development grant cannot override those states.

## Preflight and rollback

Run `python tools/SemanticCatalog/preflight_feature141.py` (or add `--json`) to validate the canonical catalog and hashes and report key-name presence plus known governance status. It never opens a database connection, reads configuration values or secret-store entries, or runs a financial query. It reports secure-store configuration as uninspected; the role grants remain unverified until the DBA runs the separate verification script. To revoke the prepared grants, use `tools/SemanticCatalog/sql/feature141-read-role-revoke.sql` after reviewing its target database guard. Remove Feature141 settings from the secure store and then independently inspect ACLs/memberships before any optional role drop.
