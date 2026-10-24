import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { anOperation, aProposal, aProposalDetail, aReceipt, NODE_ID, PROPOSAL_ID } from "@/test/graphFixtures";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";
import type { ProposalDetail } from "./agentChanges";

const twoOperations = aProposalDetail([
  anOperation({ index: 0 }),
  anOperation({ index: 1, op: "delete_node", isSensitive: true, fields: [] }),
]);

type Decide = (state: { detail: ProposalDetail | null }) => Response;

/**
 * Serves `detail` (a 404 once it is null), answers the approval with `approve` and the
 * rejection with `reject`, which may change what is served next, and records the bodies sent.
 */
function proposalApi(detail: ProposalDetail, { approve, reject }: { approve?: Decide; reject?: Decide } = {}) {
  const state = { approvals: [] as unknown[], rejections: 0, detail: detail as ProposalDetail | null };
  server.use(
    sessionHandler(() => ({ email: "ada@example.test" })),
    csrfTokenHandler().handler,
    http.get("/api/proposals/:id", () => (state.detail ? HttpResponse.json(state.detail) : problem(404, "agents.proposal_not_found"))),
    http.post("/api/proposals/:id/approval", async ({ request }) => {
      state.approvals.push(await request.json());
      return approve ? approve(state) : HttpResponse.json(aReceipt());
    }),
    http.post("/api/proposals/:id/rejection", () => {
      state.rejections += 1;
      if (reject) {
        return reject(state);
      }

      state.detail = { ...detail, proposal: { ...detail.proposal, status: "rejected" } };
      return HttpResponse.json(state.detail.proposal);
    }),
  );
  return state;
}

/** Decided elsewhere meanwhile: the refusal, and the proposal read again as `status`. */
const decidedElsewhere =
  (status: ProposalDetail["proposal"]["status"]): Decide =>
  (state) => {
    state.detail = state.detail && { ...state.detail, proposal: { ...state.detail.proposal, status } };
    return problem(422, "agents.proposal_not_pending");
  };

const ALREADY_DECIDED = "Esta proposta já foi decidida ou expirou. Nada foi aplicado.";

