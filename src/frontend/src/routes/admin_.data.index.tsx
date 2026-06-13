import { createFileRoute } from "@tanstack/react-router";
import { DataManagementGuard } from "@/components/admin/data-management-guard";
import { DataManagementOverviewPage } from "@/components/admin/data-management-overview-page";

export const Route = createFileRoute("/admin_/data/")({
  component: DataOverviewRoute,
});

function DataOverviewRoute() {
  return (
    <DataManagementGuard>
      {() => <DataManagementOverviewPage />}
    </DataManagementGuard>
  );
}
