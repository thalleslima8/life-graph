import { act, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { axe } from "vitest-axe";
import { aLocalGraphModel, fakeGraphRenderer as fake } from "@/test/fakeGraphRenderer";
import { aChangeSet, aFeed, aLocalGraph, aNode, aType, NODE_ID, ontologyHandlers, OTHER_NODE_ID, RELATION_ID } from "@/test/graphFixtures";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";
import { graphKeys, type NodeDetail } from "./graphData";
import { LEGEND_AGENT_TEXT } from "./graphMessages";
import { graphPath, INITIAL_ZOOM, MAX_ZOOM, MIN_ZOOM, readLastFocus, steppedZoom } from "./localGraph";
import type { ChangeSetFeed } from "./recentChanges";

// jsdom has no WebGL: the page draws through the fake renderer (DA-137).
vi.mock("./graphRenderer", async () => ({ createSigmaRenderer: (await import("@/test/fakeGraphRenderer")).fakeGraphRenderer.factory }));

const THIRD_ID = "01920000-0000-7000-8000-000000000003";

/** The default graph plus a Node two Relations from the root. */
function aDeeperGraph() {
  const graph = aLocalGraph();
  return aLocalGraph({
    nodes: [...graph.nodes, { id: THIRD_ID, title: "Estoicismo", typeId: null, distance: 2, degree: 1, agentUnreviewed: false }],
    edges: [...graph.edges, { ...graph.edges[0], id: "01920000-0000-7000-8000-0000000000f2", sourceNodeId: OTHER_NODE_ID, targetNodeId: THIRD_ID } as (typeof graph.edges)[number]],
  });
}

type GraphApiOptions = {
  /** The Nodes the Inspector reads; any other one is a 404. */
  nodes?: () => NodeDetail[];
  /** The ChangeSet feed, whose newest cursor the probe starts from. */
  feed?: () => ChangeSetFeed;
  /** What the probe finds after `since`. */
  probe?: (since: string | null) => ChangeSetFeed;
};

const INSPECTED_NODES = [aNode(), aNode({ id: OTHER_NODE_ID, title: "Meditações" }), aNode({ id: THIRD_ID, title: "Estoicismo" })];

/**
 * Serves the local graph from `respond` and records each request as `{nodeId}?depth={depth}`;
 * also the Inspector's reads and the ChangeSet probe, quiet unless the options say otherwise.
 */
function graphApi(respond: (depth: string | null, nodeId: string) => Response | Promise<Response>, options: GraphApiOptions = {}) {
  const { nodes = () => INSPECTED_NODES, feed = () => aFeed([], null), probe = () => aFeed([], null) } = options;
  const asked: string[] = [];
  server.use(
    sessionHandler(() => ({ email: "ada@example.test" })),
    csrfTokenHandler().handler,
    ...ontologyHandlers({ types: [aType()] }),
    http.get("/api/changesets", ({ request }) => {
      const query = new URL(request.url).searchParams;
      return HttpResponse.json(query.has("limit") ? probe(query.get("since")) : feed());
    }),
    http.get("/api/nodes", () => HttpResponse.json({ data: [], page: { nextCursor: null } })),
    http.get("/api/nodes/:nodeId", ({ params }) => {
      const node = nodes().find((candidate) => candidate.id === params.nodeId);
      return node ? HttpResponse.json(node) : problem(404, "graph.node_not_found");
    }),
    http.get("/api/nodes/:nodeId/relations", () => HttpResponse.json({ data: [], page: { nextCursor: null } })),
    http.get("/api/nodes/:nodeId/graph", ({ request, params }) => {
      const depth = new URL(request.url).searchParams.get("depth");
      const nodeId = String(params.nodeId);
      asked.push(`${nodeId}?depth=${String(depth)}`);
      return respond(depth, nodeId);
    }),
  );
  return asked;
}

/** Where focus comes back when the Inspector closes or its Node goes away. */
const graphViewArea = () => screen.getByText(/^Teclado:/).closest("[tabindex='-1']");

describe("local graph page", () => {
  beforeEach(() => fake.reset());

  it("draws the graph around the Node at depth 2 when the URL names none", async () => {
    const asked = graphApi(() => HttpResponse.json(aLocalGraph()));
    renderRoute(graphPath(NODE_ID));

    expect(await screen.findByRole("heading", { level: 1, name: "Grafo de Leituras" })).toBeInTheDocument();
    expect(screen.getByRole("application", { name: "Grafo local de Leituras: 2 Nodes e 1 Relation" })).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "2" })).toBeChecked();
    expect(asked).toEqual([`${NODE_ID}?depth=2`]);
    expect(fake.recorded.mounts.map((mount) => mount.view.graph.edges.map((edge) => edge.id))).toEqual([[RELATION_ID]]);
  });

  it("remembers the Node it opened at as the last focus of this browser", async () => {
    window.localStorage.clear();
    graphApi(() => HttpResponse.json(aLocalGraph()));
    renderRoute(graphPath(NODE_ID));

    await screen.findByRole("heading", { level: 1, name: "Grafo de Leituras" });
    expect(readLastFocus()).toBe(NODE_ID);
  });

  it("changes the depth in the URL and keeps the graph on screen, busy, until the deeper one arrives", async () => {
    let release = () => {};
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    graphApi(async (depth) => {
      if (depth === "3") {
        await gate;
        return HttpResponse.json(aDeeperGraph());
      }

      return HttpResponse.json(aLocalGraph());
    });
    const { router } = renderRoute(graphPath(NODE_ID));
    const page = await screen.findByRole("region", { name: "Grafo de Leituras" });

    await userEvent.click(screen.getByRole("radio", { name: "3" }));

    expect(router.state.location.search).toBe("?depth=3");
    expect(await screen.findByText("Atualizando…")).toBeInTheDocument();
    expect(page).toHaveAttribute("aria-busy", "true");
    expect(screen.getByRole("application")).toBeInTheDocument();
    release();
    await waitFor(() => expect(page).toHaveAttribute("aria-busy", "false"));
    expect(fake.recorded.mounts).toHaveLength(1);
    expect(fake.recorded.updates.at(-1)?.graph.nodes.map((node) => node.id)).toEqual([NODE_ID, OTHER_NODE_ID, THIRD_ID]);
  });

  it("says how much the caps left out", async () => {
    graphApi(() => HttpResponse.json(aLocalGraph({ truncated: true, omittedNodeCount: 1200, omittedEdgeCount: 1 })));
    renderRoute(graphPath(NODE_ID));

    expect(await screen.findByText("1.200 Nodes omitidos e 1 Relation omitida. Centralize em outro Node para ver mais.")).toBeInTheDocument();
  });

  it("opens a selected Node in the Inspector beside the graph with focus on it, and re-roots on it from there", async () => {
    graphApi((_depth, nodeId) => HttpResponse.json(nodeId === NODE_ID ? aLocalGraph() : aLocalGraph({ rootNodeId: OTHER_NODE_ID })));
    const { router } = renderRoute(`${graphPath(NODE_ID)}?depth=3`);
    await screen.findByRole("heading", { level: 1, name: "Grafo de Leituras" });

    act(() => fake.events().onSelect(OTHER_NODE_ID));
    const panel = await screen.findByRole("complementary", { name: "Meditações" });
    expect(within(panel).getByRole("heading", { level: 2, name: "Meditações" })).toHaveFocus();
    expect(await within(panel).findByRole("form", { name: "Editar" })).toBeInTheDocument();
    expect([router.state.location.search, router.state.historyAction]).toEqual([`?depth=3&selected=${OTHER_NODE_ID}`, "REPLACE"]);

    await userEvent.click(within(panel).getByRole("button", { name: "Centralizar aqui" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Grafo de Meditações" })).toBeInTheDocument();
    expect([router.state.location.pathname, router.state.location.search]).toEqual([graphPath(OTHER_NODE_ID), "?depth=3"]);

    act(() => fake.events().onReroot(NODE_ID));
    expect(await screen.findByRole("heading", { level: 1, name: "Grafo de Leituras" })).toBeInTheDocument();
  });

  it("says a Node that is not there is not found, whatever the cause", async () => {
    graphApi(() => problem(404, "graph.node_not_found"));
    renderRoute(graphPath(NODE_ID));

    expect(await screen.findByRole("heading", { level: 1, name: "Node não encontrado" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Voltar para os Nodes" })).toHaveAttribute("href", "/nodes");
  });

  it("says when the graph could not load, and retries", async () => {
    const api = { failing: true };
    graphApi(() => (api.failing ? problem(500, "unexpected") : HttpResponse.json(aLocalGraph())));
    renderRoute(graphPath(NODE_ID));

    expect(await screen.findByRole("alert")).toHaveTextContent("Não foi possível carregar. Tente novamente em instantes.");
    api.failing = false;
    await userEvent.click(screen.getByRole("button", { name: "Tentar novamente" }));

    expect(await screen.findByRole("heading", { level: 1, name: "Grafo de Leituras" })).toBeInTheDocument();
  });

  it("invites to relate a Node that has no Relations yet, drawing nothing", async () => {
    const graph = aLocalGraph();
    graphApi(() => HttpResponse.json(aLocalGraph({ nodes: graph.nodes.slice(0, 1), edges: [] })));
    renderRoute(graphPath(NODE_ID));

    expect(await screen.findByText("Este Node ainda não tem Relations.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Criar Relation" })).toHaveAttribute("href", `/nodes/${NODE_ID}`);
    expect(screen.queryByRole("application")).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: /^Grafo local/ })).not.toBeInTheDocument();
  });

  it("falls back on the list when the browser cannot draw the graph, and stays there when Back returns to the drawing", async () => {
    fake.recorded.failMount = true;
    graphApi(() => HttpResponse.json(aLocalGraph()));
    const { router } = renderRoute(graphPath(NODE_ID));

    expect(await screen.findByRole("alert")).toHaveTextContent("Não foi possível desenhar o grafo; mostrando a lista.");
    expect(screen.getByRole("region", { name: "Grafo local de Leituras: 2 Nodes e 1 Relation" })).toBeInTheDocument();
    expect([router.state.location.search, screen.getByRole("radio", { name: "Lista" })]).toEqual(["?view=list", expect.objectContaining({ checked: true })]);
    expect(screen.queryByRole("button", { name: "Aproximar" })).not.toBeInTheDocument();

    await act(() => router.navigate(-1));
    expect(router.state.location.search).toBe("");
    expect(screen.getByRole("region", { name: /^Grafo local de Leituras/ })).toBeInTheDocument();
    expect(fake.recorded.mounts).toHaveLength(0);
  });

  it("falls back on the list when drawing fails after the graph was shown", async () => {
    graphApi(() => HttpResponse.json(aLocalGraph()));
    renderRoute(graphPath(NODE_ID));
    await screen.findByRole("application");

    fake.recorded.failUpdate = true;
    await userEvent.click(screen.getByRole("button", { name: "Aproximar" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Não foi possível desenhar o grafo; mostrando a lista.");
    expect(screen.queryByRole("application")).not.toBeInTheDocument();
  });

  it("switches between the drawing and the list in the URL, sharing the selection and the re-root", async () => {
    const user = userEvent.setup();
    graphApi((_depth, nodeId) => HttpResponse.json(nodeId === NODE_ID ? aLocalGraph() : aLocalGraph({ rootNodeId: OTHER_NODE_ID })));
    const { router } = renderRoute(`${graphPath(NODE_ID)}?depth=3`);
    await screen.findByRole("application");

    await user.click(screen.getByRole("radio", { name: "Lista" }));
    expect(router.state.location.search).toBe("?depth=3&view=list");
    const list = await screen.findByRole("region", { name: "Grafo local de Leituras: 2 Nodes e 1 Relation" });
    expect([screen.queryByRole("application"), screen.queryByRole("region", { name: "Legenda" })]).toEqual([null, null]);

    await user.click(within(list).getByRole("button", { name: "Meditações" }));
    const panel = await screen.findByRole("complementary", { name: "Meditações" });
    expect(within(panel).getByRole("heading", { level: 2, name: "Meditações" })).toHaveFocus();
    expect(within(list).getByRole("button", { name: "Meditações" })).toHaveAttribute("aria-current", "true");

    await user.click(within(list).getByRole("button", { name: "Centralizar aqui: Meditações" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Grafo de Meditações" })).toBeInTheDocument();
    expect([router.state.location.pathname, router.state.location.search]).toEqual([graphPath(OTHER_NODE_ID), "?depth=3&view=list"]);

    await user.click(screen.getByRole("radio", { name: "Desenho" }));
    expect(await screen.findByRole("application")).toBeInTheDocument();
    expect(router.state.location.search).toBe("?depth=3");
  });

  it("steps the zoom within its bounds, follows the drawing's own zoom and says what each level shows", async () => {
    const user = userEvent.setup();
    graphApi(() => HttpResponse.json(aLocalGraph()));
    renderRoute(graphPath(NODE_ID));
    const zoomIn = await screen.findByRole("button", { name: "Aproximar" });
    const zoomOut = screen.getByRole("button", { name: "Afastar" });
    const drawnZoom = () => fake.recorded.updates.at(-1)?.zoom;

    expect(screen.getByText("Zoom afastado: títulos só na raiz e nos vizinhos diretos.")).toHaveAttribute("role", "status");
    await user.click(zoomIn);
    expect([drawnZoom(), screen.getByText("Zoom médio: todos os títulos, com a cor do Type.")]).toEqual([1.5, expect.anything()]);
    await user.click(zoomIn);
    await user.click(zoomIn);
    expect(screen.getByText("Zoom próximo: resumo de cada Node e o tipo de cada Relation.")).toBeInTheDocument();

    act(() => fake.events().onZoomChange(MAX_ZOOM));
    expect([zoomIn, zoomOut].map((button) => (button as HTMLButtonElement).disabled)).toEqual([true, false]);
    act(() => fake.events().onZoomChange(MIN_ZOOM));
    expect([zoomIn, zoomOut].map((button) => (button as HTMLButtonElement).disabled)).toEqual([false, true]);
    await user.click(screen.getByRole("button", { name: "Restaurar zoom" }));
    expect([drawnZoom(), screen.getByText("Zoom afastado: títulos só na raiz e nos vizinhos diretos.")]).toEqual([1, expect.anything()]);
  });

  it("explains sizes, lines and Type colours in a legend, and summarises each Node by its Type", async () => {
    graphApi(() => HttpResponse.json(aLocalGraph()));
    renderRoute(graphPath(NODE_ID));
    const legend = await screen.findByRole("region", { name: "Legenda" });
    const [root, other] = aLocalGraphModel().nodes;
    const summaries = () => {
      const labels = (fake.recorded.updates.at(-1) ?? fake.recorded.mounts.at(-1)?.view)?.labels;
      return [root, other].map((node) => (node && labels ? labels.nodeSummary(node) : undefined));
    };

    await waitFor(() => expect(legend).toHaveTextContent("Livro"));
    expect(legend).toHaveTextContent("Sem Type");
    expect(legend).toHaveTextContent("Hard Relation: linha reta");
    expect(legend).not.toHaveTextContent("Soft Relation");
    await waitFor(() => expect(summaries()).toEqual(["Sem Type, 1 Relation", "Livro, 1 Relation"]));
  });

  it("names the Soft line in the legend when the graph has a Soft Relation", async () => {
    const graph = aLocalGraph();
    graphApi(() => HttpResponse.json({ ...graph, edges: graph.edges.map((edge) => ({ ...edge, assertion: "soft" as const })) }));
    renderRoute(graphPath(NODE_ID));

    expect(await screen.findByRole("region", { name: "Legenda" })).toHaveTextContent("Soft Relation: linha curva e mais fina");
  });

  it("closes the Inspector, dropping the selection and bringing focus back to the graph", async () => {
    graphApi(() => HttpResponse.json(aLocalGraph()));
    const { router } = renderRoute(`${graphPath(NODE_ID)}?selected=${OTHER_NODE_ID}`);
    const panel = await screen.findByRole("complementary", { name: "Meditações" });

    await userEvent.click(within(panel).getByRole("button", { name: "Fechar" }));

    await waitFor(() => expect(screen.queryByRole("complementary", { name: "Meditações" })).not.toBeInTheDocument());
    expect(router.state.location.search).toBe("");
    expect(graphViewArea()).toHaveFocus();
  });

  it("opens the Inspector from the list with Enter and brings focus back to the list when it closes", async () => {
    const user = userEvent.setup();
    graphApi(() => HttpResponse.json(aLocalGraph()));
    renderRoute(`${graphPath(NODE_ID)}?view=list`);
    const list = await screen.findByRole("region", { name: "Grafo local de Leituras: 2 Nodes e 1 Relation" });

    within(list).getByRole("button", { name: "Leituras" }).focus();
    await user.keyboard("{ArrowRight}{Enter}");
    const panel = await screen.findByRole("complementary", { name: "Meditações" });
    expect(within(panel).getByRole("heading", { level: 2, name: "Meditações" })).toHaveFocus();
    await user.click(within(panel).getByRole("button", { name: "Fechar" }));

    await waitFor(() => expect(screen.queryByRole("complementary")).not.toBeInTheDocument());
    expect(graphViewArea()).toHaveFocus();
    expect(graphViewArea()).toContainElement(screen.getByRole("region", { name: /^Grafo local de Leituras/ }));
  });

  it("shows the Inspector's gone state when the selected Node goes away while the graph still has it", async () => {
    let nodes = INSPECTED_NODES;
    graphApi(() => HttpResponse.json(aLocalGraph()), { nodes: () => nodes });
    const { queryClient } = renderRoute(`${graphPath(NODE_ID)}?selected=${OTHER_NODE_ID}`);
    await screen.findByRole("complementary", { name: "Meditações" });

    nodes = [];
    await act(() => queryClient.invalidateQueries({ queryKey: graphKeys.all }));

    const panel = await screen.findByRole("complementary", { name: "Node indisponível" });
    expect(within(panel).getByRole("link", { name: "Desfazer em Recent Changes" })).toHaveAttribute("href", "/changes");
  });

  it("clears a selected Node a fresh read of the graph no longer has, says so and brings focus back to the graph", async () => {
    let graph = aDeeperGraph();
    graphApi(() => HttpResponse.json(graph));
    const { router, queryClient } = renderRoute(graphPath(NODE_ID));
    await screen.findByRole("heading", { level: 1, name: "Grafo de Leituras" });
    act(() => fake.events().onSelect(THIRD_ID));
    await screen.findByRole("complementary", { name: "Estoicismo" });

    graph = aLocalGraph();
    await act(() => queryClient.invalidateQueries({ queryKey: graphKeys.all }));

    expect(await screen.findByText("O Node selecionado não existe mais.")).toBeInTheDocument();
    expect([router.state.location.search, router.state.historyAction]).toEqual(["", "REPLACE"]);
    await waitFor(() => expect(screen.queryByRole("complementary", { name: "Estoicismo" })).not.toBeInTheDocument());
    expect(graphViewArea()).toHaveFocus();
  });

  it("announces news from agents and draws it only on Atualizar, keeping the zoom and marking what the agent changed", async () => {
    const agentChange = aChangeSet({ actorKind: "agent_identity", channel: "mcp" });
    let feed = aFeed([], "cursor-1");
    let graph = aLocalGraph();
    const asked = graphApi(() => HttpResponse.json(graph), {
      feed: () => feed,
      probe: (since) => (since === "cursor-1" ? aFeed([agentChange], "cursor-2") : aFeed([], "cursor-2")),
    });
    renderRoute(graphPath(NODE_ID));
    await screen.findByRole("heading", { level: 1, name: "Grafo de Leituras" });
    await userEvent.click(screen.getByRole("button", { name: "Aproximar" }));

    const refresh = await screen.findByRole("button", { name: "Atualizar" });
    expect(screen.getByText("Há novidades de agentes.").closest("[role=status]")).not.toBeNull();
    // Nothing is read again on its own: the layout stays still while the person inspects.
    expect(asked).toHaveLength(1);

    const deeper = aDeeperGraph();
    graph = { ...deeper, nodes: deeper.nodes.map((node) => ({ ...node, agentUnreviewed: node.id === THIRD_ID })) };
    feed = aFeed([agentChange], "cursor-2");
    await userEvent.click(refresh);

    await waitFor(() => expect(fake.recorded.updates.at(-1)?.graph.nodes.map((node) => node.id)).toContain(THIRD_ID));
    expect(fake.recorded.updates.at(-1)?.zoom).toBe(steppedZoom(INITIAL_ZOOM, "in"));
    expect(screen.getByText(LEGEND_AGENT_TEXT)).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole("button", { name: "Atualizar" })).not.toBeInTheDocument());

    act(() => fake.events().onSelect(THIRD_ID));
    const panel = await screen.findByRole("complementary", { name: "Estoicismo" });
    expect(within(panel).getByText(/Este Node tem uma mudança de agente não revisada/)).toBeInTheDocument();
    expect(within(panel).getByRole("link", { name: "Revisar em Agent Changes" })).toHaveAttribute("href", "/agent-changes?unreviewed=1");
  });

  it("has no accessibility violations in the list", async () => {
    graphApi(() => HttpResponse.json(aLocalGraph()));
    const { container } = renderRoute(`${graphPath(NODE_ID)}?view=list&selected=${OTHER_NODE_ID}`);
    await screen.findByRole("button", { name: "Centralizar aqui" });

    expect(await axe(container)).toHaveNoViolations();
  });

  it("has no accessibility violations", async () => {
    const graph = aLocalGraph();
    graphApi(() => HttpResponse.json({ ...graph, nodes: graph.nodes.map((node) => ({ ...node, agentUnreviewed: node.id === OTHER_NODE_ID })) }), {
      probe: () => aFeed([aChangeSet()], "cursor-2"),
    });
    const { container } = renderRoute(`${graphPath(NODE_ID)}?selected=${OTHER_NODE_ID}`);
    await screen.findByRole("button", { name: "Centralizar aqui" });
    await screen.findByRole("button", { name: "Atualizar" });

    expect(await axe(container)).toHaveNoViolations();
  });
});
