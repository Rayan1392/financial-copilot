import { useState } from "react";
import { ChevronDown, ChevronRight, Cpu } from "lucide-react";
import type { AssistantChatBlock } from "@/lib/chat.functions";

interface OrchestrationDiagnosticsPanelProps {
  orchestration: NonNullable<AssistantChatBlock["orchestration"]>;
}

export function OrchestrationDiagnosticsPanel({
  orchestration,
}: OrchestrationDiagnosticsPanelProps) {
  const [expanded, setExpanded] = useState(false);

  return (
    <div className="mt-2 text-xs border border-dashed border-muted-foreground/30 rounded">
      <button
        onClick={() => setExpanded((v) => !v)}
        className="flex items-center gap-1 px-2 py-1 text-muted-foreground hover:text-foreground w-full text-left"
      >
        {expanded ? (
          <ChevronDown className="h-3 w-3" />
        ) : (
          <ChevronRight className="h-3 w-3" />
        )}
        <Cpu className="h-3 w-3" />
        <span>Orchestration diagnostics</span>
        {orchestration.providerFallbackOccurred && (
          <span className="ml-1 text-amber-500">(fallback)</span>
        )}
      </button>
      {expanded && (
        <div className="px-3 pb-2 space-y-1 text-muted-foreground font-mono">
          {orchestration.mode && (
            <div>
              <span className="text-foreground/60">mode: </span>
              {orchestration.mode}
            </div>
          )}
          {orchestration.workflowVersion && (
            <div>
              <span className="text-foreground/60">version: </span>
              {orchestration.workflowVersion}
            </div>
          )}
          {orchestration.providerSelection && (
            <div>
              <span className="text-foreground/60">provider: </span>
              {orchestration.providerSelection}
            </div>
          )}
          {orchestration.providerFallbackOccurred !== undefined && (
            <div>
              <span className="text-foreground/60">fallback: </span>
              {orchestration.providerFallbackOccurred ? "yes" : "no"}
            </div>
          )}
          {orchestration.correlationId && (
            <div className="truncate">
              <span className="text-foreground/60">correlation: </span>
              {orchestration.correlationId}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
