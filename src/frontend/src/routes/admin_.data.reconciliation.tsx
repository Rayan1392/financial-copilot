import { createFileRoute } from "@tanstack/react-router";
import { DataManagementGuard } from "@/components/admin/data-management-guard";
import { ReconciliationPage } from "@/components/admin/reconciliation-page";

export const Route = createFileRoute("/admin_/data/reconciliation")({
  component: DataReconciliationRoute,
});

function DataReconciliationRoute() {
  return (
    <DataManagementGuard>
      {() => <ReconciliationPage />}
    </DataManagementGuard>
  );
}
