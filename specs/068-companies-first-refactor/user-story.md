# Spec 068 — Companies-First Symbol Resolution and ExternalCompanyId Unification

## Depends on
- `039-nadpco-api-company-catalog-sync` — Companies is already the NADPCO-backed catalog
- `067-cyclicalwaves-company-mapping` — CompanyResolverService and Companies.Ticker/EnTicker columns must exist before this spec ships

---
## User Story

As a platform owner,

I want Companies.ExternalCompanyId (NADPCO coID) to become the single canonical company identifier across the entire financial data platform,

so that all symbol resolution, financial statement lookup, monthly reports, derived metrics, feature snapshots, scanners, and AI queries operate through one consistent company key and the Symbols table can be completely removed.

---

## Background

Today the platform uses a mixed identification model:

* Companies (NADPCO catalog)
* Symbols (provider-specific symbol mapping)
* SymbolId references in DerivedMetrics and FeatureSnapshots
* ExternalCompanyId references in FinancialStatements and MonthlyReports

This creates multiple lookup paths and cross-provider fan-out logic.

The target architecture is:

User Symbol
→ CompanyResolverService
→ Companies
→ ExternalCompanyId (NADPCO coID)
→ FinancialStatements
→ MonthlyReports
→ DerivedMetrics
→ FeatureSnapshots

Symbols becomes obsolete and must be removed.

Data migration is explicitly out of scope because all affected data can be re-ingested from source providers.

---

# Acceptance Criteria

## A. Company Resolution

1. EfCoreSymbolNameResolver is removed.

2. CompanyResolverService becomes the only symbol-to-company resolution component.

3. All symbol resolution queries must use Companies exclusively.

4. Resolution order:

   * CompanySymbol
   * TseSymbol
   * EnTicker
   * IsinCode
   * InsCode
   * Normalized Name

5. No code path may query Symbols.SymbolCode.

6. No code path may query Symbols for company resolution.

7. Resolver must return exactly one company.

8. Multiple matches must generate an AmbiguousCompanyMatch warning and return null.

9. Unresolved symbols must be reported through existing UnresolvedSymbols behavior.

---

## B. Canonical Company Key

10. ExternalCompanyId becomes the single canonical company identifier.

11. SymbolId must not exist in any financial domain table.

12. All metric lookups must use ExternalCompanyId.

13. All financial item lookups must use ExternalCompanyId.

14. All scanner executions must use ExternalCompanyId.

15. All AI financial retrieval paths must use ExternalCompanyId.

---

## C. DerivedMetrics Refactor

16. Remove SymbolId.

17. Add ExternalCompanyId.

18. ExternalCompanyId must be non-null.

19. Unique index becomes:

(ExternalCompanyId,
MetricCode,
MetricVersion,
CalculationPolicyVersion,
PeriodEnd)

20. Metric lookup service must query only by ExternalCompanyId.

21. No Symbols join is allowed.

22. No CompanyId fan-out logic is allowed.

---

## D. FeatureSnapshots Refactor

23. Remove SymbolId.

24. Add ExternalCompanyId.

25. Update all indexes to use ExternalCompanyId.

26. No Symbols references remain.

---

## E. FinancialStatements and MonthlyReports

27. FinancialStatements continue using ExternalCompanyId.

28. MonthlyReports continue using ExternalCompanyId.

29. ExternalCompanyId must always represent NADPCO coID.

30. Provider-specific identifiers must never be stored in ExternalCompanyId.

---

## F. CyclicalWaves Refactor

31. CyclicalWaves normalizers must resolve companies through CompanyResolverService.

32. Incoming Persian ticker must resolve to NADPCO coID before persistence.

33. Persist ExternalCompanyId into FinancialStatements.

34. Persist ExternalCompanyId into MonthlyReports.

35. Structured warning logs must be generated for unresolved companies.

36. No CyclicalWaves component may write to Companies.

37. No CyclicalWaves component may write to Symbols.

---

## G. CodalDB Refactor

38. CodalDB normalizers must resolve companies through CompanyResolverService.

39. CodalDB must stop writing Symbols records.

40. CodalDB financial data must be linked using ExternalCompanyId.

41. No CodalDB component may write to Companies.

42. No CodalDB component may write to Symbols.

---

## H. Companies Ownership

43. NadpcoApiCompanyNormalizer is the only component allowed to insert Companies rows.

44. NadpcoApiCompanyNormalizer is the only component allowed to update Companies rows.

45. All other providers are read-only consumers of Companies.

46. Architecture tests must enforce this rule.

---

## I. Symbols Removal

47. Remove all foreign keys referencing Symbols.

48. Remove all indexes referencing Symbols.

49. Drop Symbols table.

50. Remove all repositories and services dedicated to Symbols.

51. Remove all DI registrations related to Symbols.

52. No runtime dependency on Symbols remains.

---

## J. Architecture Safety Verification

53. Perform solution-wide scan for SymbolId usage.

54. Perform solution-wide scan for Symbols table usage.

55. Perform solution-wide scan for Symbols repository usage.

56. Perform solution-wide scan for Symbols joins.

57. Perform solution-wide scan for ExternalCompanyId consumers.

58. Verify scanners continue functioning.

59. Verify AI query endpoints continue functioning.

60. Verify metric retrieval continues functioning.

61. Verify financial statement retrieval continues functioning.

62. Verify monthly report retrieval continues functioning.

63. Verify no table references Symbols.Id after migration.

---

## K. Data Loss Assumption

64. Data migration is not required.

65. Existing data may be deleted.

66. Services will be re-run after deployment.

67. Database will be repopulated from source systems.

68. No backward compatibility layer is required.

---

# Tasks

## Phase 1 — Schema Refactor

* Remove SymbolId from DerivedMetrics.
* Add ExternalCompanyId to DerivedMetrics.
* Remove SymbolId from FeatureSnapshots.
* Add ExternalCompanyId to FeatureSnapshots.
* Update indexes.
* Remove foreign keys.
* Drop Symbols table.
* Create EF migration.

## Phase 2 — Resolution Layer Refactor

* Remove EfCoreSymbolNameResolver.
* Register CompanyResolverService.
* Refactor all lookup paths.
* Remove Symbols-based queries.

## Phase 3 — Derived Metrics Refactor

* Persist ExternalCompanyId.
* Remove SymbolId logic.
* Remove fan-out logic.

## Phase 4 — CyclicalWaves Refactor

* Resolve company through CompanyResolverService.
* Store NADPCO coID.
* Stop writing Companies.
* Stop writing Symbols.

## Phase 5 — CodalDB Refactor

* Resolve company through CompanyResolverService.
* Stop writing Companies.
* Stop writing Symbols.

## Phase 6 — Architecture Verification

* Scan solution for SymbolId usage.
* Scan solution for Symbols usage.
* Scan solution for remaining joins.
* Fix remaining references.

## Phase 7 — Tests

* Unit tests for CompanyResolverService.
* Unit tests for metric lookup.
* Unit tests for normalizers.
* Integration tests for AI endpoints.
* Architecture tests for Companies ownership.
* Architecture tests for Symbols removal.
* Regression tests for scanner execution.
* Regression tests for AI financial queries.
