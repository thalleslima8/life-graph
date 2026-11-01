import { limitReachedText } from "./limitMessages";
import { isPlanLimitReached, refusedLimitOf, usePlanLimit, type LimitKind } from "./planData";

/**
 * The plan's limit of `kind` at a creation point (DA-266): shown when the server refused a
 * creation past it (`error`) or when the plan says it is reached. Owns the text and its
 * announcement: an alert after a refusal, a status otherwise. Renders nothing below the limit,
 * while the plan loads, or when it fails to load: the server decides anyway (FE-020).
 */
export function LimitNotice({ kind, error }: { kind: LimitKind; error?: unknown }) {
  const status = usePlanLimit(kind);
  const refused = isPlanLimitReached(error, kind);
  if (!refused && !status?.reached) {
    return null;
  }

  return (
    <p role={refused ? "alert" : "status"} className="rounded border border-amber-500 px-3 py-2 text-sm">
      {limitReachedText(kind, status?.max ?? refusedLimitOf(error), status?.used)}
    </p>
  );
}
