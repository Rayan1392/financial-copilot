import { createFileRoute } from "@tanstack/react-router";
import { DataManagementGuard } from "@/components/admin/data-management-guard";
import { DataManagementOverviewPage } from "@/components/admin/data-management-overview-page";

export const Route = createFileRoute("/admin_/data-management/fund-reports")({
  component: FundReportsRoute,
});

function FundReportsRoute() {
  return (
    <DataManagementGuard>
      {() => <DataManagementOverviewPage />}
    </DataManagementGuard>
  );
}
