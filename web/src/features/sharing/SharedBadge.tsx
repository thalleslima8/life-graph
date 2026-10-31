import { useQuery } from "@tanstack/react-query";
import { sharedNodeIdsOptions } from "./sharesData";

export const SHARED_BADGE_LABEL = "Compartilhado";

/**
 * The "compartilhado" badge (DA-078): the Node is in a link of the person's that still opens, or
 * opens again when its root comes back. Said in text, never by colour alone (FE-034). Renders
 * nothing while the list loads, when it fails, or when the Node is in no link.
 */
export function SharedBadge({ nodeId }: { nodeId: string }) {
  const shared = useQuery({
    ...sharedNodeIdsOptions(),
    select: (ids) => ids.has(nodeId),
  });
  if (shared.data !== true) {
    return null;
  }

  return (
    <span className="rounded border px-2 py-0.5 text-xs">
      {SHARED_BADGE_LABEL}
    </span>
  );
}
