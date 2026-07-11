import { createFileRoute } from "@tanstack/react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useServerFn } from "@tanstack/react-start";
import { useState } from "react";
import { Bot, ChevronLeft, ChevronRight, ExternalLink, Eye, X } from "lucide-react";
import {
  dismissInsight,
  explainInsight,
  getFollowedSymbols,
  getFollowedSymbolInsights,
  markInsightSeen,
  unfollowSymbolByExternalId,
  type FollowedSymbolInsightFeedItem,
} from "@/lib/followed-symbols.functions";
import { searchSymbolMetadata, type SymbolMetadata } from "@/lib/metadata.functions";
import { FollowSymbolButton } from "@/components/app/follow-symbol-button";

export const Route = createFileRoute("/_app/followed-symbols")({
  component: FollowedSymbolsPage,
});

function FollowedSymbolsPage() {
  const qc = useQueryClient();
  const fetchFollowed = useServerFn(getFollowedSymbols);
  const fetchInsights = useServerFn(getFollowedSymbolInsights);
  const searchSymbols = useServerFn(searchSymbolMetadata);
  const unfollow = useServerFn(unfollowSymbolByExternalId);
  const markSeen = useServerFn(markInsightSeen);
  const dismiss = useServerFn(dismissInsight);
  const explain = useServerFn(explainInsight);
  const [search, setSearch] = useState("");
  const [typeFilter, setTypeFilter] = useState("");
  const [severityFilter, setSeverityFilter] = useState("");
  const [includeDismissed, setIncludeDismissed] = useState(false);
  const [skip, setSkip] = useState(0);
  const [explanations, setExplanations] = useState<Record<string, string>>({});
  const take = 10;
  const followed = useQuery({
    queryKey: ["followed-symbols"],
    queryFn: () => fetchFollowed(),
    retry: false,
    throwOnError: false,
    refetchOnWindowFocus: false,
  });
  const insights = useQuery({
    queryKey: ["followed-symbol-insights", typeFilter, severityFilter, includeDismissed, skip],
    queryFn: () =>
      fetchInsights({
        data: {
          type: typeFilter || undefined,
          severity: severityFilter || undefined,
          includeDismissed,
          skip,
          take,
        },
      }),
    retry: false,
    throwOnError: false,
    refetchOnWindowFocus: false,
  });
  const searchQuery = useQuery({
    queryKey: ["followed-symbol-search", search],
    queryFn: () => searchSymbols({ data: { search, limit: 12 } }),
    enabled: search.trim().length > 0,
    retry: false,
    throwOnError: false,
    refetchOnWindowFocus: false,
  });
  const remove = useMutation({
    mutationFn: (externalCompanyId: string) => unfollow({ data: { externalCompanyId } }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["followed-symbols"] }),
  });
  const mark = useMutation({
    mutationFn: (insightEventId: string) => markSeen({ data: { insightEventId } }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["followed-symbol-insights"] }),
  });
  const hide = useMutation({
    mutationFn: (insightEventId: string) => dismiss({ data: { insightEventId } }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["followed-symbol-insights"] }),
  });
  const ask = useMutation({
    mutationFn: (insightEventId: string) => explain({ data: { insightEventId } }),
    onSuccess: (result, insightEventId) =>
      setExplanations((current) => ({ ...current, [insightEventId]: result.text })),
  });
  const followedIds = new Set(followed.data?.symbols.map((item) => item.externalCompanyId) ?? []);

  return (
    <div className="flex-1 overflow-y-auto p-8">
      <div className="mx-auto max-w-4xl space-y-8">
        <header className="space-y-3">
          <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald">
            Followed symbols
          </p>
          <h1 className="text-3xl font-bold text-foreground">Manage followed symbols</h1>
          <p className="max-w-2xl text-sm text-muted-foreground">
            Followed symbols are a personal attention list for future AI feeds. They are not
            portfolio holdings and do not imply position size, cost basis, or exposure.
          </p>
        </header>

        <section className="rounded-2xl border border-hairline bg-surface/60 p-5">
          <div className="mb-5 flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.2em] text-emerald">
                Intelligence feed
              </p>
              <h2 className="mt-1 text-xl font-semibold">Followed-symbol events</h2>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <select
                value={typeFilter}
                onChange={(event) => {
                  setTypeFilter(event.target.value);
                  setSkip(0);
                }}
                className="rounded-lg border border-hairline bg-background px-3 py-2 text-xs text-foreground outline-none focus:border-emerald/50"
              >
                <option value="">All types</option>
                <option value="MonthlyReportPublished">Monthly reports</option>
                <option value="MonthlySalesAnomaly">Sales anomalies</option>
                <option value="MonthlyQualityRankingChange">Quality changes</option>
                <option value="PriceMovement">Price movement</option>
                <option value="ComprehensiveAnalysisPublished">Analysis posts</option>
                <option value="FinancialStatementPublished">Financial statements</option>
                <option value="DataFreshnessWarning">Freshness warnings</option>
              </select>
              <select
                value={severityFilter}
                onChange={(event) => {
                  setSeverityFilter(event.target.value);
                  setSkip(0);
                }}
                className="rounded-lg border border-hairline bg-background px-3 py-2 text-xs text-foreground outline-none focus:border-emerald/50"
              >
                <option value="">All severities</option>
                <option value="Critical">Critical</option>
                <option value="Important">Important</option>
                <option value="Notice">Notice</option>
                <option value="Informational">Informational</option>
              </select>
              <label className="flex items-center gap-2 rounded-lg border border-hairline bg-background px-3 py-2 text-xs text-muted-foreground">
                <input
                  type="checkbox"
                  checked={includeDismissed}
                  onChange={(event) => {
                    setIncludeDismissed(event.target.checked);
                    setSkip(0);
                  }}
                  className="accent-emerald"
                />
                Dismissed
              </label>
            </div>
          </div>

          {insights.isLoading && <p className="text-sm text-muted-foreground">Loading insights...</p>}
          {insights.isError && <p className="text-sm text-rose">Could not load personalized insights.</p>}
          {!insights.isLoading && !insights.isError && insights.data?.emptyState && (
            <div className="rounded-lg border border-hairline bg-background/50 p-4">
              <p className="text-sm text-muted-foreground">{insights.data.emptyState.message}</p>
            </div>
          )}
          <div className="grid gap-3">
            {insights.data?.items.map((item) => (
              <InsightCard
                key={item.insight.id}
                item={item}
                explanation={explanations[item.insight.id]}
                onSeen={() => mark.mutate(item.insight.id)}
                onDismiss={() => hide.mutate(item.insight.id)}
                onAsk={() => ask.mutate(item.insight.id)}
                busy={mark.isPending || hide.isPending || ask.isPending}
              />
            ))}
          </div>
          {!insights.isLoading && !insights.isError && (insights.data?.totalCount ?? 0) > 0 && (
            <div className="mt-4 flex items-center justify-between">
              <span className="text-xs text-muted-foreground">
                {skip + 1}-{Math.min(skip + take, insights.data?.totalCount ?? 0)} of{" "}
                {insights.data?.totalCount ?? 0}
              </span>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => setSkip(Math.max(0, skip - take))}
                  disabled={skip === 0}
                  className="rounded-lg border border-hairline p-2 text-muted-foreground hover:text-foreground disabled:opacity-40"
                  aria-label="Previous insights"
                >
                  <ChevronLeft className="size-4" />
                </button>
                <button
                  type="button"
                  onClick={() => setSkip(skip + take)}
                  disabled={skip + take >= (insights.data?.totalCount ?? 0)}
                  className="rounded-lg border border-hairline p-2 text-muted-foreground hover:text-foreground disabled:opacity-40"
                  aria-label="Next insights"
                >
                  <ChevronRight className="size-4" />
                </button>
              </div>
            </div>
          )}
        </section>

        <section className="rounded-2xl border border-hairline bg-surface/60 p-5">
          <label className="text-xs font-medium text-muted-foreground" htmlFor="symbol-search">
            Add a symbol by company search
          </label>
          <input
            id="symbol-search"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Search symbol or company name"
            className="mt-2 w-full rounded-xl border border-hairline bg-background px-4 py-3 text-sm outline-none focus:border-emerald/50"
          />
          <div className="mt-4 grid gap-2 md:grid-cols-2">
            {searchQuery.data?.map((item) => (
              <SearchResult
                key={item.externalCompanyId}
                item={item}
                followed={followedIds.has(item.externalCompanyId)}
              />
            ))}
          </div>
        </section>

        <section className="rounded-2xl border border-hairline bg-surface/60 p-5">
          <div className="mb-4 flex items-center justify-between">
            <h2 className="text-lg font-semibold">My followed symbols</h2>
            <span className="text-xs text-muted-foreground">
              {followed.data?.symbols.length ?? 0} symbols
            </span>
          </div>
          {followed.isLoading && <p className="text-sm text-muted-foreground">Loading followed symbols...</p>}
          {followed.isError && <p className="text-sm text-rose">Could not load followed symbols.</p>}
          {!followed.isLoading && !followed.isError && followed.data?.symbols.length === 0 && (
            <p className="text-sm text-muted-foreground">No followed symbols yet.</p>
          )}
          <div className="grid gap-3">
            {followed.data?.symbols.map((item) => (
              <div
                key={item.externalCompanyId}
                className="flex items-center justify-between rounded-xl border border-hairline bg-background/50 px-4 py-3"
              >
                <div>
                  <div className="font-semibold text-foreground">{item.symbol}</div>
                  <div className="text-xs text-muted-foreground">{item.companyName}</div>
                  <div className="mt-1 text-[10px] text-muted-foreground">
                    External company id: {item.externalCompanyId}
                  </div>
                </div>
                <button
                  type="button"
                  onClick={() => remove.mutate(item.externalCompanyId)}
                  disabled={remove.isPending}
                  className="rounded-lg border border-rose/30 px-3 py-1.5 text-xs text-rose hover:bg-rose/10 disabled:opacity-50"
                >
                  Unfollow
                </button>
              </div>
            ))}
          </div>
        </section>
      </div>
    </div>
  );
}

