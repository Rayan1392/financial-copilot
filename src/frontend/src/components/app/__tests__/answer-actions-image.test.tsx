import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { AssistantChatBlock } from "@/lib/chat.functions";
import { renderNodeToPngBlob } from "@/lib/chat/answer-image";
import { AnswerActions } from "../answer-actions";
import { MessageList } from "../message-list";
import { gaugeResult, ratioCard } from "./valuation-gauge-fixtures";

// Feature 159 change «valuation-gauge-answer-image-share»: the capture library is replaced; everything else is real.
vi.mock("@/lib/chat/answer-image", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/chat/answer-image")>()),
  renderNodeToPngBlob: vi.fn(),
}));
vi.mock("@/components/app/follow-symbol-button", () => ({ FollowSymbolButton: () => null }));

const draw = vi.mocked(renderNodeToPngBlob);
const png = new Blob(["png"], { type: "image/png" });
const TEXT = "گیج P/E فجر\n\n—\nساپیو | دستیار هوشمند بازار سرمایه\nhttps://sapioai.ir";
const image = {
  content: <p data-testid="picture-content">کارت‌ها</p>,
  width: 800,
  fileName: "گیج-ارزش‌گذاری-فجر.png",
};

class FakeClipboardItem {
  constructor(readonly items: Record<string, Promise<Blob>>) {}
}

/** A clipboard that waits for the picture, as browsers do with a promised item. */
function stubClipboard() {
  const write = vi.fn(async ([item]: FakeClipboardItem[]) => {
    await item.items["image/png"];
  });
  vi.stubGlobal("ClipboardItem", FakeClipboardItem);
  Object.defineProperty(navigator, "clipboard", {
    value: { write, writeText: vi.fn().mockResolvedValue(undefined) },
    configurable: true,
  });
  return write;
}

function stubShare(share: ReturnType<typeof vi.fn>, acceptsFiles = true) {
  Object.defineProperty(navigator, "share", { value: share, configurable: true });
  if (acceptsFiles) {
    Object.defineProperty(navigator, "canShare", { value: () => true, configurable: true });
  }
}

const click = (name: string) =>
  act(async () => {
    fireEvent.click(screen.getByRole("button", { name }));
  });

