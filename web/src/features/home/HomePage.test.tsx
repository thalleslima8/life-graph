import { act, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { axe } from "vitest-axe";
import { fakeGraphRenderer as fake } from "@/test/fakeGraphRenderer";
import { aLocalGraph, aNode, aType, ontologyHandlers, OTHER_NODE_ID } from "@/test/graphFixtures";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { ACCOUNT_ROOT_ID, server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";
import { LOAD_ERROR_MESSAGE, LOADING_MESSAGE, RETRY_LABEL } from "@/features/graph/graphMessages";
import { graphPath } from "@/features/graph/graphView";
import { readLastFocus } from "@/features/graph/localGraph";
import { UNANCHORED_PATH } from "@/features/graph/anchoringData";
import { HOME_PATHS } from "./homeData";
import {
  HOME_AGENT_CHANGES_LINK,
  HOME_CONNECT_AGENT_LABEL,
  HOME_CREATE_FIRST_NODE_LINK,
  HOME_CREATE_NODE_LINK,
  HOME_EMPTY_MESSAGE,
  HOME_INBOX_LINK,
  HOME_RECENT_LINK,
  HOME_ROOT_LABEL,
  HOME_SHORTCUTS_LABEL,
  HOME_TITLE,
  HOME_UNLINKED_MESSAGE,
  unanchoredLinkText,
} from "./homeMessages";

// jsdom has no WebGL: the Home draws through the fake renderer (DA-137).
vi.mock("@/features/graph/graphRenderer", async () => ({ createSigmaRenderer: (await import("@/test/fakeGraphRenderer")).fakeGraphRenderer.factory }));

/** The Raiz da Conta and one Node linked to it, as the API sends the root's local graph. */
function aRootGraph(overrides: Parameters<typeof aLocalGraph>[0] = {}) {
  const graph = aLocalGraph();
  return aLocalGraph({
    rootNodeId: ACCOUNT_ROOT_ID,
    nodes: graph.nodes.map((node) => (node.id === graph.rootNodeId ? { ...node, id: ACCOUNT_ROOT_ID, title: "Raiz" } : node)),
    edges: graph.edges.map((edge) => ({ ...edge, sourceNodeId: ACCOUNT_ROOT_ID })),
    ...overrides,
  });
}

/** The root alone: the depth-1 graph of an Account whose Nodes the root does not reach. */
const aLonelyRoot = () => aRootGraph({ nodes: [{ id: ACCOUNT_ROOT_ID, title: "Raiz", typeId: null, distance: 0, degree: 0, agentUnreviewed: false }], edges: [] });

type HomeApiOptions = {
  graph?: () => Response;
  /** The first page of `GET /nodes?limit=2`. */
  nodes?: () => Response;
  /** The one-item page of `GET /nodes?unanchored=true` whose total is the Desancorados count. */
  unanchored?: () => Response;
};

const emptyPage = () => HttpResponse.json({ data: [], page: { nextCursor: null } });
const unanchoredTotal = (totalCount: number) => () => HttpResponse.json({ data: [], page: { nextCursor: null, totalCount } });

/** Serves the Home's reads and records each graph request as `{nodeId}?depth={depth}`. */
function homeApi({ graph = () => HttpResponse.json(aRootGraph()), nodes = emptyPage, unanchored = unanchoredTotal(0) }: HomeApiOptions = {}) {
  const asked: string[] = [];
  server.use(
    sessionHandler(() => ({ email: "ada@example.test" })),
    ...ontologyHandlers({ types: [aType()] }),
    http.get("/api/nodes", ({ request }) => (new URL(request.url).searchParams.get("unanchored") === "true" ? unanchored() : nodes())),
    http.get("/api/nodes/:nodeId/graph", ({ params, request }) => {
      asked.push(`${String(params.nodeId)}?depth=${new URL(request.url).searchParams.get("depth")}`);
      return graph();
    }),
  );
  return asked;
}

describe("home page", () => {
  beforeEach(() => fake.reset());

  it("draws the Raiz da Conta's graph one level deep, the root named Seu grafo", async () => {
    const asked = homeApi();
    renderRoute("/");

    expect(await screen.findByRole("application", { name: `Grafo local de ${HOME_ROOT_LABEL}: 2 Nodes e 1 Relation` })).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1, name: HOME_TITLE })).toBeInTheDocument();
    expect(asked).toEqual([`${ACCOUNT_ROOT_ID}?depth=1`]);
    // The renderer mounts once the canvas is in the page, after the role is there.
    await waitFor(() => expect(fake.recorded.mounts.at(-1)?.view.graph.nodes.find((node) => node.id === ACCOUNT_ROOT_ID)?.title).toBe(HOME_ROOT_LABEL));
    expect(screen.queryByRole("radio")).not.toBeInTheDocument();
  });

  it("does not become the graph page's last focus", async () => {
    window.localStorage.clear();
    homeApi();
    renderRoute("/");

    await screen.findByRole("application");
    expect(readLastFocus()).toBeUndefined();
  });

  it("says how much the server left out when it cut the root's neighbors", async () => {
    homeApi({ graph: () => HttpResponse.json(aRootGraph({ truncated: true, omittedNodeCount: 5, omittedEdgeCount: 5 })) });
    renderRoute("/");

    expect(await screen.findByText("5 Nodes omitidos e 5 Relations omitidas. Centralize em outro Node para ver mais.")).toBeInTheDocument();
  });

  it("opens the graph page with the Node selected, or rooted at it", async () => {
    homeApi();
    const { router } = renderRoute("/");
    await screen.findByRole("application");

    act(() => fake.events().onSelect(OTHER_NODE_ID));
    await waitFor(() =>
      expect(`${router.state.location.pathname}${router.state.location.search}`).toBe(`${graphPath(ACCOUNT_ROOT_ID)}?depth=1&selected=${OTHER_NODE_ID}`),
    );

    await act(() => router.navigate("/"));
    await screen.findByRole("application");
    act(() => fake.events().onReroot(OTHER_NODE_ID));
    await waitFor(() => expect(router.state.location.pathname).toBe(graphPath(OTHER_NODE_ID)));
  });

  it("lists the graph when the browser cannot draw it", async () => {
    fake.recorded.failMount = true;
    homeApi();
    renderRoute("/");

    expect(await screen.findByRole("alert")).toHaveTextContent("Não foi possível desenhar o grafo; mostrando a lista.");
    expect(screen.getByRole("region", { name: `Grafo local de ${HOME_ROOT_LABEL}: 2 Nodes e 1 Relation` })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Aproximar" })).not.toBeInTheDocument();
  });

  it("shows the empty state when no Node exists besides the root", async () => {
    homeApi({ graph: () => HttpResponse.json(aLonelyRoot()), nodes: () => HttpResponse.json({ data: [aNode({ id: ACCOUNT_ROOT_ID, isAccountRoot: true })], page: { nextCursor: null } }) });
    renderRoute("/");

    expect(await screen.findByText(HOME_EMPTY_MESSAGE)).toBeInTheDocument();
    expect(screen.queryByRole("application")).not.toBeInTheDocument();
  });

  it("tells Nodes the root does not reach apart from an empty graph", async () => {
    homeApi({
      graph: () => HttpResponse.json(aLonelyRoot()),
      nodes: () => HttpResponse.json({ data: [aNode({ id: ACCOUNT_ROOT_ID, isAccountRoot: true }), aNode()], page: { nextCursor: null } }),
    });
    renderRoute("/");

    expect(await screen.findByText(HOME_UNLINKED_MESSAGE)).toBeInTheDocument();
  });

  it("shows an error, not an empty graph, when the Nodes read fails", async () => {
    homeApi({ graph: () => HttpResponse.json(aLonelyRoot()), nodes: () => problem(500, "unexpected") });
    renderRoute("/");

    expect(await screen.findByRole("alert")).toHaveTextContent(LOAD_ERROR_MESSAGE);
    expect(screen.queryByText(HOME_EMPTY_MESSAGE)).not.toBeInTheDocument();
  });

  it("shows an error with a retry when the Account has no root yet", async () => {
    let missing = true;
    homeApi();
    server.use(http.get("/api/account-root", () => (missing ? problem(422, "graph.root_missing") : HttpResponse.json({ id: ACCOUNT_ROOT_ID }))));
    renderRoute("/");

    expect(await screen.findByRole("alert")).toHaveTextContent(LOAD_ERROR_MESSAGE);

    missing = false;
    await userEvent.click(screen.getByRole("button", { name: RETRY_LABEL }));
    expect(await screen.findByRole("application")).toBeInTheDocument();
  });
  it("links the entry panel to the Inbox, Desancorados, Recent, Agent Changes and the Nodes page", async () => {
    homeApi({ unanchored: unanchoredTotal(3) });
    renderRoute("/");

    const panel = await screen.findByRole("navigation", { name: HOME_SHORTCUTS_LABEL });
    const linkTo = (name: string) => within(panel).getByRole("link", { name });
    expect(linkTo(HOME_INBOX_LINK)).toHaveAttribute("href", HOME_PATHS.inbox);
    expect(await within(panel).findByRole("link", { name: unanchoredLinkText("3") })).toHaveAttribute("href", UNANCHORED_PATH);
    expect(linkTo(HOME_RECENT_LINK)).toHaveAttribute("href", HOME_PATHS.recentChanges);
    expect(linkTo(HOME_AGENT_CHANGES_LINK)).toHaveAttribute("href", HOME_PATHS.agentChanges);
    expect(linkTo(HOME_CREATE_NODE_LINK)).toHaveAttribute("href", HOME_PATHS.nodes);
  });

  it("caps the Desancorados count at 99+", async () => {
    homeApi({ unanchored: unanchoredTotal(150) });
    renderRoute("/");

    expect(await screen.findByRole("link", { name: unanchoredLinkText("99+") })).toBeInTheDocument();
  });

  it("keeps the Desancorados link without a number when its count fails", async () => {
    homeApi({ unanchored: () => problem(500, "unexpected") });
    renderRoute("/");

    const panel = await screen.findByRole("navigation", { name: HOME_SHORTCUTS_LABEL });
    await screen.findByRole("application");
    expect(within(panel).getByRole("link", { name: unanchoredLinkText(undefined) })).toHaveAttribute("href", UNANCHORED_PATH);
  });

  it("opens the agents page from the panel's connect action", async () => {
    homeApi();
    server.use(http.get("/api/agent-identities", () => emptyPage()));
    const { router } = renderRoute("/");

    const panel = await screen.findByRole("navigation", { name: HOME_SHORTCUTS_LABEL });
    expect(within(panel).getByRole("link", { name: HOME_CONNECT_AGENT_LABEL })).toHaveAttribute("href", HOME_PATHS.agents);
    await userEvent.click(within(panel).getByRole("link", { name: HOME_CONNECT_AGENT_LABEL }));
    await waitFor(() => expect(router.state.location.pathname).toBe(HOME_PATHS.agents));
  });

  it("offers to create the first Node or connect an agent when the Account is empty", async () => {
    homeApi({ graph: () => HttpResponse.json(aLonelyRoot()), nodes: () => HttpResponse.json({ data: [aNode({ id: ACCOUNT_ROOT_ID, isAccountRoot: true })], page: { nextCursor: null } }) });
    renderRoute("/");

    await screen.findByText(HOME_EMPTY_MESSAGE);
    const main = screen.getByRole("region", { name: HOME_TITLE });
    const emptyState = screen.getByText(HOME_EMPTY_MESSAGE).parentElement as HTMLElement;
    expect(within(emptyState).getByRole("link", { name: HOME_CREATE_FIRST_NODE_LINK })).toHaveAttribute("href", HOME_PATHS.nodes);
    expect(within(emptyState).getByRole("link", { name: HOME_CONNECT_AGENT_LABEL })).toHaveAttribute("href", HOME_PATHS.agents);
    expect(within(main).getByRole("navigation", { name: HOME_SHORTCUTS_LABEL })).toBeInTheDocument();
  });

  it("leads unlinked Nodes to Desancorados with their count", async () => {
    homeApi({
      graph: () => HttpResponse.json(aLonelyRoot()),
      nodes: () => HttpResponse.json({ data: [aNode({ id: ACCOUNT_ROOT_ID, isAccountRoot: true }), aNode()], page: { nextCursor: null } }),
      unanchored: unanchoredTotal(4),
    });
    renderRoute("/");

    await screen.findByText(HOME_UNLINKED_MESSAGE);
    const unlinked = screen.getByText(HOME_UNLINKED_MESSAGE).parentElement as HTMLElement;
    expect(await within(unlinked).findByRole("link", { name: unanchoredLinkText("4") })).toHaveAttribute("href", UNANCHORED_PATH);
  });

  it("says it is loading while the root is read, with the panel already there", async () => {
    homeApi();
    server.use(http.get("/api/account-root", () => new Promise<Response>(() => undefined)));
    renderRoute("/");

    expect(await screen.findByRole("status")).toHaveTextContent(LOADING_MESSAGE);
    expect(screen.getByRole("navigation", { name: HOME_SHORTCUTS_LABEL })).toBeInTheDocument();
  });

  it("shows an error with a retry when the graph read fails, keeping the panel", async () => {
    let failing = true;
    homeApi({ graph: () => (failing ? problem(500, "unexpected") : HttpResponse.json(aRootGraph())) });
    renderRoute("/");

    expect(await screen.findByRole("alert")).toHaveTextContent(LOAD_ERROR_MESSAGE);
    expect(screen.getByRole("navigation", { name: HOME_SHORTCUTS_LABEL })).toBeInTheDocument();

    failing = false;
    await userEvent.click(screen.getByRole("button", { name: RETRY_LABEL }));
    expect(await screen.findByRole("application")).toBeInTheDocument();
  });

  it("asks for two Nodes, so the root alone never hides the Account's first Node", async () => {
    const probes: (string | null)[] = [];
    homeApi({ graph: () => HttpResponse.json(aLonelyRoot()) });
    server.use(
      http.get("/api/nodes", ({ request }) => {
        const query = new URL(request.url).searchParams;
        if (query.get("unanchored") === "true") return unanchoredTotal(0)();
        probes.push(query.get("limit"));
        return HttpResponse.json({ data: [aNode({ id: ACCOUNT_ROOT_ID, isAccountRoot: true })], page: { nextCursor: null } });
      }),
    );
    renderRoute("/");

    await screen.findByText(HOME_EMPTY_MESSAGE);
    expect(probes).toEqual(["2"]);
    // The server's Desancorados total already leaves the root out: an Account with only its root shows 0.
    expect(await screen.findByRole("link", { name: unanchoredLinkText("0") })).toHaveAttribute("href", UNANCHORED_PATH);
  });

  it("does not read the Nodes page when the root's graph already has a neighbor", async () => {
    let probes = 0;
    homeApi({
      nodes: () => {
        probes += 1;
        return emptyPage();
      },
    });
    renderRoute("/");

    await screen.findByRole("application");
    await screen.findByRole("link", { name: unanchoredLinkText("0") });
    expect(probes).toBe(0);
  });

  it("has no accessibility violations with the graph", async () => {
    homeApi();
    const { container } = renderRoute("/");
    await screen.findByRole("application");
    await screen.findByRole("link", { name: unanchoredLinkText("0") });

    expect(await axe(container)).toHaveNoViolations();
  });

  it("has no accessibility violations when the Account is empty", async () => {
    homeApi({ graph: () => HttpResponse.json(aLonelyRoot()), nodes: () => HttpResponse.json({ data: [aNode({ id: ACCOUNT_ROOT_ID, isAccountRoot: true })], page: { nextCursor: null } }) });
    const { container } = renderRoute("/");
    await screen.findByText(HOME_EMPTY_MESSAGE);
    await screen.findByRole("link", { name: unanchoredLinkText("0") });

    expect(await axe(container)).toHaveNoViolations();
  });

  it("has no accessibility violations when the graph read fails", async () => {
    homeApi({ graph: () => problem(500, "unexpected") });
    const { container } = renderRoute("/");
    await screen.findByRole("alert");

    expect(await axe(container)).toHaveNoViolations();
  });
});

// Route smoke (DA-316): login → "/" → Home with the graph, through MSW. It does not exercise the BFF
// cookie, the dev proxy or real drawing; the real-browser E2E is deferred to its own epic.
describe("home route smoke", () => {
  beforeEach(() => fake.reset());

  it("takes a visitor who logs in to the Home with the Account's graph", async () => {
    homeApi();
    let session: { email: string } | null = null;
    server.use(
      csrfTokenHandler().handler,
      sessionHandler(() => session),
      http.post("/api/sessions", () => {
        session = { email: "ada@example.test" };
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const { router } = renderRoute("/");

    await userEvent.type(await screen.findByLabelText("E-mail"), "ada@example.test");
    await userEvent.type(screen.getByLabelText("Senha"), "correct horse battery staple");
    await userEvent.click(screen.getByRole("button", { name: "Entrar" }));

    expect(await screen.findByRole("heading", { level: 1, name: HOME_TITLE })).toBeInTheDocument();
    expect(await screen.findByRole("application", { name: `Grafo local de ${HOME_ROOT_LABEL}: 2 Nodes e 1 Relation` })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/");
  });
});
