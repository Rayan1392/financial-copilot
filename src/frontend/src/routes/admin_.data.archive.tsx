import { createFileRoute } from "@tanstack/react-router";
import { DataManagementGuard } from "@/components/admin/data-management-guard";
import { ArchiveImportPage } from "@/components/admin/archive-import-page";

export const Route = createFileRoute("/admin_/data/archive")({
  component: DataArchiveRoute,
});

function DataArchiveRoute() {
  return (
    <DataManagementGuard>
      {() => <ArchiveImportPage />}
    </DataManagementGuard>
  );
}