function SearchResult({ item, followed }: { item: SymbolMetadata; followed: boolean }) {
  return (
    <div className="flex items-center justify-between rounded-xl border border-hairline bg-background/50 px-4 py-3">
      <div>
        <div className="font-semibold text-foreground">{item.symbolCode}</div>
        <div className="text-xs text-muted-foreground">{item.companyName}</div>
      </div>
      {followed ? (
        <span className="rounded-full border border-emerald/30 px-2 py-1 text-xs text-emerald">
          Followed
        </span>
      ) : (
        <FollowSymbolButton
          externalCompanyId={item.externalCompanyId}
          symbol={item.symbolCode}
          compact
        />
      )}
    </div>
  );
}

function InsightCard({
  item,
  explanation,
  onSeen,
  onDismiss,
  onAsk,
  busy,
}: {
  item: FollowedSymbolInsightFeedItem;
  explanation?: string;
  onSeen: () => void;
  onDismiss: () => void;
  onAsk: () => void;
  busy: boolean;
}) {
  const insight = item.insight;
  return (
    <article className="rounded-lg border border-hairline bg-background/50 p-4">
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <span className="rounded-md border border-emerald/30 px-2 py-1 text-xs text-emerald">
              {insight.symbol}
            </span>
            <span className="rounded-md border border-hairline px-2 py-1 text-xs text-muted-foreground">
              {insight.insightType}
            </span>
            <span className={severityClass(insight.severity)}>{insight.severity}</span>
            {item.dismissed && (
              <span className="rounded-md border border-rose/30 px-2 py-1 text-xs text-rose">
                Dismissed
              </span>
            )}
            {item.seen && !item.dismissed && (
              <span className="rounded-md border border-hairline px-2 py-1 text-xs text-muted-foreground">
                Seen
              </span>
            )}
          </div>
          <h3 className="mt-3 text-base font-semibold text-foreground">{insight.title}</h3>
          <p className="mt-2 text-sm leading-6 text-muted-foreground">{insight.summary}</p>
          <div className="mt-3 grid gap-2 text-xs text-muted-foreground md:grid-cols-3">
            <span>Importance {formatScore(insight.importanceScore)}</span>
            <span>Confidence {formatScore(insight.confidenceScore)}</span>
            <span>{new Date(insight.detectedAtUtc).toLocaleDateString()}</span>
          </div>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <button
            type="button"
            onClick={onSeen}
            disabled={busy || item.seen}
            className="rounded-lg border border-hairline p-2 text-muted-foreground hover:text-foreground disabled:opacity-40"
            aria-label="Mark seen"
            title="Mark seen"
          >
            <Eye className="size-4" />
          </button>
          <button
            type="button"
            onClick={onAsk}
            disabled={busy}
            className="rounded-lg border border-hairline p-2 text-muted-foreground hover:text-foreground disabled:opacity-40"
            aria-label="Ask AI"
            title="Ask AI"
          >
            <Bot className="size-4" />
          </button>
          <button
            type="button"
            disabled={!insight.sourceEntityId}
            className="rounded-lg border border-hairline p-2 text-muted-foreground hover:text-foreground disabled:opacity-40"
            aria-label="Open source"
            title="Open source"
          >
            <ExternalLink className="size-4" />
          </button>
          <button
            type="button"
            onClick={onDismiss}
            disabled={busy || item.dismissed}
            className="rounded-lg border border-rose/30 p-2 text-rose hover:bg-rose/10 disabled:opacity-40"
            aria-label="Dismiss"
            title="Dismiss"
          >
            <X className="size-4" />
          </button>
        </div>
      </div>
      <div className="mt-4 grid gap-2 md:grid-cols-2">
        {insight.evidence.slice(0, 4).map((evidence) => (
          <div key={`${evidence.label}-${evidence.value}`} className="rounded-md border border-hairline px-3 py-2">
            <div className="text-xs text-muted-foreground">{evidence.label}</div>
            <div className="mt-1 text-sm font-medium text-foreground">{evidence.value}</div>
            <div className="mt-1 text-[11px] text-muted-foreground">
              {evidence.sourceProvider}
              {evidence.sourcePeriod ? ` | ${evidence.sourcePeriod}` : ""}
            </div>
          </div>
        ))}
      </div>
      {explanation && (
        <div className="mt-4 rounded-md border border-emerald/20 bg-emerald/5 p-3 text-sm leading-6 text-foreground whitespace-pre-wrap">
          {explanation}
        </div>
      )}
      <div className="mt-4 flex flex-wrap gap-2 text-xs text-muted-foreground">
        <span>Source {insight.sourceProviderName}</span>
        <span>Entity {insight.sourceEntityType}</span>
        {insight.sourcePeriod && <span>Period {insight.sourcePeriod}</span>}
      </div>
    </article>
  );
}

function severityClass(severity: string) {
  const base = "rounded-md border px-2 py-1 text-xs";
  if (severity === "Critical") return `${base} border-rose/30 text-rose`;
  if (severity === "Important") return `${base} border-amber-400/30 text-amber-300`;
  if (severity === "Notice") return `${base} border-sky-400/30 text-sky-300`;
  return `${base} border-hairline text-muted-foreground`;
}

function formatScore(score: number) {
  return Number.isInteger(score) ? score.toString() : score.toFixed(1);
}
