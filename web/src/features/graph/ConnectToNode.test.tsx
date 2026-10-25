import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import type { SearchResult } from "@/features/search/searchData";
import { aNode, aReceipt, aType, NODE_ID, OTHER_NODE_ID, ontologyHandlers, RELATION_ID } from "@/test/graphFixtures";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { ACCOUNT_ROOT_ID, server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";
import { ALREADY_CONNECTED_MESSAGE, CONNECTED_MESSAGE, TARGET_GONE_MESSAGE } from "./ConnectToNode";
import type { RelationItem } from "./graphData";
import { ROOT_MISSING_MESSAGE } from "./graphMessages";

const aHit = (overrides: Partial<SearchResult> = {}): SearchResult => ({
  id: OTHER_NODE_ID,
  title: "Meditações",
  typeName: "Livro",
  snippet: "",
  match: "both",
  ...overrides,
});

const toRoot: RelationItem = {
  id: RELATION_ID,
  kind: "related_to",
  sourceNodeId: NODE_ID,
  sourceNodeTitle: "Leituras",
  targetNodeId: ACCOUNT_ROOT_ID,
  targetNodeTitle: "Raiz",
  assertion: "hard",
  origin: "user",
  confidence: null,
  strength: 1,
  createdAt: "2026-10-25T12:00:00Z",
};

type ConnectApiOptions = { hits?: SearchResult[]; relations?: RelationItem[]; isAccountRoot?: boolean };

/** The Inspector of NODE_ID, a search that answers `hits`, and a record of what is searched and written. */
function connectApi({ hits = [aHit()], relations = [], isAccountRoot = false }: ConnectApiOptions = {}) {
  const state = { searches: [] as string[], writes: [] as unknown[], nodeReads: 0 };
  server.use(
    sessionHandler(() => ({ email: "ada@example.test" })),
    csrfTokenHandler().handler,
    ...ontologyHandlers({ types: [aType()] }),
    http.get("/api/tags", () => HttpResponse.json({ data: [], page: { nextCursor: null } })),
    http.get("/api/nodes", () => HttpResponse.json({ data: [], page: { nextCursor: null, totalCount: 0 } })),
    http.get("/api/nodes/:nodeId", () => {
      state.nodeReads += 1;
      return HttpResponse.json(aNode({ isAccountRoot }));
    }),
    http.get("/api/nodes/:nodeId/relations", () => HttpResponse.json({ data: relations, page: { nextCursor: null } })),
    http.get("/api/search", ({ request }) => {
      state.searches.push(new URL(request.url).searchParams.get("q") ?? "");
      return HttpResponse.json({ items: hits, degraded: false });
    }),
    http.post("/api/relations", async ({ request }) => {
      state.writes.push(await request.json());
      return HttpResponse.json(aReceipt());
    }),
  );
  return state;
}

async function openConnect() {
  const view = renderRoute(`/nodes/${NODE_ID}`);
  const form = await screen.findByRole("search", { name: "Conectar a…" });
  return { ...view, form };
}

describe("Conectar a… in the node inspector", () => {
  beforeEach(() => {
    window.history.replaceState(null, "", "/");
  });

  it("connects the Node to the Raiz da Conta in one click, from this Node to the root", async () => {
    const state = connectApi();
    const { form } = await openConnect();

    const button = within(form).getByRole("button", { name: "Conectar à Raiz" });
    await waitFor(() => expect(button).toBeEnabled());
    await userEvent.click(button);

    expect(await screen.findByText(CONNECTED_MESSAGE)).toHaveAttribute("role", "status");
    expect(state.writes).toEqual([{ sourceNodeId: NODE_ID, targetNodeId: ACCOUNT_ROOT_ID, kind: "related_to" }]);
    expect(within(form).getByLabelText("Conectar a…")).toHaveFocus();
  });

  it("does not search with fewer than two letters, and says why", async () => {
    const state = connectApi();
    const { form } = await openConnect();

    await userEvent.type(within(form).getByLabelText("Conectar a…"), "a{Enter}");

    expect(await screen.findByText("Digite ao menos 2 letras para buscar.")).toBeInTheDocument();
    expect(state.searches).toEqual([]);
  });

  it("connects to a Node found by the search, never offering the Node itself or the root", async () => {
    const state = connectApi({ hits: [aHit(), aHit({ id: NODE_ID, title: "Leituras" }), aHit({ id: ACCOUNT_ROOT_ID, title: "Raiz" })] });
    const { form } = await openConnect();

    await userEvent.type(within(form).getByLabelText("Conectar a…"), "medit{Enter}");
    const options = await screen.findByRole("list", { name: "Nodes para conectar" });
    expect(within(options).getAllByRole("listitem")).toHaveLength(1);
    await userEvent.click(within(options).getByRole("button", { name: "Conectar a Meditações" }));

    expect(await screen.findByText(CONNECTED_MESSAGE)).toBeInTheDocument();
    expect(state.searches[0]).toBe("medit");
    expect(state.writes).toEqual([{ sourceNodeId: NODE_ID, targetNodeId: OTHER_NODE_ID, kind: "related_to" }]);
  });

  it("says when the search finds nothing", async () => {
    connectApi({ hits: [] });
    const { form } = await openConnect();

    await userEvent.type(within(form).getByLabelText("Conectar a…"), "nada{Enter}");

    expect(await screen.findByText("Nenhum Node encontrado.")).toHaveAttribute("role", "status");
  });

  it("says when the search fails and searches again on retry", async () => {
    const state = connectApi();
    let fails = true;
    server.use(
      http.get("/api/search", ({ request }) => {
        state.searches.push(new URL(request.url).searchParams.get("q") ?? "");
        return fails ? problem(500, "unexpected") : HttpResponse.json({ items: [aHit()], degraded: false });
      }),
    );
    const { form } = await openConnect();

    await userEvent.type(within(form).getByLabelText("Conectar a…"), "medit{Enter}");
    expect(await screen.findByText("Não foi possível carregar. Tente novamente em instantes.")).toHaveAttribute("role", "alert");
    fails = false;
    await userEvent.click(screen.getByRole("button", { name: "Tentar novamente" }));

    expect(await screen.findByRole("button", { name: "Conectar a Meditações" })).toBeInTheDocument();
  });

  it("disables the root shortcut and says why when the Account has no root", async () => {
    connectApi();
    server.use(http.get("/api/account-root", () => problem(422, "graph.root_missing")));
    const { form } = await openConnect();

    expect(await within(form).findByText(ROOT_MISSING_MESSAGE)).toBeInTheDocument();
    expect(within(form).getByRole("button", { name: "Conectar à Raiz" })).toBeDisabled();
    expect(within(form).queryByRole("alert")).not.toBeInTheDocument();
  });

  it("says the root could not be loaded and reads it again on retry", async () => {
    connectApi();
    let fails = true;
    server.use(http.get("/api/account-root", () => (fails ? problem(500, "unexpected") : HttpResponse.json({ id: ACCOUNT_ROOT_ID }))));
    const { form } = await openConnect();

    expect(await within(form).findByRole("alert")).toHaveTextContent("Não foi possível carregar a Raiz.");
    fails = false;
    await userEvent.click(within(form).getByRole("button", { name: "Tentar novamente" }));

    await waitFor(() => expect(within(form).getByRole("button", { name: "Conectar à Raiz" })).toBeEnabled());
  });

  it("says the target no longer exists and searches again when the write answers NotFound", async () => {
    const state = connectApi();
    server.use(http.post("/api/relations", () => problem(404, "graph.node_not_found")));
    const { form } = await openConnect();

    await userEvent.type(within(form).getByLabelText("Conectar a…"), "medit{Enter}");
    await userEvent.click(await screen.findByRole("button", { name: "Conectar a Meditações" }));

    expect(await screen.findByText(TARGET_GONE_MESSAGE)).toHaveAttribute("role", "alert");
    await waitFor(() => expect(state.searches).toEqual(["medit", "medit"]));
  });

  it("says the Node is already connected instead of writing a second Relation", async () => {
    const state = connectApi({ relations: [toRoot] });
    const { form } = await openConnect();

    await screen.findByRole("list", { name: "Relations" });
    const button = within(form).getByRole("button", { name: "Conectar à Raiz" });
    await waitFor(() => expect(button).toBeEnabled());
    await userEvent.click(button);

    expect(await screen.findByText(ALREADY_CONNECTED_MESSAGE)).toHaveAttribute("role", "status");
    expect(state.writes).toEqual([]);
  });

  it("is not offered on the Raiz da Conta itself", async () => {
    connectApi({ isAccountRoot: true });
    renderRoute(`/nodes/${NODE_ID}`);

    await screen.findByRole("heading", { level: 1, name: "Leituras" });
    expect(screen.queryByRole("search", { name: "Conectar a…" })).not.toBeInTheDocument();
  });

  it("has no accessibility violations with search results on screen", async () => {
    connectApi();
    const { container, form } = await openConnect();

    await userEvent.type(within(form).getByLabelText("Conectar a…"), "medit{Enter}");
    await screen.findByRole("list", { name: "Nodes para conectar" });

    expect(await axe(container)).toHaveNoViolations();
  });
});
