import { createContext, useContext } from "react";

/** Set by the shared answer picture (`AnswerSharePicture`) for everything rendered inside it. */
export const AnswerPictureContext = createContext(false);

/**
 * True for content rendered inside a shared answer picture. The picture has a fixed width and is drawn node by node,
 * so a component that renders several responsive layouts side by side can render only the one the picture shows.
 */
export function useIsAnswerPicture(): boolean {
  return useContext(AnswerPictureContext);
}
