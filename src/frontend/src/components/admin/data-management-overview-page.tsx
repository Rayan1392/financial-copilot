import { Link } from "@tanstack/react-router";
import { Archive, BarChart2, Database, Activity, GitCompare, ArrowRight, AlertTriangle } from "lucide-react";

type SourceCard = {
  title: string;
  vendor: string;
  source: string;
  mode: string;
  description: string;
  to: string;
  icon: React.ComponentType<{ className?: string }>;
  badge?: { label: string; cls: string };
};

const sources: SourceCard[] = [
  {
    title: "Noavaran Amin Archive",
    vendor: "NoavaranAmin",
    source: "NoavaranArchiveSql",
    mode: "ArchiveOneTime",
    description: "Frozen CodalDB SQL snapshot. Import once, validate, and freeze. Never scheduled.",
    to: "/admin/data/archive",
    icon: Archive,
    badge: { label: "Archive", cls: "bg-blue-500/15 text-blue-600" },
  },
  {
    title: "Noavaran Current API",
    vendor: "NoavaranAmin",
    source: "NoavaranCurrentApi",
    mode: "CurrentIncremental",
    description: "Recurring incremental sync from Shamsi 1403 onward via NADPCO HTTP API.",
    to: "/admin/data/noavaran",
    icon: BarChart2,
    badge: { label: "Live", cls: "bg-emerald-500/15 text-emerald-600" },
  },
  {
    title: "CyclicalWaves API",
    vendor: "CyclicalWaves",
    source: "CyclicalWavesApi",
    mode: "ExternalSnapshot",
    description: "Independent fundamentals vendor. Periodic full snapshot sync.",
    to: "/admin/data/noavaran",
    icon: BarChart2,
    badge: { label: "Snapshot", cls: "bg-purple-500/15 text-purple-600" },
  },
  {
    title: "StockMarketDB Bridge",
    vendor: "Tsetmc",
    source: "StockMarketDb",
    mode: "MigrationBridge",
    description: "Market trading data via read-only StockMarketDB SQL Server bridge. Transitional — will be replaced by direct TSETMC feed.",
    to: "/admin/data/stockmarket",
    icon: Database,
    badge: { label: "Bridge", cls: "bg-amber-500/15 text-amber-600" },
  },
  {
    title: "TSETMC Direct Feed",
    vendor: "Tsetmc",
    source: "TsetmcWebService",
    mode: "CurrentIncremental",
    description: "Direct TSETMC ASMX web-service ingestion. Not yet operational — Phase 2 of spec 054.",
    to: "/admin/data/stockmarket",
    icon: Database,
    badge: { label: "Planned", cls: "bg-muted text-muted-foreground" },
  },
];

export function DataManagementOverviewPage() {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-bold">Data Operations</h1>
        <p className="text-sm text-muted-foreground mt-1">
          Manage archive imports, current API syncs, market data sources, and live monitoring.
        </p>
      </div>

      {/* Source cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-3 gap-4">
        {sources.map(({ title, vendor, source, mode, description, to, icon: Icon, badge }) => (
          <Link
            key={source}
            to={to}
            className="group rounded-xl border border-border bg-surface/60 p-4 hover:border-primary/40 hover:bg-primary/5 transition-colors flex flex-col gap-3"
          >
            <div className="flex items-start justify-between gap-2">
              <div className="rounded-lg bg-muted p-2">
                <Icon className="size-4 text-foreground" />
              </div>
              {badge && (
                <span className={`text-[10px] font-medium px-2 py-0.5 rounded-full ${badge.cls}`}>
                  {badge.label}
                </span>
              )}
            </div>
            <div className="flex-1">
              <h2 className="text-sm font-semibold group-hover:text-primary transition-colors">{title}</h2>
              <p className="text-xs text-muted-foreground mt-1 line-clamp-2">{description}</p>
            </div>
            <div className="text-xs text-muted-foreground space-y-0.5">
              <div><span className="font-mono">{vendor}</span> / <span className="font-mono">{source}</span></div>
              <div className="text-[10px] opacity-70 font-mono">{mode}</div>
            </div>
            <div className="flex items-center gap-1 text-xs text-primary opacity-0 group-hover:opacity-100 transition-opacity">
              Open <ArrowRight className="size-3" />
            </div>
          </Link>
        ))}
      </div>

      {/* Quick links */}
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <Link
          to="/admin/data/monitor"
          className="group rounded-xl border border-border bg-surface/60 p-4 hover:border-primary/40 hover:bg-primary/5 transition-colors flex items-center gap-4"
        >
          <div className="rounded-lg bg-emerald-500/10 p-3">
            <Activity className="size-5 text-emerald-600" />
          </div>
          <div className="flex-1">
            <h2 className="text-sm font-semibold group-hover:text-primary transition-colors">Live Monitor</h2>
            <p className="text-xs text-muted-foreground">Real-time sync activity stream across all providers.</p>
          </div>
          <ArrowRight className="size-4 text-muted-foreground group-hover:text-primary transition-colors" />
        </Link>

        <Link
          to="/admin/data/reconciliation"
          className="group rounded-xl border border-border bg-surface/60 p-4 hover:border-primary/40 hover:bg-primary/5 transition-colors flex items-center gap-4"
        >
          <div className="rounded-lg bg-purple-500/10 p-3">
            <GitCompare className="size-5 text-purple-600" />
          </div>
          <div className="flex-1">
            <h2 className="text-sm font-semibold group-hover:text-primary transition-colors">Reconciliation</h2>
            <p className="text-xs text-muted-foreground">Source coverage, conflicts, stale data, and missing periods.</p>
          </div>
          <ArrowRight className="size-4 text-muted-foreground group-hover:text-primary transition-colors" />
        </Link>
      </div>

      {/* Transitional notice */}
      <div className="rounded-xl border border-amber-500/20 bg-amber-500/5 p-4 flex gap-3">
        <AlertTriangle className="size-4 text-amber-500 shrink-0 mt-0.5" />
        <p className="text-xs text-amber-600">
          <strong>StockMarketDB</strong> is a <strong>MigrationBridge</strong> source. The roadmap
          moves to a direct TSETMC web-service feed (spec 054, Phase 2). Until then, StockMarketDB
          polling continues as the authoritative market-data source.
        </p>
      </div>
    </div>
  );
}
