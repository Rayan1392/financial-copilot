/**
 * The one owner of the phone workspace's icons (Feature 153 change mobile-ui-refresh). Every `/m` file takes its
 * icons from here under a semantic name, so the icon set can be replaced in this file alone. ESLint forbids
 * importing an icon library directly in `components/mobile/**` and `routes/m.*`.
 *
 * Weights: `fill` for the active tab, `duotone` inside an IconTile, `regular` (the default) elsewhere.
 *
 * Each icon is imported from its own file. The package's main entry re-exports about 1,500 icons, which makes
 * unbundled loads (Vitest, the dev server) slow enough to time out tests.
 */
export type { Icon as MobileIcon, IconWeight as MobileIconWeight } from "@phosphor-icons/react";

// Bottom navigation
export { ChatCircleDots as NavChatIcon } from "@phosphor-icons/react/dist/csr/ChatCircleDots";
export { ClockCounterClockwise as NavHistoryIcon } from "@phosphor-icons/react/dist/csr/ClockCounterClockwise";
export { ChartLineUp as NavMarketIcon } from "@phosphor-icons/react/dist/csr/ChartLineUp";
export { Binoculars as NavWatchlistIcon } from "@phosphor-icons/react/dist/csr/Binoculars";

// App bar and common actions
export { NotePencil as NewChatIcon } from "@phosphor-icons/react/dist/csr/NotePencil";
export { Wallet as CreditIcon } from "@phosphor-icons/react/dist/csr/Wallet";
export { User as AccountIcon } from "@phosphor-icons/react/dist/csr/User";
export { GearSix as AdminIcon } from "@phosphor-icons/react/dist/csr/GearSix";
export { ArrowUp as SendIcon } from "@phosphor-icons/react/dist/csr/ArrowUp";
export { X as CloseIcon } from "@phosphor-icons/react/dist/csr/X";
export { MagnifyingGlass as SearchIcon } from "@phosphor-icons/react/dist/csr/MagnifyingGlass";
export { DotsThree as MoreIcon } from "@phosphor-icons/react/dist/csr/DotsThree";
export { CaretLeft as ChevronIcon } from "@phosphor-icons/react/dist/csr/CaretLeft";
export { Plus as AddIcon } from "@phosphor-icons/react/dist/csr/Plus";
export { ArrowsClockwise as RefreshIcon } from "@phosphor-icons/react/dist/csr/ArrowsClockwise";
export { DownloadSimple as DownloadIcon } from "@phosphor-icons/react/dist/csr/DownloadSimple";
export { Trash as DeleteIcon } from "@phosphor-icons/react/dist/csr/Trash";

// Starter prompts and symbol analysis
export { ChartPieSlice as SalesMixIcon } from "@phosphor-icons/react/dist/csr/ChartPieSlice";
export { TrendUp as MonthlySalesIcon } from "@phosphor-icons/react/dist/csr/TrendUp";
export { ChartBar as ProductTrendIcon } from "@phosphor-icons/react/dist/csr/ChartBar";
export { ListNumbers as RankingIcon } from "@phosphor-icons/react/dist/csr/ListNumbers";
export { FileText as QuarterlyReportIcon } from "@phosphor-icons/react/dist/csr/FileText";
export { Scales as BalanceSheetIcon } from "@phosphor-icons/react/dist/csr/Scales";

// Account
export { ShieldCheck as SecurityIcon } from "@phosphor-icons/react/dist/csr/ShieldCheck";
export { TelegramLogo as TelegramIcon } from "@phosphor-icons/react/dist/csr/TelegramLogo";
export { Moon as DarkThemeIcon } from "@phosphor-icons/react/dist/csr/Moon";
export { Desktop as DesktopIcon } from "@phosphor-icons/react/dist/csr/Desktop";
export { SignOut as SignOutIcon } from "@phosphor-icons/react/dist/csr/SignOut";
export { Export as ShareIcon } from "@phosphor-icons/react/dist/csr/Export";

// States
export { WifiSlash as OfflineIcon } from "@phosphor-icons/react/dist/csr/WifiSlash";
export { WarningCircle as ErrorIcon } from "@phosphor-icons/react/dist/csr/WarningCircle";
