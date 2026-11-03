import { createContext, useContext } from "react";

/**
 * How assistant answers are laid out. "compact" is the phone workspace (`/m`, Feature 153): answer blocks with a
 * dedicated mobile presentation read it and switch layout; everything else keeps the desktop rendering.
 */
export type AnswerDensity = "default" | "compact";

export const AnswerDensityContext = createContext<AnswerDensity>("default");

export function useAnswerDensity(): AnswerDensity {
  return useContext(AnswerDensityContext);
}
