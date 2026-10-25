import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router";
import { UNANCHORED_PATH, unanchoredCountOptions } from "./anchoringData";

const COUNT_CAP = 99;

/**
 * The Inbox's way to Desancorados (DA-197, DA-208): a link with how many Nodes are loose. While
 * the count loads or when it fails, the link stays, with no number: it never blocks the way there.
 */
export function UnanchoredBadge() {
  const count = useQuery(unanchoredCountOptions());
  const total = Number(count.data ?? 0);
  const shown = total > COUNT_CAP ? `${COUNT_CAP}+` : String(total);
  const label = count.isSuccess ? `Sem âncora: ${total === 1 ? "1 Node solto" : `${total} Nodes soltos`}` : "Sem âncora";

  return (
    <Link to={UNANCHORED_PATH} aria-label={label} className="inline-flex items-center gap-2 text-sm underline underline-offset-4">
      Sem âncora
      {count.isSuccess && total > 0 && (
        <span aria-hidden="true" className="rounded-full border px-2 text-xs font-medium no-underline">
          {shown}
        </span>
      )}
    </Link>
  );
}
