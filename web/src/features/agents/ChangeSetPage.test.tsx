import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { aChangeSet, aChangeSetDetail, AGENT_ID, aReceipt, CHANGESET_ID, PROPOSAL_ID } from "@/test/graphFixtures";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";
import type { ChangeSetDetail } from "./agentChanges";

const UNDO_ID = "01920000-0000-7000-8000-0000000000c2";

function changeSetApi(detail: ChangeSetDetail) {
  const state = { undone: [] as string[], reviewed: [] as string[] };
  server.use(
    sessionHandler(() => ({ email: "ada@example.test" })),
    csrfTokenHandler().handler,
    http.get("/api/changesets/:id", () => HttpResponse.json(detail)),
    http.post("/api/changesets/:id/undo", ({ params }) => {
      state.undone.push(String(params.id));
      return HttpResponse.json(aReceipt(UNDO_ID));
    }),
    http.post("/api/changesets/:id/review", ({ params }) => {
      state.reviewed.push(String(params.id));
      return new HttpResponse(null, { status: 204 });
    }),
  );
  return state;
}

describe("the GraphChangeSet screen", () => {
  it("shows the agent, the provenance and each field, and undoes it", async () => {
    const user = userEvent.setup();
    const detail = aChangeSetDetail();
    detail.changeSet = { ...detail.changeSet, source: "Conversa no Claude.ai" };
    const state = changeSetApi(detail);
    const { container } = renderRoute(`/changesets/${CHANGESET_ID}`);

    expect(await screen.findByText("Agente: Claude")).toBeInTheDocument();
    // Provenance: who, by which channel and from which source.
    const provenance = screen.getAllByRole("term").find((term) => term.textContent === "Provenance");
    expect(provenance?.nextElementSibling).toHaveTextContent(`Agente ${AGENT_ID} · MCP · fonte: Conversa no Claude.ai`);
    expect(screen.getByText("Não revisada")).toBeInTheDocument();
    expect(screen.getByText("Atenção: sobrescreveu um valor seu")).toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();

    await user.click(screen.getByRole("button", { name: "Desfazer" }));

    await waitFor(() => expect(state.undone).toEqual([CHANGESET_ID]));
    // FE-032: the button goes away once undone, so focus moves to what happened.
    await waitFor(() => expect(screen.getByText("Desfeito.")).toHaveFocus());
  });

  it("marks an agent's change as reviewed and moves focus to the outcome", async () => {
    const user = userEvent.setup();
    const state = changeSetApi(aChangeSetDetail());
    renderRoute(`/changesets/${CHANGESET_ID}`);

    await user.click(await screen.findByRole("button", { name: "Marcar como revisada" }));

    await waitFor(() => expect(state.reviewed).toEqual([CHANGESET_ID]));
    await waitFor(() => expect(screen.getByText("Marcada como revisada.")).toHaveFocus());
  });

  // GEN-070: a change that is gone, and any other failure, each with its own message.
  it("says a change that is gone is no longer available", async () => {
    server.use(sessionHandler(() => ({ email: "ada@example.test" })), http.get("/api/changesets/:id", () => problem(404, "graph.changeset_not_found")));
    renderRoute(`/changesets/${CHANGESET_ID}`);

    expect(await screen.findByRole("alert")).toHaveTextContent("Esta alteração não está mais disponível.");
  });

  it("says the change could not be loaded on any other failure", async () => {
    server.use(sessionHandler(() => ({ email: "ada@example.test" })), http.get("/api/changesets/:id", () => problem(500, "internal")));
    renderRoute(`/changesets/${CHANGESET_ID}`);

    expect(await screen.findByRole("alert")).toHaveTextContent("Não foi possível carregar. Tente novamente em instantes.");
  });

  // DA-210: the GraphChangeSet that created the Raiz da Conta is never offered for Undo.
  it("says the change that created the account root cannot be undone", async () => {
    changeSetApi(aChangeSetDetail({ undo: { state: "unavailable", byChangeSetId: null, reason: "account_root" } }));
    renderRoute(`/changesets/${CHANGESET_ID}`);

    expect(await screen.findByText("Não dá para desfazer: esta alteração criou a Raiz da Conta, que fica sempre no grafo.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Desfazer" })).not.toBeInTheDocument();
  });

  it("says why Undo is no longer possible, or links the Undo that reverted it", async () => {
    changeSetApi(aChangeSetDetail({ undo: { state: "unavailable", byChangeSetId: null, reason: "content_purged" } }));
    const { unmount } = renderRoute(`/changesets/${CHANGESET_ID}`);

    expect(await screen.findByText(/parte do conteúdo foi removida por purge/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Desfazer" })).not.toBeInTheDocument();
    unmount();

    changeSetApi(aChangeSetDetail({ undo: { state: "undone", byChangeSetId: UNDO_ID, reason: null } }));
    renderRoute(`/changesets/${CHANGESET_ID}`);

    expect(await screen.findByRole("link", { name: "Ver o Undo desta alteração" })).toHaveAttribute("href", `/changesets/${UNDO_ID}`);
  });

  it("shows the person's own change without an agent, and links the proposal an approval came from", async () => {
    changeSetApi(aChangeSetDetail({ agent: null, changeSet: aChangeSet({ proposalId: PROPOSAL_ID }) }));
    renderRoute(`/changesets/${CHANGESET_ID}`);

    expect(await screen.findByText("Você")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Marcar como revisada" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Ver a proposta que originou esta alteração" })).toHaveAttribute("href", `/agent-changes/proposals/${PROPOSAL_ID}`);
  });

  // GEN-070: a refused Undo says what changed since; the button stays to try again later.
  it("says why the Undo was refused", async () => {
    const user = userEvent.setup();
    changeSetApi(aChangeSetDetail());
    server.use(http.post("/api/changesets/:id/undo", () => problem(409, "graph.undo_conflict", { details: { "entries[0]": ["changed"] } })));
    renderRoute(`/changesets/${CHANGESET_ID}`);

    await user.click(await screen.findByRole("button", { name: "Desfazer" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Não dá para desfazer: Leituras de 2026 mudou depois desta alteração. Nada foi sobrescrito.");
    expect(screen.getByRole("button", { name: "Desfazer" })).toBeEnabled();
  });

  it("says when marking it as reviewed failed", async () => {
    const user = userEvent.setup();
    changeSetApi(aChangeSetDetail());
    server.use(http.post("/api/changesets/:id/review", () => problem(500, "unexpected_error")));
    renderRoute(`/changesets/${CHANGESET_ID}`);

    await user.click(await screen.findByRole("button", { name: "Marcar como revisada" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Não foi possível concluir agora. Tente novamente em instantes.");
    expect(screen.getByText("Não revisada")).toBeInTheDocument();
  });
});
