import { clsx, type ClassValue } from "clsx";
import { extendTailwindMerge } from "tailwind-merge";

/**
 * The mobile type scale (`--text-m-*` in styles.css) adds font-size utilities such as `text-m-body`. Without
 * this, tailwind-merge reads them as text colors and drops a real color given in the same call.
 */
const twMerge = extendTailwindMerge({
  extend: {
    classGroups: {
      "font-size": [{ text: ["m-hero", "m-head", "m-body", "m-num", "m-title", "m-sub", "m-cap"] }],
    },
  },
});

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}
