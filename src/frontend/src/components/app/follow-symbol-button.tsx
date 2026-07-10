import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useServerFn } from "@tanstack/react-start";
import { followSymbolByExternalId } from "@/lib/followed-symbols.functions";
import { searchSymbolMetadata } from "@/lib/metadata.functions";

type FollowSymbolButtonProps = {
  symbol?: string;
  externalCompanyId?: string;
  compact?: boolean;
};

export function FollowSymbolButton({ symbol, externalCompanyId, compact = false }: FollowSymbolButtonProps) {
  const qc = useQueryClient();
  const followById = useServerFn(followSymbolByExternalId);
  const searchSymbols = useServerFn(searchSymbolMetadata);
  const follow = useMutation({
    mutationFn: async () => {
      const resolvedId = externalCompanyId ?? (await resolveExternalCompanyId());
      if (!resolvedId) throw new Error("Symbol could not be resolved.");
      return followById({ data: { externalCompanyId: resolvedId } });
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["followed-symbols"] });
    },
  });

  async function resolveExternalCompanyId() {
    if (!symbol) return null;
    const matches = await searchSymbols({ data: { search: symbol, limit: 5 } });
    const exact = matches.find((item) => item.symbolCode.localeCompare(symbol, undefined, { sensitivity: "accent" }) === 0);
    return (exact ?? matches[0])?.externalCompanyId ?? null;
  }

  if (!symbol && !externalCompanyId) return null;

  return (
    <button
      type="button"
      onClick={(event) => {
        event.preventDefault();
        follow.mutate();
      }}
      disabled={follow.isPending}
      className={
        compact
          ? "rounded-full border border-emerald/30 px-2 py-0.5 text-[10px] text-emerald hover:bg-emerald/10 disabled:opacity-50"
          : "rounded-lg border border-emerald/30 px-3 py-1.5 text-xs font-medium text-emerald hover:bg-emerald/10 disabled:opacity-50"
      }
    >
      {follow.isSuccess ? "Followed" : follow.isPending ? "Following..." : "Follow symbol"}
    </button>
  );
}
