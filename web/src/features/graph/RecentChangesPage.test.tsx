import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { aChangeSet, aFeed, CHANGESET_ID, NODE_ID } from "@/test/graphFixtures";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";
import type { ChangeSetItem } from "./recentChanges";

const AGENT_ID = "01920000-0000-7000-8000-0000000000f1";
const RELATION_ID = "01920000-0000-7000-8000-0000000000d1";

/** The feed serves `items`; the probe finds nothing new. */
function changesApi(items: ChangeSetItem[], undo: () => Response) {
  const state = { items, undone: [] as string[] };
  server.use(
    sessionHandler(() => ({ email: "ada@example.test" })),
    csrfTokenHandler().handler,
    http.get("/api/changesets", ({ request }) => {
      const query = new URL(request.url).searchParams;
      return HttpResponse.json(query.has("limit") ? aFeed([], query.get("since")) : aFeed(state.items, "cursor-1"));
    }),
    http.post("/api/changesets/:id/undo", ({ params }) => {
      state.undone.push(String(params.id));
      return undo();
    }),
  );
  return state;
}

const deletion = aChangeSet({
  entries: [
    { sequence: 0, entityKind: "node", entityId: NODE_ID, operation: "deleted", label: "Leituras", isPurged: false, cascadeOf: null },
    { sequence: 1, entityKind: "relation", entityId: RELATION_ID, operation: "deleted", label: "cita", isPurged: false, cascadeOf: 0 },
  ],
});

describe("recent changes", () => {
  it("shows each ChangeSet with who, by which channel, from which source and what it did", async () => {
    changesApi([aChangeSet({ actorKind: "agent_identity", agentIdentityId: AGENT_ID, channel: "mcp", source: "chat-42" }), deletion], () =>
      HttpResponse.json({}),
    );
    renderRoute("/changes");

    const items = within(await screen.findByRole("list", { name: "Alterações" })).getAllByRole("listitem", { name: /2026/ });
    expect(items[0]).toHaveTextContent(`Agente ${AGENT_ID} · MCP · fonte: chat-42`);
    expect(items[0]).toHaveTextContent("Criou Node «Leituras»");
    expect(items[1]).toHaveTextContent("Você · Interface");
    expect(items[1]).toHaveTextContent("Apagou Relation «cita» (em cascata)");
  });

  it("undoes a ChangeSet and names the Relations that did not come back", async () => {
    const state = changesApi([deletion], () => {
      state.items = [{ ...deletion, status: "reverted", revertedByChangeSetId: "01920000-0000-7000-8000-0000000000c9" }];
      return HttpResponse.json({ changeSetId: "01920000-0000-7000-8000-0000000000c9", changes: [], relationsLeftDeleted: [RELATION_ID] });
    });
    renderRoute("/changes");

    await userEvent.click(await screen.findByRole("button", { name: "Desfazer" }));

    expect(await screen.findByText("Desfeito. Não voltaram: cita.")).toBeInTheDocument();
    expect(await screen.findByText("Desfeita")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Desfazer" })).not.toBeInTheDocument();
    expect(state.undone).toEqual([CHANGESET_ID]);
  });

  it("explains a refused Undo by the entry that conflicted", async () => {
    changesApi([deletion], () => problem(409, "graph.undo_conflict", { details: { "entries[0]": ["Changed later."] } }));
    renderRoute("/changes");

    await userEvent.click(await screen.findByRole("button", { name: "Desfazer" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Não dá para desfazer: Leituras mudou depois desta alteração. Nada foi sobrescrito.");
  });

  it("explains a closed window", async () => {
    changesApi([deletion], () => problem(422, "graph.undo_window_closed"));
    renderRoute("/changes");

    await userEvent.click(await screen.findByRole("button", { name: "Desfazer" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("O prazo para desfazer terminou");
  });

  it("says when there is nothing yet", async () => {
    changesApi([], () => HttpResponse.json({}));
    renderRoute("/changes");

    expect(await screen.findByText("Nenhuma alteração ainda.")).toBeInTheDocument();
  });

  it("has no accessibility violations", async () => {
    changesApi([deletion], () => HttpResponse.json({}));
    const { container } = renderRoute("/changes");
    await screen.findByRole("list", { name: "Alterações" });

    expect(await axe(container)).toHaveNoViolations();
  });
});
