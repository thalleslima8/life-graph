import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { aNode, aReceipt, aType, NODE_ID, OTHER_NODE_ID, ontologyHandlers } from "@/test/graphFixtures";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { ACCOUNT_ROOT_ID, server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";
import { ALREADY_CONNECTED_MESSAGE } from "./ConnectToNode";
import type { NodeSummary } from "./graphData";

/** A row of the Unanchored View, as the list sends it. */
const aSummary = (overrides: Partial<NodeSummary> = {}): NodeSummary => ({ ...aNode(), softRelationCount: 0, ...overrides });

const loose = aSummary({ title: "Leituras" });
const inInbox = aSummary({ id: OTHER_NODE_ID, title: "Ideia solta", inInbox: true });

/** Serves the Unanchored View from `state.nodes` (one page, or two with `secondPage`), and records each read. */
function unanchoredApi(nodes: NodeSummary[] = [loose, inInbox], { secondPage }: { secondPage?: NodeSummary[] } = {}) {
  const state = { nodes, reads: [] as string[] };
  server.use(
    sessionHandler(() => ({ email: "ada@example.test" })),
    csrfTokenHandler().handler,
    ...ontologyHandlers({ types: [aType()] }),
    http.get("/api/nodes", ({ request }) => {
      const query = new URL(request.url).searchParams;
      state.reads.push(query.toString());
      if (query.get("cursor") === "next" && secondPage) {
        return HttpResponse.json({ data: secondPage, page: { nextCursor: null, totalCount: state.nodes.length + secondPage.length } });
      }

      const data = query.get("unanchored") === "true" ? state.nodes : [];
      const nextCursor = secondPage && query.get("unanchored") === "true" && query.get("limit") !== "1" ? "next" : null;
      return HttpResponse.json({ data: query.get("limit") === "1" ? data.slice(0, 1) : data, page: { nextCursor, totalCount: data.length } });
    }),
    http.get("/api/nodes/:nodeId/relations", () => HttpResponse.json({ data: [], page: { nextCursor: null } })),
    http.post("/api/relations", () => {
      state.nodes = state.nodes.filter((node) => node.id !== NODE_ID);
      return HttpResponse.json(aReceipt());
    }),
  );
  return state;
}

describe("Unanchored page", () => {
  beforeEach(() => {
    window.history.replaceState(null, "", "/");
  });

  it("lists the Nodes the root does not reach, marking those also in the Inbox", async () => {
    unanchoredApi();
    const { container } = renderRoute("/unanchored");

    const list = await screen.findByRole("list", { name: "Nós soltos" });
    const rows = within(list).getAllByRole("listitem");
    expect(rows.map((row) => within(row).getByRole("link").textContent)).toEqual(["Leituras", "Ideia solta"]);
    expect(within(rows[0] as HTMLElement).queryByText("Na Inbox")).not.toBeInTheDocument();
    expect(within(rows[1] as HTMLElement).getByText("Na Inbox")).toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();
  });

  it("says it is loading while the list is on its way", async () => {
    unanchoredApi();
    server.use(http.get("/api/nodes", () => new Promise<never>(() => undefined)));
    renderRoute("/unanchored");

    expect(await screen.findByRole("status")).toHaveTextContent("Carregando…");
  });

  it("says when no Node is loose", async () => {
    unanchoredApi([]);
    renderRoute("/unanchored");

    expect(await screen.findByText("Nenhum Node solto.")).toBeInTheDocument();
  });

  it("says when the list could not be loaded and reads it again on retry", async () => {
    const state = unanchoredApi();
    let fails = true;
    server.use(
      http.get("/api/nodes", () => (fails ? problem(500, "unexpected") : HttpResponse.json({ data: state.nodes, page: { nextCursor: null, totalCount: 2 } }))),
    );
    renderRoute("/unanchored");

    expect(await screen.findByRole("alert")).toHaveTextContent("Não foi possível carregar.");
    fails = false;
    await userEvent.click(screen.getByRole("button", { name: "Tentar novamente" }));

    expect(await screen.findByRole("list", { name: "Nós soltos" })).toBeInTheDocument();
  });

  it("loads the next page on request and hides the button when there is no more", async () => {
    const third = aSummary({ id: "01920000-0000-7000-8000-000000000003", title: "Estoicismo" });
    unanchoredApi([loose], { secondPage: [third] });
    renderRoute("/unanchored");

    await userEvent.click(await screen.findByRole("button", { name: "Carregar mais" }));

    expect(await screen.findByRole("link", { name: "Estoicismo" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Carregar mais" })).not.toBeInTheDocument();
  });

  it("connects a row to the root; the row leaves and focus moves to the next one", async () => {
    const state = unanchoredApi();
    renderRoute("/unanchored");

    await userEvent.click(await screen.findByRole("button", { name: "Conectar a… Leituras" }));
    const root = await screen.findByRole("button", { name: "Conectar à Raiz" });
    await waitFor(() => expect(root).toBeEnabled());
    const readsBefore = state.reads.length;
    await userEvent.click(root);

    await waitFor(() => expect(screen.queryByRole("link", { name: "Leituras" })).not.toBeInTheDocument());
    expect(screen.getByRole("link", { name: "Ideia solta" })).toHaveFocus();
    expect(state.reads.slice(readsBefore).some((query) => query.includes("unanchored=true"))).toBe(true);
  });

  it("reads the Node's Relations before writing, with nothing cached, and writes no second Relation", async () => {
    const state = unanchoredApi();
    let writes = 0;
    server.use(
      http.get("/api/nodes/:nodeId/relations", () =>
        HttpResponse.json({
          data: [
            {
              id: "0199a000-0000-7000-8000-0000000000aa",
              kind: "part_of",
              sourceNodeId: ACCOUNT_ROOT_ID,
              sourceNodeTitle: "Raiz",
              targetNodeId: NODE_ID,
              targetNodeTitle: "Leituras",
              assertion: "hard",
              origin: "user",
              confidence: null,
              strength: 1,
              createdAt: "2026-10-25T12:00:00Z",
            },
          ],
          page: { nextCursor: null },
        }),
      ),
      http.post("/api/relations", () => {
        writes += 1;
        return HttpResponse.json(aReceipt());
      }),
    );
    renderRoute("/unanchored");

    await userEvent.click(await screen.findByRole("button", { name: "Conectar a… Leituras" }));
    const root = await screen.findByRole("button", { name: "Conectar à Raiz" });
    await waitFor(() => expect(root).toBeEnabled());
    await userEvent.click(root);

    expect(await screen.findByText(ALREADY_CONNECTED_MESSAGE)).toHaveAttribute("role", "status");
    expect(writes).toBe(0);
    expect(state.nodes).toHaveLength(2);
  });

  it("moves focus to the title when the last row leaves", async () => {
    unanchoredApi([loose]);
    renderRoute("/unanchored");

    await userEvent.click(await screen.findByRole("button", { name: "Conectar a… Leituras" }));
    const root = await screen.findByRole("button", { name: "Conectar à Raiz" });
    await waitFor(() => expect(root).toBeEnabled());
    await userEvent.click(root);

    expect(await screen.findByText("Nenhum Node solto.")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1, name: "Nós soltos" })).toHaveFocus();
  });
});

describe("Unanchored counter in the Inbox", () => {
  beforeEach(() => {
    window.history.replaceState(null, "", "/");
  });

  /** The Inbox, with the Unanchored count answered by `count`. */
  function inboxWithCount(count: () => Response | Promise<Response>) {
    server.use(
      sessionHandler(() => ({ email: "ada@example.test" })),
      csrfTokenHandler().handler,
      ...ontologyHandlers({ types: [aType()] }),
      http.get("/api/nodes", ({ request }) =>
        new URL(request.url).searchParams.get("unanchored") === "true" ? count() : HttpResponse.json({ data: [], page: { nextCursor: null } }),
      ),
    );
    return renderRoute("/nodes?inbox=1");
  }

  const total = (totalCount: number) => () => HttpResponse.json({ data: [], page: { nextCursor: null, totalCount } });

  it("links to the Nós soltos with how many there are", async () => {
    const { container } = inboxWithCount(total(3));

    const link = await screen.findByRole("link", { name: "Sem âncora: 3 Nodes soltos" });
    expect(link).toHaveAttribute("href", "/unanchored");
    expect(link).toHaveTextContent("Sem âncora3");
    expect(await axe(container)).toHaveNoViolations();
  });

  it("caps the number shown at 99+", async () => {
    inboxWithCount(total(250));

    expect(await screen.findByRole("link", { name: "Sem âncora: 250 Nodes soltos" })).toHaveTextContent("99+");
  });

  it("shows no number when nothing is loose", async () => {
    inboxWithCount(total(0));

    expect(await screen.findByRole("link", { name: "Sem âncora: 0 Nodes soltos" })).toHaveTextContent(/^Sem âncora$/);
  });

  it("keeps the link, with no number and no error, when the count fails", async () => {
    inboxWithCount(() => problem(500, "unexpected"));

    const link = await screen.findByRole("link", { name: "Sem âncora" });
    expect(link).toHaveAttribute("href", "/unanchored");
    expect(link).toHaveTextContent(/^Sem âncora$/);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
