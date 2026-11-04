type SapioMarkProps = {
  className?: string;
};

/** Sapio's compact market mark: a rising price path inside a calm, rounded tile. */
export function SapioMark({ className = "size-10" }: SapioMarkProps) {
  return (
    <svg
      aria-hidden="true"
      className={className}
      viewBox="0 0 48 48"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <rect x="1" y="1" width="46" height="46" rx="15" fill="currentColor" />
      <path
        d="M12 31.5 19 25l5 3.5L35.5 17"
        stroke="white"
        strokeWidth="3.2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path d="M28 17h7.5v7.5" stroke="white" strokeWidth="3.2" strokeLinecap="round" strokeLinejoin="round" />
      <circle cx="12" cy="31.5" r="2" fill="white" />
    </svg>
  );
}
