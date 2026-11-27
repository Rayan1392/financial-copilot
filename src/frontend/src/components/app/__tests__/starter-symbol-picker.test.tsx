import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, fireEvent, render, renderHook, screen, waitFor } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { STARTER_PROMPTS, type SymbolStarterPrompt } from "@/lib/chat/starter-prompts";

const searchSymbolMetadata = vi.fn();

// Server functions are plain async functions here; useServerFn just returns them.
vi.mock("@tanstack/react-start", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@tanstack/react-start")>()),
  useServerFn: (fn: unknown) => fn,
}));
vi.mock("@/lib/metadata.functions", () => ({
  searchSymbolMetadata: (input: unknown) => searchSymbolMetadata(input),
}));

const { StarterSymbolPicker } = await import("../starter-symbol-picker");
const { useStarterSelection } = await import("@/lib/chat/use-starter-selection");

const gauge = STARTER_PROMPTS.find(
  (prompt): prompt is SymbolStarterPrompt => prompt.id === "valuation-gauges",
)!;
const ranking = STARTER_PROMPTS.find((prompt) => prompt.id === "ranking")!;
const company = (symbolCode: string, companyName: string) => ({
  externalCompanyId: symbolCode,
  symbolCode,
  companyName,
});

beforeAll(() => {
  // cmdk measures its list and scrolls the highlighted row into view; jsdom has neither.
  globalThis.ResizeObserver ??= class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
  Element.prototype.scrollIntoView ??= () => {};
});

beforeEach(() => {
  searchSymbolMetadata.mockReset();
});

function renderPicker(watchlistSymbols: string[] = []) {
  const onPick = vi.fn();
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <StarterSymbolPicker prompt={gauge} watchlistSymbols={watchlistSymbols} onPick={onPick} />
    </QueryClientProvider>,
  );
  const input = screen.getByRole("combobox");
  const type = (text: string) => fireEvent.change(input, { target: { value: text } });
  const preview = () => screen.getByTestId("starter-picker-preview").textContent;
  return { onPick, input, type, preview };
}

