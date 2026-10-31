import { createFileRoute } from "@tanstack/react-router";
import { DataManagementGuard } from "@/components/admin/data-management-guard";
import { CyclicalWavesV2Page } from "@/components/admin/cyclicalwaves-v2-page";

export const Route = createFileRoute("/admin_/data/cyclicalwaves-v2")({
  component: DataCyclicalWavesV2Route,
});

function DataCyclicalWavesV2Route() {
  return (
    <DataManagementGuard>
      {() => <CyclicalWavesV2Page />}
    </DataManagementGuard>
  );
}
