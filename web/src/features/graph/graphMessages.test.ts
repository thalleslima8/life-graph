import { describe, expect, it } from "vitest";

import { provenanceOf } from "./graphMessages";
import type { ChangeSetItem } from "./recentChanges";

function changeSet(
  fields: Pick<ChangeSetItem, "actorKind" | "channel"> & Partial<ChangeSetItem>,
): ChangeSetItem {
  return { agentIdentityId: null, source: null, ...fields } as ChangeSetItem;
}

describe("provenanceOf", () => {
  it("names the metadata fetch as the system, on its own channel", () => {
    expect(
      provenanceOf(changeSet({ actorKind: "system", channel: "system" })),
    ).toBe("Busca de metadados · Sistema");
  });

  it("names the user and an agent as before", () => {
    expect(provenanceOf(changeSet({ actorKind: "human", channel: "ui" }))).toBe(
      "Você · Interface",
    );
    expect(
      provenanceOf(
        changeSet({
          actorKind: "agent_identity",
          channel: "mcp",
          agentIdentityId: "a1",
          source: "chat",
        }),
      ),
    ).toBe("Agente a1 · MCP · fonte: chat");
  });
});
