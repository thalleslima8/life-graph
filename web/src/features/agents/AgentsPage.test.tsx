import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";
import type { AgentIdentityView } from "./agentIdentities";

const CLAUDE_ID = "01920000-0000-7000-8000-0000000000a1";
const CHATGPT_ID = "01920000-0000-7000-8000-0000000000a2";

function anAgent(overrides: Partial<AgentIdentityView> = {}): AgentIdentityView {
  return {
    id: CLAUDE_ID,
    name: "Claude",
    clientName: "Claude.ai",
    clientId: "claude-ai",
    scopes: ["lifegraph.read", "lifegraph.write"],
    status: "active",
    connectedAt: "2026-10-01T12:00:00Z",
    lastUsedAt: "2026-10-02T12:00:00Z",
    ...overrides,
  };
}

/** Serves `pages` by cursor ("page-1", "page-2"…) and records what was changed. */
function agentsApi(pages: AgentIdentityView[][], { rename, revoke }: { rename?: () => Response; revoke?: () => Response } = {}) {
  const state = { pages, renamed: [] as unknown[], revoked: [] as string[], listReads: 0 };
  server.use(
    sessionHandler(() => ({ email: "ada@example.test" })),
    csrfTokenHandler().handler,
    http.get("/api/agent-identities", ({ request }) => {
      state.listReads += 1;
      const cursor = new URL(request.url).searchParams.get("cursor");
      const index = cursor ? Number(cursor.replace("page-", "")) : 0;
      const next = index + 1 < state.pages.length ? `page-${index + 1}` : null;
      return HttpResponse.json({ data: state.pages[index] ?? [], page: { nextCursor: next } });
    }),
    http.patch("/api/agent-identities/:id", async ({ request, params }) => {
      state.renamed.push({ id: params.id, body: await request.json() });
      return rename ? rename() : HttpResponse.json(anAgent({ id: String(params.id), name: "Pesquisa" }));
    }),
    http.delete("/api/agent-identities/:id", ({ params }) => {
      state.revoked.push(String(params.id));
      if (revoke) {
        return revoke();
      }

      state.pages = state.pages.map((page) => page.filter((agent) => agent.id !== params.id));
      return new HttpResponse(null, { status: 204 });
    }),
  );
  return state;
}

