import { Link } from "react-router";
import { Button } from "@/components/ui/button";
import { UNANCHORED_PATH } from "@/features/graph/anchoringData";
import { HOME_PATHS, useShownUnanchoredCount } from "./homeData";
import {
  HOME_CONNECT_AGENT_LABEL,
  HOME_CREATE_FIRST_NODE_LINK,
  HOME_EMPTY_HINT,
  HOME_EMPTY_MESSAGE,
  HOME_UNLINKED_HINT,
  HOME_UNLINKED_MESSAGE,
  unanchoredLinkText,
} from "./homeMessages";

/**
 * What the Home shows in place of a root with no neighbor, so a new Account never sees a lone
 * point (DA-273). `empty`: no Node besides the root, with the actions to create the first Node and
 * to connect an agent. `unlinked`: Nodes exist but none is linked to the root, so the way is to
 * Desancorados, with its count once read (DA-312).
 */
export function HomeEmptyState({ variant }: { variant: "empty" | "unlinked" }) {
  return (
    <div className="space-y-3 rounded-md border p-4">
      {variant === "empty" ? <EmptyContent /> : <UnlinkedContent />}
    </div>
  );
}

function EmptyContent() {
  return (
    <>
      <p className="font-medium">{HOME_EMPTY_MESSAGE}</p>
      <p className="text-sm text-muted-foreground">{HOME_EMPTY_HINT}</p>
      <div className="flex flex-wrap gap-2">
        <Button asChild>
          <Link to={HOME_PATHS.nodes}>{HOME_CREATE_FIRST_NODE_LINK}</Link>
        </Button>
        <Button asChild variant="secondary">
          <Link to={HOME_PATHS.agents}>{HOME_CONNECT_AGENT_LABEL}</Link>
        </Button>
      </div>
    </>
  );
}

function UnlinkedContent() {
  const unanchored = useShownUnanchoredCount();
  return (
    <>
      <p className="font-medium">{HOME_UNLINKED_MESSAGE}</p>
      <p className="text-sm text-muted-foreground">{HOME_UNLINKED_HINT}</p>
      <Button asChild>
        <Link to={UNANCHORED_PATH}>{unanchoredLinkText(unanchored)}</Link>
      </Button>
    </>
  );
}
