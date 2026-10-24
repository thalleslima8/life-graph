import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { aChangeSet, AGENT_ID, aProposal, aReceipt, CHANGESET_ID, PROPOSAL_ID } from "@/test/graphFixtures";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";

const agentChangeSet = aChangeSet({
  actorKind: "agent_identity",
  agentIdentityId: AGENT_ID,
  agent: { id: AGENT_ID, name: "Claude", revoked: false },
  channel: "mcp",
});

const connectedAgent = { id: AGENT_ID, name: "Claude", clientName: "Claude.ai", clientId: "claude-ai", scopes: ["lifegraph.write"], status: "active", connectedAt: "2026-10-01T12:00:00Z", lastUsedAt: null, requiresApproval: false };
const OTHER_AGENT_ID = "01920000-0000-7000-8000-0000000000e2";
const OTHER_PROPOSAL_ID = "01920000-0000-7000-8000-0000000000d2";
const OTHER_CHANGESET_ID = "01920000-0000-7000-8000-0000000000c3";
const LOAD_MORE_ERROR = "Não foi possível carregar mais. Tente de novo.";

/**
 * Serves `pages` by cursor ("page-1"…), read from `cursorParam` (the feed of changes pages by
 * `before`), with `extra` in each answer; from the page `failFrom` on, it fails.
 */
function paged<T>(
  pages: T[][],
  { cursorParam = "cursor", extra = {}, failFrom = Number.POSITIVE_INFINITY }: { cursorParam?: string; extra?: Record<string, unknown>; failFrom?: number } = {},
) {
  return ({ request }: { request: Request }) => {
    const cursor = new URL(request.url).searchParams.get(cursorParam);
    const index = cursor ? Number(cursor.replace("page-", "")) : 0;
    if (index >= failFrom) {
      return problem(500, "unexpected_error");
    }

    const next = index + 1 < pages.length ? `page-${index + 1}` : null;
    return HttpResponse.json({ data: pages[index] ?? [], page: { nextCursor: next }, ...extra });
  };
}

/** Records the query of each list read, and the changes marked as reviewed. */
function agentChangesApi({
  changeSets = [agentChangeSet],
  proposals = [aProposal()],
  agents = () => HttpResponse.json({ data: [connectedAgent], page: { nextCursor: null } }),
}: { changeSets?: (typeof agentChangeSet)[]; proposals?: ReturnType<typeof aProposal>[]; agents?: (info: { request: Request }) => Response } = {}) {
  const state = { proposalQueries: [] as string[], changeSetQueries: [] as string[], reviewed: [] as string[] };
  server.use(
    sessionHandler(() => ({ email: "ada@example.test" })),
    csrfTokenHandler().handler,
    http.get("/api/agent-identities", agents),
    http.get("/api/proposals", ({ request }) => {
      state.proposalQueries.push(new URL(request.url).search);
      return HttpResponse.json({ data: proposals, page: { nextCursor: null } });
    }),
    http.get("/api/changesets", ({ request }) => {
      state.changeSetQueries.push(new URL(request.url).search);
      const data = state.reviewed.length > 0 ? changeSets.map((changeSet) => ({ ...changeSet, reviewedAt: "2026-10-06T12:00:00Z" })) : changeSets;
      return HttpResponse.json({ data, page: { nextCursor: null }, latestCursor: null });
    }),
    http.post("/api/changesets/:id/review", ({ params }) => {
      state.reviewed.push(String(params.id));
      return new HttpResponse(null, { status: 204 });
    }),
  );
  return state;
}

