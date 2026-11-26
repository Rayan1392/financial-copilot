import { StarterCard } from "@/components/app/starter-card";
import type { StarterPrompt } from "@/lib/chat/starter-prompts";

/**
 * The desktop starter cards: featured cards first and wide, the rest in two columns. With one featured card and an even
 * number of others, every row is full whatever the total (change «valuation-gauge-starter-card»).
 */
export function StarterCardGrid({
  prompts,
  disabled,
  onSelect,
}: {
  prompts: readonly StarterPrompt[];
  disabled: boolean;
  onSelect: (text: string) => void;
}) {
  const ordered = [
    ...prompts.filter((prompt) => prompt.featured),
    ...prompts.filter((prompt) => !prompt.featured),
  ];
  return (
    <div
      className="grid grid-cols-1 gap-3 text-right md:grid-cols-2"
      data-testid="starter-card-grid"
    >
      {ordered.map((prompt) => (
        <StarterCard key={prompt.text} prompt={prompt} disabled={disabled} onSelect={onSelect} />
      ))}
    </div>
  );
}
