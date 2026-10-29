import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { aNode, NODE_ID, OTHER_NODE_ID } from "@/test/graphFixtures";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { ACCOUNT_ROOT_ID, server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";
import { GRAPH_ENTRY_LOADING_MESSAGE, ROOT_NOT_FOUND_TITLE } from "./GraphEntryPage";
import { graphPath } from "./graphView";
import { GRAPH_ENTRY_PATH, readLastFocus, rememberLastFocus } from "./localGraph";

/** Serves the Nodes in `existing`, a 404 for any other, and counts the root reads. */
function entryApi(existing: string[] = [NODE_ID]) {
  const state = { rootReads: 0 };
  server.use(
    sessionHandler(() => ({ email: "ada@example.test" })),
    csrfTokenHandler().handler,
    http.get("/api/account-root", () => {
      state.rootReads += 1;
      return HttpResponse.json({ id: ACCOUNT_ROOT_ID });
    }),
    http.get("/api/nodes/:nodeId", ({ params }) =>
      existing.includes(String(params.nodeId)) ? HttpResponse.json(aNode({ id: String(params.nodeId) })) : problem(404, "graph.node_not_found"),
    ),
    // The graph page itself is not under test here: it never answers.
    http.get("/api/nodes/:nodeId/graph", () => new Promise<never>(() => undefined)),
  );
  return state;
}

describe("graph entry", () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it("opens the graph at the Raiz da Conta when there is no last focus", async () => {
    entryApi();
    const { router } = renderRoute(GRAPH_ENTRY_PATH);

    await waitFor(() => expect(router.state.location.pathname).toBe(graphPath(ACCOUNT_ROOT_ID)));
  });

  it("opens the graph at the last focus of this browser, before the root", async () => {
    const state = entryApi([OTHER_NODE_ID]);
    rememberLastFocus(OTHER_NODE_ID);
    const { router } = renderRoute(GRAPH_ENTRY_PATH);

    await waitFor(() => expect(router.state.location.pathname).toBe(graphPath(OTHER_NODE_ID)));
    expect(state.rootReads).toBe(0);
  });

  it("opens at the Node named by ?focus=, without waiting for the root", async () => {
    const state = entryApi();
    rememberLastFocus(OTHER_NODE_ID);
    const { router } = renderRoute(`${GRAPH_ENTRY_PATH}?focus=${NODE_ID}`);

    await waitFor(() => expect(router.state.location.pathname).toBe(graphPath(NODE_ID)));
    expect(state.rootReads).toBe(0);
  });

  it("opens at the root when the Node named by ?focus= is not found, and keeps the last focus", async () => {
    entryApi([OTHER_NODE_ID]);
    rememberLastFocus(OTHER_NODE_ID);
    const { router } = renderRoute(`${GRAPH_ENTRY_PATH}?focus=${NODE_ID}`);

    await waitFor(() => expect(router.state.location.pathname).toBe(graphPath(ACCOUNT_ROOT_ID)));
    expect(readLastFocus()).toBe(OTHER_NODE_ID);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("forgets a last focus that is not found and opens at the root, with no error", async () => {
    entryApi([]);
    rememberLastFocus(OTHER_NODE_ID);
    const { router } = renderRoute(GRAPH_ENTRY_PATH);

    await waitFor(() => expect(router.state.location.pathname).toBe(graphPath(ACCOUNT_ROOT_ID)));
    expect(readLastFocus()).toBeUndefined();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("says the graph is loading while the root is on its way", async () => {
    entryApi();
    server.use(http.get("/api/account-root", () => new Promise<never>(() => undefined)));
    const { container } = renderRoute(GRAPH_ENTRY_PATH);

    expect(await screen.findByRole("status")).toHaveTextContent(GRAPH_ENTRY_LOADING_MESSAGE);
    expect(await axe(container)).toHaveNoViolations();
  });

  it("says the root could not be loaded and reads it again on retry", async () => {
    entryApi();
    let fails = true;
    server.use(http.get("/api/account-root", () => (fails ? problem(500, "unexpected") : HttpResponse.json({ id: ACCOUNT_ROOT_ID }))));
    const { router } = renderRoute(GRAPH_ENTRY_PATH);

    expect(await screen.findByRole("alert")).toHaveTextContent("Não foi possível carregar.");
    fails = false;
    await userEvent.click(screen.getByRole("button", { name: "Tentar novamente" }));
    await waitFor(() => expect(router.state.location.pathname).toBe(graphPath(ACCOUNT_ROOT_ID)));
  });

  it("explains a missing root and links to the Nós soltos", async () => {
    entryApi();
    server.use(http.get("/api/account-root", () => problem(422, "graph.root_missing")));
    const { container } = renderRoute(GRAPH_ENTRY_PATH);

    expect(await screen.findByRole("heading", { name: ROOT_NOT_FOUND_TITLE })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Ver os Nós soltos" })).toHaveAttribute("href", "/unanchored");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();
  });
});
