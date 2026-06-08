# StockMarketDB to Direct TSETMC Feed Migration

## User Story

As a data platform owner, I want trading statistics currently synchronized from `StockMarketDB` to have a planned migration path toward direct TSETMC web service ingestion so TahlilApp-AI eventually owns the market-data update pipeline and no longer depends on another database being refreshed by a separate service.

## Business Context

`StockMarketDB` is currently different from the Noavaran archive sources. It is not a frozen archive. It is being updated by another service using TSETMC ASMX web services.

The future target architecture is:

- Short term: continue reading market trading statistics from read-only `StockMarketDB`.
- Transition: compare StockMarketDB results against direct TSETMC ingestion.
- Final state: TahlilApp-AI directly updates trading statistics from TSETMC services and no longer needs StockMarketDB synchronization.

## Acceptance Criteria

1. StockMarketDB remains a separate datasource from Noavaran Amin and CyclicalWaves.
2. Current StockMarketDB synchronization remains valid as a bridge source.
3. A new `TsetmcWebService` physical source is introduced in the provider/source model.
4. Direct TSETMC ingestion is implemented behind provider abstractions, not by querying from the scanner path.
5. The system supports parallel validation where direct TSETMC data is compared with StockMarketDB-derived data.
6. The final migration can disable StockMarketDB polling without changing scanner query contracts.
7. Provenance distinguishes `StockMarketDb` bridge data from `TsetmcWebService` direct data.
8. Latest market quote projection can be fed by either source according to configured priority.
9. DataAdmin exposes both StockMarketDB bridge status and TSETMC direct-feed status.
10. Tests prove the scanner reads canonical market projections and is not coupled to StockMarketDB.

## Out of Scope

- Replacing Noavaran financial/fundamental data.
- Implementing portfolio valuation.
- Direct query-time calls to TSETMC from AI/chat endpoints.
