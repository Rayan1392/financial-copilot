import { useLocation, useRouter } from "@tanstack/react-router";
import { toDesktopPath, toMobilePath } from "@/lib/experience/path-map";
import { writeExperiencePreference } from "@/lib/experience/client";

/** Manual experience switch (spec §7.5): writes the preference first, then navigates to the mapped path. */
export function useExperienceSwitch() {
  const router = useRouter();
  const location = useLocation();
  return {
    switchToMobile() {
      writeExperiencePreference("mobile");
      void router.navigate({ href: toMobilePath(location.href) });
    },
    switchToDesktop() {
      writeExperiencePreference("desktop");
      void router.navigate({ href: toDesktopPath(location.href) });
    },
  };
}
