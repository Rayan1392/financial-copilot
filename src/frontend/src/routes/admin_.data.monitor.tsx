import { createFileRoute } from "@tanstack/react-router";
import { DataManagementGuard } from "@/components/admin/data-management-guard";
import { DataSyncMonitorPage } from "@/components/admin/data-sync-monitor-page";

export const Route = createFileRoute("/admin_/data/monitor")({
  component: DataMonitorRoute,
});

function DataMonitorRoute() {
  return (
    <DataManagementGuard>
      {() => <DataSyncMonitorPage />}
    </DataManagementGuard>
  );
}