describe("a proposal", () => {
  it("shows who proposed it and every operation, all checked to approve", async () => {
    proposalApi(twoOperations);
    const { container } = renderRoute(`/agent-changes/proposals/${PROPOSAL_ID}`);

    expect(await screen.findByRole("checkbox", { name: "Operação 1: Alterar Node «Leituras»" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Operação 2: Apagar Node «Leituras»" })).toBeChecked();
    expect(screen.getByText("Claude")).toBeInTheDocument();
    expect(screen.getByText("Sensível: só você aprova")).toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();
  });

  it("approves everything without a list, and part of it with the chosen operations", async () => {
    const user = userEvent.setup();
    const state = proposalApi(twoOperations);
    renderRoute(`/agent-changes/proposals/${PROPOSAL_ID}`);

    await user.click(await screen.findByRole("checkbox", { name: /Operação 2/ }));
    await user.click(screen.getByRole("button", { name: "Aprovar" }));

    await waitFor(() => expect(state.approvals).toEqual([{ operations: [0] }]));
    const decision = await screen.findByText("Aprovada em parte. As operações desmarcadas foram recusadas.");
    await waitFor(() => expect(decision).toHaveFocus());
  });

  it("cannot approve with nothing checked", async () => {
    const user = userEvent.setup();
    proposalApi(aProposalDetail());
    renderRoute(`/agent-changes/proposals/${PROPOSAL_ID}`);

    await user.click(await screen.findByRole("checkbox", { name: /Operação 1/ }));

    expect(screen.getByRole("button", { name: "Aprovar" })).toBeDisabled();
  });

  it("names and highlights the operation whose Node changed since the agent read it", async () => {
    const user = userEvent.setup();
    proposalApi(twoOperations, {
      approve: () => problem(409, "graph.node_version_conflict", { details: { "operations[1].version": ["2"] } }),
    });
    renderRoute(`/agent-changes/proposals/${PROPOSAL_ID}`);

    await user.click(await screen.findByRole("button", { name: "Aprovar" }));

    expect(await screen.findByText("Nada foi aplicado: o Node da operação 2 mudou depois que o agente o leu.")).toBeInTheDocument();
    expect(within(screen.getByRole("region", { name: /Operação 2/ })).getByText("Esta operação impediu a aprovação.")).toBeInTheDocument();
  });

  it("says a relation needs its new Node when the server refuses the dependency", async () => {
    const user = userEvent.setup();
    proposalApi(aProposalDetail([anOperation({ index: 0 })]), {
      approve: () => problem(422, "agents.operation_dependency", { errors: { "operations[0]": ["uses 1"] } }),
    });
    renderRoute(`/agent-changes/proposals/${PROPOSAL_ID}`);

    await user.click(await screen.findByRole("button", { name: "Aprovar" }));

    expect(await screen.findByText(/a operação 1 usa um Node novo que ficou de fora/)).toBeInTheDocument();
  });

  // FE-032: the refusal takes the buttons away; focus goes to what happened, never to the page.
  it("moves focus to what happened when the approval finds it already decided", async () => {
    const user = userEvent.setup();
    proposalApi(aProposalDetail(), { approve: decidedElsewhere("applied") });
    renderRoute(`/agent-changes/proposals/${PROPOSAL_ID}`);

    await user.click(await screen.findByRole("button", { name: "Aprovar" }));

    await waitFor(() => expect(screen.queryByRole("button", { name: "Aprovar" })).not.toBeInTheDocument());
    expect(screen.getByText(ALREADY_DECIDED)).toHaveFocus();
    expect(screen.getByText("Aplicada")).toBeInTheDocument();
  });

  it("moves focus to the title when the proposal is gone on approval", async () => {
    const user = userEvent.setup();
    proposalApi(aProposalDetail(), {
      approve: (state) => {
        state.detail = null;
        return problem(404, "agents.proposal_not_found");
      },
    });
    renderRoute(`/agent-changes/proposals/${PROPOSAL_ID}`);

    await user.click(await screen.findByRole("button", { name: "Aprovar" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Esta proposta não está mais disponível.");
    const title = screen.getByRole("heading", { name: "Proposta de agente" });
    expect(title).toHaveFocus();
    // The focus stays visible there (FE-032): nothing hides the outline of the title.
    expect(title.className).not.toMatch(/outline-none/);
  });

  it("moves focus to what happened when the rejection finds it already decided", async () => {
    const user = userEvent.setup();
    proposalApi(aProposalDetail(), { reject: decidedElsewhere("expired") });
    renderRoute(`/agent-changes/proposals/${PROPOSAL_ID}`);

    await user.click(await screen.findByRole("button", { name: "Recusar tudo" }));

    await waitFor(() => expect(screen.queryByRole("button", { name: "Recusar tudo" })).not.toBeInTheDocument());
    expect(screen.getByText(ALREADY_DECIDED)).toHaveFocus();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  // GEN-070: any other failure of the rejection says so and keeps the buttons.
  it("says the rejection failed on any other error", async () => {
    const user = userEvent.setup();
    proposalApi(aProposalDetail(), { reject: () => problem(500, "unexpected_error") });
    renderRoute(`/agent-changes/proposals/${PROPOSAL_ID}`);

    const rejectButton = await screen.findByRole("button", { name: "Recusar tudo" });
    await user.click(rejectButton);

    expect(await screen.findByRole("alert")).toHaveTextContent("Não foi possível concluir agora. Tente novamente em instantes.");
    expect(rejectButton).toBeEnabled();
  });

  it("rejects it whole", async () => {
    const user = userEvent.setup();
    const state = proposalApi(aProposalDetail());
    renderRoute(`/agent-changes/proposals/${PROPOSAL_ID}`);

    await user.click(await screen.findByRole("button", { name: "Recusar tudo" }));

    await waitFor(() => expect(state.rejections).toBe(1));
    expect(await screen.findByText("Proposta recusada. Nada foi aplicado.")).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole("button", { name: "Aprovar" })).not.toBeInTheDocument());
  });

  it("offers nothing to approve once purge emptied it, and shows a revoked agent", async () => {
    proposalApi(aProposalDetail([], { contentPurged: true, agent: { id: null, name: null, revoked: false } }));
    renderRoute(`/agent-changes/proposals/${PROPOSAL_ID}`);

    expect(await screen.findByText(/conteúdo removido por purge/)).toBeInTheDocument();
    expect(screen.getByText("Agente removido")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Aprovar" })).toBeDisabled();
  });

  // GEN-070: a proposal that is gone, and any other failure, each with its own message.
  it("says a proposal that is gone is no longer available", async () => {
    server.use(sessionHandler(() => ({ email: "ada@example.test" })), http.get("/api/proposals/:id", () => problem(404, "agents.proposal_not_found")));
    renderRoute(`/agent-changes/proposals/${PROPOSAL_ID}`);

    expect(await screen.findByRole("alert")).toHaveTextContent("Esta proposta não está mais disponível.");
  });

  it("says the proposal could not be loaded on any other failure", async () => {
    server.use(sessionHandler(() => ({ email: "ada@example.test" })), http.get("/api/proposals/:id", () => problem(500, "internal")));
    renderRoute(`/agent-changes/proposals/${PROPOSAL_ID}`);

    expect(await screen.findByRole("alert")).toHaveTextContent("Não foi possível carregar. Tente novamente em instantes.");
  });

  it("links an applied proposal to its change", async () => {
    proposalApi(aProposalDetail([anOperation({ nodeId: NODE_ID })], { proposal: aProposal({ status: "applied", changeSetId: "01920000-0000-7000-8000-0000000000c9" }) }));
    renderRoute(`/agent-changes/proposals/${PROPOSAL_ID}`);

    expect(await screen.findByRole("link", { name: "Ver a alteração aplicada" })).toHaveAttribute("href", "/changesets/01920000-0000-7000-8000-0000000000c9");
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
  });
});
