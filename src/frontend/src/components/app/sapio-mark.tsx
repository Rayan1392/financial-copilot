type SapioMarkProps = {
  className?: string;
};

/** Shared image component for Sapio's supplied brand mark. */
export function SapioMark({ className = "size-10" }: SapioMarkProps) {
  return (
    <img
      src="/sapio-logo.png"
      alt=""
      aria-hidden="true"
      className={className}
      draggable={false}
    />
  );
}
