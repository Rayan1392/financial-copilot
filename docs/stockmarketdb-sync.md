# StockMarketDB

FinancialCopilot.Worker is the project to run. The StockMarketDbPollingWorker handles all incremental sync automatically.

To enable it, set Enabled: true in src/backend/FinancialCopilot.Worker/appsettings.json:

# How to sync stockmarketdb 
## Worker appsettings.json
"StockMarketDbPolling": {
  "Enabled": true,
  "IntradayTradeIntervalSeconds": 60,
  "IntradayIndexIntervalSeconds": 300,
  "DailyTradeIntervalSeconds": 3600,
  "InstrumentIntervalSeconds": 86400
}
Then run:

dotnet run --project src/backend/FinancialCopilot.Worker
Poll schedule (defaults):

## Dataset	| Interval
- IntradayTrades (tse.Trade)	| every 60s
- IntradayIndices (tse.IndexB1LastDay)	| every 5 min
- DailyTrades (Tse.TradeRefined) |	every 1 hour
- Instruments (Tse.Instrument)	| every 24 hours

The worker runs all four datasets concurrently in a single loop. It uses the watermark-based incremental cursor, so each poll only fetches rows newer than the last sync.