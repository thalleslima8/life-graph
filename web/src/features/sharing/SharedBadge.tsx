import type { SharedBadgeState } from "./sharesData";

export const SHARED_BADGE_LABEL = "Compartilhado";
export const SHARED_BADGE_UNKNOWN_LABEL = "Compartilhamento não verificado";

/**
 * The "compartilhado" badge (DA-078, DA-299): the Node is in a link of the person's that still
 * opens, or opens again when its root comes back. Said in text, never by colour alone (FE-034).
 * While the answer loads or after it fails it says so instead of claiming the Node is not exposed;
 * a Node in no link shows nothing.
 */
export function SharedBadge({ state }: { state: SharedBadgeState }) {
  if (state === "not-shared") {
    return null;
  }

  if (state === "unknown") {
    return (
      <span className="rounded border border-dashed px-2 py-0.5 text-xs text-muted-foreground">
        {SHARED_BADGE_UNKNOWN_LABEL}
      </span>
    );
  }

  return (
    <span className="rounded border px-2 py-0.5 text-xs">
      {SHARED_BADGE_LABEL}
    </span>
  );
}