describe("Agent Changes", () => {
  it("lists the proposals and the agents' changes by agent name, each linked to its screen", async () => {
    const state = agentChangesApi();
    const { container } = renderRoute("/agent-changes");

    const proposals = await screen.findByRole("list", { name: "Propostas" });
    const proposal = await within(proposals).findByRole("link", { name: "Claude · 1 operação" });
    expect(proposal).toHaveAttribute("href", `/agent-changes/proposals/${PROPOSAL_ID}`);
    expect(within(proposals).getByText("Aguardando você")).toBeInTheDocument();
    const changes = screen.getByRole("list", { name: "Alterações feitas por agentes" });
    expect(within(changes).getByRole("link", { name: /^Claude · / })).toHaveAttribute("href", `/changesets/${CHANGESET_ID}`);
    expect(within(changes).getByText("Não revisada")).toBeInTheDocument();
    expect(state.changeSetQueries[0]).toContain("agentsOnly=true");
    expect(await axe(container)).toHaveNoViolations();
  });

  it("keeps the filters in the URL and sends them to each list", async () => {
    const state = agentChangesApi();
    renderRoute(`/agent-changes?agentIdentityId=${AGENT_ID}&unreviewed=1&status=proposed`);

    await screen.findByRole("list", { name: "Propostas" });
    expect(screen.getByRole("checkbox", { name: "Só alterações não revisadas" })).toBeChecked();
    expect(state.proposalQueries.at(-1)).toContain("status=proposed");
    expect(state.proposalQueries.at(-1)).toContain(`agentIdentityId=${AGENT_ID}`);
    expect(state.changeSetQueries.at(-1)).toContain("unreviewed=true");
    expect(state.changeSetQueries.at(-1)).toContain(`agentIdentityId=${AGENT_ID}`);
  });

  it("marks an agent's change as reviewed", async () => {
    const user = userEvent.setup();
    const state = agentChangesApi();
    renderRoute("/agent-changes");

    await user.click(await screen.findByRole("button", { name: "Marcar como revisada" }));

    await waitFor(() => expect(state.reviewed).toEqual([CHANGESET_ID]));
    await waitFor(() => expect(screen.queryByText("Não revisada")).not.toBeInTheDocument());
    // FE-032: the button went away, so focus is on what happened, above the list.
    expect(screen.getByText(/^Marcada como revisada: Claude · /)).toHaveFocus();
  });

  // FE-032: under "só não revisadas" the reviewed card leaves the list; focus stays on the page.
  it("keeps focus on the outcome when the reviewed card leaves the unreviewed list", async () => {
    const user = userEvent.setup();
    const state = agentChangesApi();
    server.use(
      http.get("/api/changesets", () =>
        HttpResponse.json({ data: state.reviewed.length > 0 ? [] : [agentChangeSet], page: { nextCursor: null }, latestCursor: null }),
      ),
    );
    renderRoute("/agent-changes?unreviewed=1");

    await user.click(await screen.findByRole("button", { name: "Marcar como revisada" }));

    expect(await screen.findByText("Nada para revisar.")).toBeInTheDocument();
    expect(screen.getByText(/^Marcada como revisada: Claude · /)).toHaveFocus();
  });

  it("undoes an agent's change and moves focus to the outcome", async () => {
    const user = userEvent.setup();
    agentChangesApi();
    const undone: string[] = [];
    server.use(
      http.post("/api/changesets/:id/undo", ({ params }) => {
        undone.push(String(params.id));
        return HttpResponse.json(aReceipt("01920000-0000-7000-8000-0000000000c2"));
      }),
    );
    renderRoute("/agent-changes");

    await user.click(await screen.findByRole("button", { name: "Desfazer" }));

    await waitFor(() => expect(undone).toEqual([CHANGESET_ID]));
    await waitFor(() => expect(screen.getByText(/^Desfeita: Claude · /)).toHaveFocus());
  });

  // FE-014: the cards name their agent from the lists themselves, whatever the agents list does.
  it("names the agent of each card even when the agents list fails, and offers to load it again", async () => {
    agentChangesApi({ agents: () => HttpResponse.json({ code: "internal" }, { status: 500 }) });
    renderRoute("/agent-changes");

    const proposals = await screen.findByRole("list", { name: "Propostas" });
    expect(within(proposals).getByRole("link", { name: "Claude · 1 operação" })).toBeInTheDocument();
    expect(within(screen.getByRole("list", { name: "Alterações feitas por agentes" })).getByRole("link", { name: /^Claude · / })).toBeInTheDocument();
    expect(await screen.findByText("Não foi possível carregar a lista de agentes para o filtro.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Tentar de novo" })).toBeEnabled();
  });

  // The filter offers every connected agent: each page of the agents list is read in turn.
  it("offers every connected agent in the filter, reading each page of the agents list", async () => {
    const user = userEvent.setup();
    agentChangesApi({ agents: paged([[connectedAgent], [{ ...connectedAgent, id: OTHER_AGENT_ID, name: "ChatGPT" }]]) });
    renderRoute("/agent-changes");

    await screen.findByRole("list", { name: "Propostas" });
    await user.click(await screen.findByRole("combobox", { name: "Agente" }));

    expect(await screen.findByRole("option", { name: "ChatGPT" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Claude" })).toBeInTheDocument();
  });

  // A filter by an agent no longer listed still reads as chosen, by what happened to it.
  it("names a chosen agent that is no longer listed as removed or revoked", async () => {
    agentChangesApi();
    renderRoute(`/agent-changes?agentIdentityId=${OTHER_AGENT_ID}`);

    await waitFor(() => expect(screen.getByRole("combobox", { name: "Agente" })).toHaveTextContent("Agente removido ou revogado"));
  });

  it("shows a chosen agent that is listed by its name", async () => {
    agentChangesApi();
    renderRoute(`/agent-changes?agentIdentityId=${AGENT_ID}`);

    await waitFor(() => expect(screen.getByRole("combobox", { name: "Agente" })).toHaveTextContent("Claude"));
    expect(screen.getByRole("combobox", { name: "Agente" })).not.toHaveTextContent("revogado");
  });

  it("loads more proposals and more agents' changes by cursor", async () => {
    const user = userEvent.setup();
    agentChangesApi();
    server.use(
      http.get("/api/proposals", paged([[aProposal()], [aProposal({ id: OTHER_PROPOSAL_ID, agent: { id: OTHER_AGENT_ID, name: "ChatGPT", revoked: false } })]])),
      http.get(
        "/api/changesets",
        paged([[agentChangeSet], [{ ...agentChangeSet, id: OTHER_CHANGESET_ID, agent: { id: OTHER_AGENT_ID, name: "ChatGPT", revoked: false } }]], {
          cursorParam: "before",
          extra: { latestCursor: null },
        }),
      ),
    );
    renderRoute("/agent-changes");

    await user.click(await screen.findByRole("button", { name: "Carregar mais propostas" }));
    await user.click(screen.getByRole("button", { name: "Carregar mais alterações" }));

    const proposals = screen.getByRole("list", { name: "Propostas" });
    expect(await within(proposals).findByRole("link", { name: "ChatGPT · 1 operação" })).toBeInTheDocument();
    expect(within(proposals).getByRole("link", { name: "Claude · 1 operação" })).toBeInTheDocument();
    const changes = screen.getByRole("list", { name: "Alterações feitas por agentes" });
    expect(await within(changes).findByRole("link", { name: /^ChatGPT · / })).toBeInTheDocument();
    expect(within(changes).getByRole("link", { name: /^Claude · / })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Carregar mais/ })).not.toBeInTheDocument();
  });

  // The pages already read stay when the next one fails; the error is next to the button.
  it("keeps the loaded pages when loading more fails, and says so next to the button", async () => {
    const user = userEvent.setup();
    agentChangesApi();
    server.use(
      http.get("/api/proposals", paged([[aProposal()], [aProposal({ id: OTHER_PROPOSAL_ID })]], { failFrom: 1 })),
      http.get("/api/changesets", paged([[agentChangeSet], [{ ...agentChangeSet, id: OTHER_CHANGESET_ID }]], { cursorParam: "before", extra: { latestCursor: null }, failFrom: 1 })),
    );
    renderRoute("/agent-changes");

    await user.click(await screen.findByRole("button", { name: "Carregar mais propostas" }));
    await user.click(screen.getByRole("button", { name: "Carregar mais alterações" }));

    const proposalsSection = screen.getByRole("region", { name: "Propostas" });
    expect(await within(proposalsSection).findByRole("alert")).toHaveTextContent(LOAD_MORE_ERROR);
    expect(within(proposalsSection).getByRole("link", { name: "Claude · 1 operação" })).toBeInTheDocument();
    expect(within(proposalsSection).getByRole("button", { name: "Carregar mais propostas" })).toBeEnabled();
    const changesSection = screen.getByRole("region", { name: "Alterações feitas por agentes" });
    expect(await within(changesSection).findByRole("alert")).toHaveTextContent(LOAD_MORE_ERROR);
    expect(within(changesSection).getByRole("link", { name: /^Claude · / })).toBeInTheDocument();
    expect(within(changesSection).getByRole("button", { name: "Carregar mais alterações" })).toBeEnabled();
  });

  it("names a revoked agent as such on its card", async () => {
    agentChangesApi({ changeSets: [{ ...agentChangeSet, agent: { id: AGENT_ID, name: "Claude", revoked: true } }] });
    renderRoute("/agent-changes");

    const changes = await screen.findByRole("list", { name: "Alterações feitas por agentes" });
    expect(within(changes).getByRole("link", { name: /^Claude \(revogado\) · / })).toBeInTheDocument();
  });

  // DA-135: the card offers Undo only while the server says it is available.
  it("offers Undo only while it is available and says why it is not", async () => {
    agentChangesApi({
      changeSets: [
        { ...agentChangeSet, undo: { state: "unavailable", byChangeSetId: null, reason: "content_purged" } },
        { ...agentChangeSet, id: "01920000-0000-7000-8000-0000000000c2", status: "reverted", undo: { state: "undone", byChangeSetId: CHANGESET_ID, reason: null } },
      ],
    });
    renderRoute("/agent-changes");

    const changes = await screen.findByRole("list", { name: "Alterações feitas por agentes" });
    expect(within(changes).queryByRole("button", { name: "Desfazer" })).not.toBeInTheDocument();
    expect(within(changes).getByText("Não dá mais para desfazer: parte do conteúdo foi removida por purge.")).toBeInTheDocument();
    expect(within(changes).getByText("Desfeita")).toBeInTheDocument();
  });

  // GEN-070: each list says when it could not load, apart from the other.
  it("says when the proposals could not be loaded", async () => {
    agentChangesApi();
    server.use(http.get("/api/proposals", () => problem(500, "unexpected_error")));
    renderRoute("/agent-changes");

    const proposals = await screen.findByRole("region", { name: "Propostas" });
    expect(await within(proposals).findByRole("alert")).toHaveTextContent("Não foi possível carregar. Tente novamente em instantes.");
    expect(await screen.findByRole("list", { name: "Alterações feitas por agentes" })).toBeInTheDocument();
  });

  it("says when the agents' changes could not be loaded", async () => {
    agentChangesApi();
    server.use(http.get("/api/changesets", () => problem(500, "unexpected_error")));
    renderRoute("/agent-changes");

    const changes = await screen.findByRole("region", { name: "Alterações feitas por agentes" });
    expect(await within(changes).findByRole("alert")).toHaveTextContent("Não foi possível carregar. Tente novamente em instantes.");
    expect(await screen.findByRole("list", { name: "Propostas" })).toBeInTheDocument();
  });

  it("says when there is no proposal and no agent's change", async () => {
    agentChangesApi({ proposals: [], changeSets: [] });
    renderRoute("/agent-changes");

    expect(await screen.findByText("Nenhuma proposta.")).toBeInTheDocument();
    expect(await screen.findByText("Nenhuma alteração de agente.")).toBeInTheDocument();
  });

  it("says there is nothing to review under the unreviewed filter", async () => {
    agentChangesApi({ changeSets: [] });
    renderRoute("/agent-changes?unreviewed=1");

    expect(await screen.findByText("Nada para revisar.")).toBeInTheDocument();
  });

  // GEN-070: a refused Undo names what changed since, on the card, and leaves it there.
  it("says on the card why its Undo was refused", async () => {
    const user = userEvent.setup();
    agentChangesApi();
    server.use(http.post("/api/changesets/:id/undo", () => problem(409, "graph.undo_conflict", { details: { "entries[0]": ["changed"] } })));
    renderRoute("/agent-changes");

    await user.click(await screen.findByRole("button", { name: "Desfazer" }));

    const card = screen.getByRole("listitem", { name: /^Claude · / });
    expect(await within(card).findByRole("alert")).toHaveTextContent("Não dá para desfazer: Leituras mudou depois desta alteração. Nada foi sobrescrito.");
    expect(within(card).getByRole("button", { name: "Desfazer" })).toBeEnabled();
  });

  it("says on the card when marking it as reviewed failed", async () => {
    const user = userEvent.setup();
    agentChangesApi();
    server.use(http.post("/api/changesets/:id/review", () => problem(500, "unexpected_error")));
    renderRoute("/agent-changes");

    await user.click(await screen.findByRole("button", { name: "Marcar como revisada" }));

    const card = screen.getByRole("listitem", { name: /^Claude · / });
    expect(await within(card).findByRole("alert")).toHaveTextContent("Não foi possível concluir agora. Tente novamente em instantes.");
    expect(within(card).getByText("Não revisada")).toBeInTheDocument();
  });
});
