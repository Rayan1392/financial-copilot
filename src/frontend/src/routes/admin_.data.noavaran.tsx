import { createFileRoute } from "@tanstack/react-router";
import { DataManagementGuard } from "@/components/admin/data-management-guard";
import { NadpcoCurrentApiPage } from "@/components/admin/noavaran-current-api-page";

export const Route = createFileRoute("/admin_/data/noavaran")({
  component: DataNoadvaranRoute,
});

function DataNoadvaranRoute() {
  return (
    <DataManagementGuard>
      {() => <NadpcoCurrentApiPage />}
    </DataManagementGuard>
  );
}