describe("connected agents", () => {
  it("shows each connection with its client, scopes in words, dates and the discontinued badge", async () => {
    agentsApi([[anAgent(), anAgent({ id: CHATGPT_ID, name: "ChatGPT", clientName: "OpenAI", status: "discontinued", lastUsedAt: null, scopes: ["lifegraph.read", "offline_access"] })]]);
    const { container } = renderRoute("/agentes");

    const claude = await screen.findByRole("listitem", { name: "Claude" });
    expect(claude).toHaveTextContent("Nome informado pelo cliente");
    expect(claude).toHaveTextContent("Claude.ai");
    expect(claude).toHaveTextContent("Criar e alterar Nodes e Relations no seu grafo.");
    expect(claude).not.toHaveTextContent("Cliente descontinuado");
    const chatgpt = screen.getByRole("listitem", { name: "ChatGPT" });
    expect(chatgpt).toHaveTextContent("Cliente descontinuado");
    expect(chatgpt).toHaveTextContent("Nunca usado");
    expect(chatgpt).not.toHaveTextContent("offline_access");
    expect(chatgpt).not.toHaveTextContent("Criar e alterar");
    expect(within(chatgpt).getByRole("button", { name: "Revogar ChatGPT" })).toBeInTheDocument();
    expect(within(chatgpt).getByRole("button", { name: "Renomear ChatGPT" })).toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();
  });

  it("explains how to connect an agent when there is none", async () => {
    agentsApi([[]]);
    renderRoute("/agentes");

    expect(await screen.findByText("Nenhum agente conectado.")).toBeInTheDocument();
    expect(screen.getByText(/conector MCP/)).toBeInTheDocument();
  });

  it("offers to try again when the list fails", async () => {
    let fail = true;
    server.use(
      sessionHandler(() => ({ email: "ada@example.test" })),
      http.get("/api/agent-identities", () =>
        fail ? problem(500, "internal_error") : HttpResponse.json({ data: [anAgent()], page: { nextCursor: null } }),
      ),
    );
    renderRoute("/agentes");

    expect(await screen.findByRole("alert")).toHaveTextContent("Não foi possível carregar as conexões.");
    fail = false;
    await userEvent.click(screen.getByRole("button", { name: "Tentar de novo" }));

    expect(await screen.findByRole("listitem", { name: "Claude" })).toBeInTheDocument();
  });

  it("loads more by cursor", async () => {
    agentsApi([[anAgent()], [anAgent({ id: CHATGPT_ID, name: "ChatGPT" })]]);
    renderRoute("/agentes");

    await userEvent.click(await screen.findByRole("button", { name: "Carregar mais" }));

    expect(await screen.findByRole("listitem", { name: "ChatGPT" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Carregar mais" })).not.toBeInTheDocument();
  });

  it("renames a connection", async () => {
    const state = agentsApi([[anAgent()]]);
    renderRoute("/agentes");

    await userEvent.click(await screen.findByRole("button", { name: "Renomear Claude" }));
    const field = screen.getByLabelText("Nome");
    await userEvent.clear(field);
    await userEvent.type(field, "  Pesquisa ");
    await userEvent.click(screen.getByRole("button", { name: "Salvar" }));

    await waitFor(() => expect(state.renamed).toEqual([{ id: CLAUDE_ID, body: { name: "Pesquisa" } }]));
  });

  // FE-032: opening the form focuses the field; closing it returns focus to "Renomear".
  it("focuses the name field when renaming opens and returns focus when it is cancelled", async () => {
    agentsApi([[anAgent()]]);
    renderRoute("/agentes");

    await userEvent.click(await screen.findByRole("button", { name: "Renomear Claude" }));
    expect(screen.getByLabelText("Nome")).toHaveFocus();

    await userEvent.click(screen.getByRole("button", { name: "Cancelar" }));

    await waitFor(() => expect(screen.getByRole("button", { name: "Renomear Claude" })).toHaveFocus());
  });

  it("returns focus to the rename button after saving", async () => {
    const state = agentsApi([[anAgent()]]);
    renderRoute("/agentes");

    await userEvent.click(await screen.findByRole("button", { name: "Renomear Claude" }));
    await userEvent.keyboard("{Enter}");

    await waitFor(() => expect(state.renamed).toHaveLength(1));
    await waitFor(() => expect(screen.getByRole("button", { name: /^Renomear / })).toHaveFocus());
  });

  it("shows the server's refusal of a name under the field, without retrying it", async () => {
    const state = agentsApi([[anAgent()]], { rename: () => problem(400, "validation_failed", { errors: { name: ["Must not contain control characters."] } }) });
    renderRoute("/agentes");

    await userEvent.click(await screen.findByRole("button", { name: "Renomear Claude" }));
    await userEvent.click(screen.getByRole("button", { name: "Salvar" }));

    expect(await screen.findByText("Must not contain control characters.")).toBeInTheDocument();
    expect(screen.getByLabelText("Nome")).toHaveAttribute("aria-invalid", "true");
    expect(state.renamed).toHaveLength(1);
  });

  it("confirms inline before revoking, and cancelling returns the focus", async () => {
    const state = agentsApi([[anAgent()]]);
    renderRoute("/agentes");

    await userEvent.click(await screen.findByRole("button", { name: "Revogar Claude" }));

    expect(screen.getByRole("button", { name: "Confirmar revogação" })).toHaveFocus();
    expect(screen.getByText(/não pode ser desfeito/)).toBeInTheDocument();
    expect(screen.getByText(/corta o acesso deste agente na hora/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Cancelar" }));
    expect(screen.getByRole("button", { name: "Revogar Claude" })).toHaveFocus();
    expect(state.revoked).toEqual([]);
  });

  it("revokes, announces it and moves the focus to the next connection", async () => {
    const state = agentsApi([[anAgent(), anAgent({ id: CHATGPT_ID, name: "ChatGPT" })]]);
    renderRoute("/agentes");

    await userEvent.click(await screen.findByRole("button", { name: "Revogar Claude" }));
    await userEvent.click(screen.getByRole("button", { name: "Confirmar revogação" }));

    await waitFor(() => expect(screen.queryByRole("listitem", { name: "Claude" })).not.toBeInTheDocument());
    expect(state.revoked).toEqual([CLAUDE_ID]);
    expect(screen.getByRole("status")).toHaveTextContent("Claude foi desconectado.");
    await waitFor(() => expect(screen.getByRole("button", { name: "Renomear ChatGPT" })).toHaveFocus());
  });

  it("moves the focus to the title after revoking the last connection", async () => {
    agentsApi([[anAgent()]]);
    renderRoute("/agentes");

    await userEvent.click(await screen.findByRole("button", { name: "Revogar Claude" }));
    await userEvent.click(screen.getByRole("button", { name: "Confirmar revogação" }));

    expect(await screen.findByText("Nenhum agente conectado.")).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole("heading", { name: "Agentes conectados" })).toHaveFocus());
  });

  it("says a connection revoked elsewhere is no longer connected and reads the list again", async () => {
    const state = agentsApi([[anAgent()]], { revoke: () => problem(404, "accounts.agent_identity_not_found") });
    renderRoute("/agentes");

    await userEvent.click(await screen.findByRole("button", { name: "Revogar Claude" }));
    const readsBefore = state.listReads;
    await userEvent.click(screen.getByRole("button", { name: "Confirmar revogação" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("já não está conectada");
    await waitFor(() => expect(state.listReads).toBeGreaterThan(readsBefore));
    expect(state.revoked).toEqual([CLAUDE_ID]);
  });
});
