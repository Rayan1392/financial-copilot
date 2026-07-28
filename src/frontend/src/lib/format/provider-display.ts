const NOAVARAN_AMIN_LABEL = "نوآوران امین";

const SOURCE_LABEL_MAP: Record<string, string> = {
  NoavaranCurrentApi: NOAVARAN_AMIN_LABEL,
  NADPCO: NOAVARAN_AMIN_LABEL,
  NadpcoApi: NOAVARAN_AMIN_LABEL,
  NoavaranAmin: NOAVARAN_AMIN_LABEL,
  NoavaranArchive: NOAVARAN_AMIN_LABEL,
  NoavaranArchiveSql: NOAVARAN_AMIN_LABEL,
  LatestDailyFallback: "آمار معاملات روزانه",
  LIVE: "آمار معاملات لحظه‌ای",
};

const SOURCE_NAME_PATTERN = new RegExp(
  `\\b(${Object.keys(SOURCE_LABEL_MAP)
    .sort((left, right) => right.length - left.length)
    .map(escapeRegExp)
    .join("|")})\\b`,
  "g",
);

export function formatProviderDisplayName(providerName?: string | null): string {
  if (!providerName) return "";
  return SOURCE_LABEL_MAP[providerName] ?? providerName;
}

export function replaceProviderDisplayNames(text?: string | null): string {
  if (!text) return "";
  return text.replace(SOURCE_NAME_PATTERN, (match) => formatProviderDisplayName(match));
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