describe("StarterSymbolPicker", () => {
  it("offers the watchlist symbols and the example symbol before the user types", () => {
    renderPicker(["فولاد", "شپنا"]);

    expect(screen.getByText("دیده‌بان شما")).toBeInTheDocument();
    expect(screen.getByText("نمونه")).toBeInTheDocument();
    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual([
      "فولاد",
      "شپنا",
      "گدنا",
    ]);
    expect(searchSymbolMetadata).not.toHaveBeenCalled();
  });

  it("offers only the example symbol to a user without a watchlist", () => {
    renderPicker();

    expect(screen.queryByText("دیده‌بان شما")).not.toBeInTheDocument();
    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual(["گدنا"]);
  });

  it("previews the exact question for the highlighted symbol", async () => {
    const { preview, input } = renderPicker(["فولاد", "شپنا"]);

    await waitFor(() => expect(preview()).toContain("«گیج‌های ارزش‌گذاری فولاد»"));
    fireEvent.keyDown(input, { key: "ArrowDown" });
    await waitFor(() => expect(preview()).toContain("«گیج‌های ارزش‌گذاری شپنا»"));
  });

  it("picks a row on click and the highlighted row on Enter", async () => {
    const { onPick, input, preview } = renderPicker(["فولاد"]);

    await waitFor(() => expect(preview()).toContain("فولاد"));
    fireEvent.keyDown(input, { key: "Enter" });
    expect(onPick).toHaveBeenLastCalledWith("فولاد");

    fireEvent.click(screen.getByRole("option", { name: "گدنا" }));
    expect(onPick).toHaveBeenLastCalledWith("گدنا");
    expect(onPick).toHaveBeenCalledTimes(2);
  });

  it("searches after the debounce and lists symbol and company, without rights issues", async () => {
    searchSymbolMetadata.mockResolvedValue([
      company("شپنا", "پالایش نفت اصفهان"),
      company("شپناح", "ح. پالایش نفت اصفهان"),
    ]);
    const { type, onPick, preview } = renderPicker(["فولاد"]);

    type(" شپ ");
    expect(screen.getByRole("status")).toHaveTextContent("در حال جست‌وجو…");
    expect(screen.queryByText("دیده‌بان شما")).not.toBeInTheDocument();

    const option = await screen.findByRole("option", { name: /شپنا/ });
    expect(searchSymbolMetadata).toHaveBeenCalledTimes(1);
    expect(searchSymbolMetadata).toHaveBeenCalledWith({ data: { search: "شپ", limit: 12 } });
    expect(screen.getAllByRole("option")).toHaveLength(1);
    expect(option).toHaveTextContent("پالایش نفت اصفهان");
    await waitFor(() => expect(preview()).toContain("«گیج‌های ارزش‌گذاری شپنا»"));

    fireEvent.click(option);
    expect(onPick).toHaveBeenCalledWith("شپنا");
  });

  it("says so when no symbol is found", async () => {
    searchSymbolMetadata.mockResolvedValue([]);
    const { type, preview } = renderPicker();

    type("ققق");

    expect(await screen.findByText("نمادی یافت نشد")).toBeInTheDocument();
    expect(preview()).toContain("«گیج‌های ارزش‌گذاری ‹نماد›»");
  });

  it("reports a failed search and searches again on «تلاش مجدد»", async () => {
    searchSymbolMetadata.mockRejectedValueOnce(new Error("boom"));
    searchSymbolMetadata.mockResolvedValue([company("فملی", "ملی صنایع مس ایران")]);
    const { type } = renderPicker();

    type("فم");
    expect(await screen.findByRole("alert")).toHaveTextContent("جست‌وجو ناموفق بود");

    fireEvent.click(screen.getByRole("button", { name: "تلاش مجدد" }));

    expect(await screen.findByRole("option", { name: /فملی/ })).toBeInTheDocument();
    expect(searchSymbolMetadata).toHaveBeenCalledTimes(2);
  });

  it("returns to the watchlist and example rows when the search text is cleared", async () => {
    searchSymbolMetadata.mockResolvedValue([company("فملی", "ملی صنایع مس ایران")]);
    const { type } = renderPicker(["فولاد"]);

    type("فم");
    await screen.findByRole("option", { name: /فملی/ });
    type("");

    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual([
      "فولاد",
      "گدنا",
    ]);
  });
});

describe("useStarterSelection", () => {
  it("sends a direct card's question at once", () => {
    const submit = vi.fn();
    const { result } = renderHook(() => useStarterSelection(submit));

    act(() => result.current.select(ranking));

    expect(submit).toHaveBeenCalledWith("رتبه‌بندی گزارش‌های ماهانه را نشان بده");
    expect(result.current.open).toBe(false);
  });

  it("opens the picker for a symbol card and sends nothing until a symbol is picked", () => {
    const submit = vi.fn();
    const { result } = renderHook(() => useStarterSelection(submit));

    act(() => result.current.select(gauge));
    expect(result.current.open).toBe(true);
    expect(result.current.prompt).toBe(gauge);
    expect(submit).not.toHaveBeenCalled();

    act(() => result.current.pick("فولاد"));
    expect(submit).toHaveBeenCalledTimes(1);
    expect(submit).toHaveBeenCalledWith("گیج‌های ارزش‌گذاری فولاد");
    expect(result.current.open).toBe(false);
  });

  it("sends nothing when the picker is closed, or for a pick after it closed", () => {
    const submit = vi.fn();
    const { result } = renderHook(() => useStarterSelection(submit));

    act(() => result.current.select(gauge));
    act(() => result.current.close());
    act(() => result.current.pick("فولاد"));

    expect(result.current.open).toBe(false);
    expect(submit).not.toHaveBeenCalled();
  });
});