beforeEach(() => {
  // The real draw waits for the frame; the fake does too, and records what was mounted at that moment.
  draw.mockImplementation(async (node) => {
    await node;
    return png;
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  draw.mockReset();
  document.documentElement.classList.remove("light");
  for (const key of ["share", "canShare", "clipboard"] as const) {
    delete (navigator as unknown as Record<string, unknown>)[key];
  }
});

describe("answer actions with a picture", () => {
  it("offers «کپی تصویر» only for an answer with a picture in a browser that copies images", () => {
    const { rerender } = render(<AnswerActions text="گیج P/E فجر" image={image} />);
    expect(screen.queryByRole("button", { name: "کپی تصویر" })).not.toBeInTheDocument();

    stubClipboard();
    rerender(<AnswerActions text="گیج P/E فجر" />);
    expect(screen.queryByRole("button", { name: "کپی تصویر" })).not.toBeInTheDocument();

    rerender(<AnswerActions text="گیج P/E فجر" image={image} />);
    expect(screen.getByRole("button", { name: "کپی تصویر" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "کپی" })).toBeInTheDocument();
  });

  it("copies the picture from an inert off-screen frame and removes the frame", async () => {
    const write = stubClipboard();
    let frame: { inert: boolean; hidden: string | null; content: boolean } | undefined;
    draw.mockImplementation(async (node) => {
      const element = await node;
      const outer = element.parentElement!;
      frame = {
        inert: outer.hasAttribute("inert"),
        hidden: outer.getAttribute("aria-hidden"),
        content: element.querySelector("[data-testid='picture-content']") !== null,
      };
      return png;
    });
    render(<AnswerActions text="گیج P/E فجر" image={image} />);

    await click("کپی تصویر");
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "تصویر کپی شد" })).toBeInTheDocument(),
    );

    expect(frame).toEqual({ inert: true, hidden: "true", content: true });
    const item = write.mock.calls[0][0][0];
    expect(Object.keys(item.items)).toEqual(["image/png"]);
    await expect(item.items["image/png"]).resolves.toBe(png);
    expect(document.querySelector("[data-share-frame]")).toBeNull();
  });

  it("reports a picture that could not be drawn and removes the frame", async () => {
    stubClipboard();
    draw.mockImplementation(async (node) => {
      await node;
      throw new Error("draw failed");
    });
    render(<AnswerActions text="گیج P/E فجر" image={image} />);

    await click("کپی تصویر");
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "تصویر کپی نشد" })).toBeInTheDocument(),
    );
    expect(document.querySelector("[data-share-frame]")).toBeNull();
  });

  it("shares the picture as a file with the text as caption", async () => {
    const share = vi.fn().mockResolvedValue(undefined);
    stubShare(share);
    render(<AnswerActions text="گیج P/E فجر" image={image} />);

    await click("اشتراک‌گذاری");
    await waitFor(() => expect(share).toHaveBeenCalledTimes(1));

    const data = share.mock.calls[0][0];
    expect(data.title).toBe("پاسخ ساپیو");
    expect(data.text).toBe(TEXT);
    expect(data.files).toHaveLength(1);
    expect(data.files[0].name).toBe("گیج-ارزش‌گذاری-فجر.png");
    expect(data.files[0].type).toBe("image/png");
  });

  it("shares the text where the browser cannot share files, without drawing", async () => {
    const share = vi.fn().mockResolvedValue(undefined);
    stubShare(share, false);
    render(<AnswerActions text="گیج P/E فجر" image={image} />);

    await click("اشتراک‌گذاری");

    expect(share).toHaveBeenCalledWith({ title: "پاسخ ساپیو", text: TEXT });
    expect(draw).not.toHaveBeenCalled();
  });

  it("shares the text when the picture cannot be drawn", async () => {
    const share = vi.fn().mockResolvedValue(undefined);
    stubShare(share);
    draw.mockRejectedValue(new Error("draw failed"));
    render(<AnswerActions text="گیج P/E فجر" image={image} />);

    await click("اشتراک‌گذاری");
    await waitFor(() => expect(share).toHaveBeenCalledWith({ title: "پاسخ ساپیو", text: TEXT }));
  });

  it("keeps the picture when the browser refuses a late share and sends it on the next tap", async () => {
    const refused = Object.assign(new Error("no activation"), { name: "NotAllowedError" });
    const share = vi.fn().mockRejectedValueOnce(refused).mockResolvedValue(undefined);
    stubShare(share);
    render(<AnswerActions text="گیج P/E فجر" image={image} />);

    await click("اشتراک‌گذاری");
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "ارسال تصویر" })).toBeInTheDocument(),
    );
    await click("ارسال تصویر");
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "اشتراک‌گذاری" })).toBeInTheDocument(),
    );

    expect(share).toHaveBeenCalledTimes(2);
    expect(share.mock.calls[1][0].files).toHaveLength(1);
    expect(draw).toHaveBeenCalledTimes(1);
  });

  it("says nothing when the person closes the share sheet", async () => {
    const closed = Object.assign(new Error("closed"), { name: "AbortError" });
    const share = vi.fn().mockRejectedValue(closed);
    stubShare(share);
    render(<AnswerActions text="گیج P/E فجر" image={image} />);

    await click("اشتراک‌گذاری");
    await waitFor(() => expect(share).toHaveBeenCalledTimes(1));

    expect(screen.getByRole("button", { name: "اشتراک‌گذاری" })).toBeInTheDocument();
  });

  it("draws once for several actions and again after a theme change", async () => {
    stubClipboard();
    const share = vi.fn().mockResolvedValue(undefined);
    stubShare(share);
    render(<AnswerActions text="گیج P/E فجر" image={image} />);

    // Reaching the button starts the draw; the tap and the share reuse it.
    fireEvent.pointerEnter(screen.getByRole("button", { name: "کپی تصویر" }));
    await click("کپی تصویر");
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "تصویر کپی شد" })).toBeInTheDocument(),
    );
    await click("اشتراک‌گذاری");
    await waitFor(() => expect(share).toHaveBeenCalledTimes(1));
    expect(draw).toHaveBeenCalledTimes(1);

    document.documentElement.classList.add("light");
    await click("اشتراک‌گذاری");
    await waitFor(() => expect(share).toHaveBeenCalledTimes(2));
    expect(draw).toHaveBeenCalledTimes(2);
  });
});

describe("MessageList image actions", () => {
  const block = (extra: Partial<AssistantChatBlock>): AssistantChatBlock => ({
    message: "متن تلگرام و تاریخچه",
    intent: "ValuationGauges",
    replyLanguage: "fa",
    creditsUsed: 1,
    suggestedQuestions: [],
    suggestedActions: [],
    filters: [],
    citations: [],
    ...extra,
  });
  const renderBlock = (content: AssistantChatBlock) =>
    render(
      <MessageList
        messages={[{ id: "m1", role: "assistant", content, created_at: "2026-11-26T00:00:00Z" }]}
        loading={false}
        streaming={false}
        onSuggested={vi.fn()}
      />,
    );

  it("offers the picture for a gauge answer, drawn from the cards at the wide width", async () => {
    stubClipboard();
    let captured: { width: string; cards: number; details: number } | undefined;
    draw.mockImplementation(async (node) => {
      const element = await node;
      captured = {
        width: element.style.width,
        cards: element.querySelectorAll("[data-testid^='valuation-gauge-card-']").length,
        details: element.querySelectorAll("details:not([data-share-hide])").length,
      };
      return png;
    });
    renderBlock(block({ valuationGaugeCards: gaugeResult([ratioCard("Pe"), ratioCard("Ps")]) }));

    await click("کپی تصویر");
    await waitFor(() => expect(captured).toEqual({ width: "800px", cards: 2, details: 0 }));
  });

  it("uses the narrow width for a single card", async () => {
    stubClipboard();
    let width: string | undefined;
    draw.mockImplementation(async (node) => {
      width = (await node).style.width;
      return png;
    });
    renderBlock(block({ valuationGaugeCards: gaugeResult([ratioCard("Pe")]) }));

    await click("کپی تصویر");
    await waitFor(() => expect(width).toBe("480px"));
  });

  it("offers no picture for a text answer", () => {
    stubClipboard();
    renderBlock(block({}));

    expect(screen.getByRole("button", { name: "کپی" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "کپی تصویر" })).not.toBeInTheDocument();
  });
});
