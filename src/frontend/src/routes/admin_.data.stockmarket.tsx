import { createFileRoute } from "@tanstack/react-router";
import { DataManagementGuard } from "@/components/admin/data-management-guard";
import { StockMarketDbPage } from "@/components/admin/stock-market-db-page";

export const Route = createFileRoute("/admin_/data/stockmarket")({
  component: DataStockMarketRoute,
});

function DataStockMarketRoute() {
  return (
    <DataManagementGuard>
      {() => <StockMarketDbPage />}
    </DataManagementGuard>
  );
}
