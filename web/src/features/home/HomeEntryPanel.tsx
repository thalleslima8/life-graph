import { Link } from "react-router";
import { Button } from "@/components/ui/button";
import { UNANCHORED_PATH } from "@/features/graph/anchoringData";
import { HOME_PATHS, useShownUnanchoredCount } from "./homeData";
import {
  HOME_AGENT_CHANGES_LINK,
  HOME_CONNECT_AGENT_LABEL,
  HOME_CREATE_NODE_LINK,
  HOME_INBOX_LINK,
  HOME_RECENT_LINK,
  HOME_SHORTCUTS_LABEL,
  unanchoredLinkText,
} from "./homeMessages";

const LINK_CLASS = "text-sm underline underline-offset-4";

/**
 * The Home's entry panel (DA-271): links to the Inbox, Desancorados, Recent and Agent Changes,
 * and the actions to create a Node and to connect an agent. It only reads, and only Desancorados
 * carries a number (DA-314); it shows in every state of the Home, a failed graph included (DA-318).
 */
export function HomeEntryPanel() {
  const unanchored = useShownUnanchoredCount();

  return (
    <nav aria-label={HOME_SHORTCUTS_LABEL} className="space-y-4 rounded-md border p-4">
      <ul className="space-y-2">
        <li>
          <Link to={HOME_PATHS.inbox} className={LINK_CLASS}>
            {HOME_INBOX_LINK}
          </Link>
        </li>
        <li>
          <Link to={UNANCHORED_PATH} className={LINK_CLASS}>
            {unanchoredLinkText(unanchored)}
          </Link>
        </li>
        <li>
          <Link to={HOME_PATHS.recentChanges} className={LINK_CLASS}>
            {HOME_RECENT_LINK}
          </Link>
        </li>
        <li>
          <Link to={HOME_PATHS.agentChanges} className={LINK_CLASS}>
            {HOME_AGENT_CHANGES_LINK}
          </Link>
        </li>
      </ul>
      <div className="flex flex-wrap gap-2">
        <Button asChild variant="secondary">
          <Link to={HOME_PATHS.nodes}>{HOME_CREATE_NODE_LINK}</Link>
        </Button>
        <Button asChild variant="secondary">
          <Link to={HOME_PATHS.agents}>{HOME_CONNECT_AGENT_LABEL}</Link>
        </Button>
      </div>
    </nav>
  );
}
