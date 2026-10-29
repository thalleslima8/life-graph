import { beforeEach, describe, expect, it, vi } from "vitest";
import { aRenderableGraphModel } from "@/test/fakeGraphRenderer";
import { NODE_ID, OTHER_NODE_ID, RELATION_ID, TYPE_ID } from "@/test/graphFixtures";
import { RING_SPACING } from "./graphLayout";
import { createSigmaRenderer } from "./graphRenderer";
import type { GraphRendererEvents, GraphRenderView } from "./graphRendererPort";
import { AGENT_EDGE_SCALE, AGENT_RING_GAP, HARD_EDGE_SIZE, SOFT_EDGE_SIZE, UNTYPED_COLOR, nodeSizeOf, typeColorOf } from "./graphVisuals";
import type { RenderableGraph, RenderableNode } from "./graphShape";
import { MAX_ZOOM, MIDDLE_ZOOM, MIN_ZOOM, NEAR_ZOOM } from "./graphView";

type Handler = (payload: Record<string, unknown>) => void;

// jsdom has no WebGL: Sigma is replaced by a stand-in that records what the adapter asks of it.
const sigma = vi.hoisted(() => {
  const state = {
    handlers: new Map<string, Handler>(),
    cameraHandlers: new Map<string, (camera: { ratio: number }) => void>(),
    ratio: 1,
    graph: undefined as unknown,
    container: undefined as unknown,
    settings: undefined as unknown,
    killed: false,
    refreshes: 0,
  };
  const camera = {
    getState: () => ({ x: 0.5, y: 0.5, angle: 0, ratio: state.ratio }),
    setState: (next: { ratio: number }) => {
      state.ratio = next.ratio;
    },
    on: (event: string, handler: (camera: { ratio: number }) => void) => state.cameraHandlers.set(event, handler),
  };
  class FakeSigma {
    constructor(graph: unknown, container: unknown, settings: unknown) {
      Object.assign(state, { graph, container, settings });
    }

    on(event: string, handler: Handler) {
      state.handlers.set(event, handler);
    }

    getCamera() {
      return camera;
    }

    refresh() {
      state.refreshes += 1;
    }

    kill() {
      state.killed = true;
    }
  }
  return { state, FakeSigma };
});
vi.mock("sigma", () => ({ default: sigma.FakeSigma }));
// The curved-edge program needs WebGL; the renderer only hands it to Sigma by name.
vi.mock("@sigma/edge-curve", () => ({ default: function FakeEdgeCurveProgram() {} }));
// Sigma's own label drawing loads the WebGL programs too; only the call is checked.
const discLabel = vi.hoisted(() => ({ draw: vi.fn() }));
vi.mock("sigma/rendering", () => ({ drawDiscNodeLabel: discLabel.draw }));

type DrawnGraph = {
  nodes: () => string[];
  edges: () => string[];
  getNodeAttributes: (id: string) => { x: number; y: number; label: string; size: number; color: string; zIndex: number };
  getEdgeAttributes: (id: string) => { label: string; size: number; type: string; weight: number };
};
type Reducer = (id: string, attributes: Record<string, unknown>) => Record<string, unknown>;
type LabelDrawer = (context: CanvasRenderingContext2D, data: Record<string, unknown>, settings: unknown) => void;
type Settings = { nodeReducer: Reducer; edgeReducer: Reducer; defaultDrawNodeLabel: LabelDrawer; minCameraRatio: number; maxCameraRatio: number; edgeProgramClasses: Record<string, unknown> };

const THIRD_ID = "01920000-0000-7000-8000-000000000003";
const FOURTH_ID = "01920000-0000-7000-8000-000000000004";
const drawn = () => sigma.state.graph as DrawnGraph;
const settings = () => sigma.state.settings as Settings;
const position = (id: string) => {
  const { x, y } = drawn().getNodeAttributes(id);
  return { x, y };
};
const distanceBetween = (from: string, to: string) => Math.hypot(position(from).x - position(to).x, position(from).y - position(to).y);
/** What the drawing shows of a Node or a Relation at the camera's current zoom. */
const shownNode = (id: string) => settings().nodeReducer(id, drawn().getNodeAttributes(id));
const shownEdge = (id: string) => settings().edgeReducer(id, drawn().getEdgeAttributes(id));
const zoomTo = (zoom: number) => {
  sigma.state.ratio = 1 / zoom;
};

function aView(overrides: Partial<GraphRenderView> = {}): GraphRenderView {
  return {
    graph: aRenderableGraphModel(),
    rootId: NODE_ID,
    selectedId: undefined,
    focusedId: undefined,
    highlightAgent: true,
    zoom: 1,
    labels: {
      canvas: "Grafo",
      agentUnreviewed: "mudança de agente não revisada",
      nodeSummary: (node: RenderableNode) => `resumo de ${node.title}`,
    },
    ...overrides,
  };
}

function events(): GraphRendererEvents {
  return { onSelect: vi.fn(), onReroot: vi.fn(), onZoomChange: vi.fn() };
}

/** The model graph plus `extra` Nodes, each one Relation from `from` (the root by default). */
function withNeighbors(extra: { id: string; title: string; distance: number; from?: string; strength?: number; soft?: boolean }[]): RenderableGraph {
  const graph = aRenderableGraphModel();
  const [root, other] = graph.nodes;
  const [edge] = graph.edges;
  if (!root || !other || !edge) {
    throw new Error("The model graph has a root, a neighbor and a Relation.");
  }

  return {
    ...graph,
    nodes: [root, ...extra.map((node) => ({ ...other, id: node.id, title: node.title, distance: node.distance, typeId: null })), other],
    edges: [
      edge,
      ...extra.map((node) => ({
        ...edge,
        id: `${node.id}-edge`,
        sourceNodeId: node.from ?? NODE_ID,
        targetNodeId: node.id,
        kind: "inspira",
        strength: node.strength ?? 1,
        assertion: node.soft ? ("soft" as const) : ("hard" as const),
      })),
    ],
  };
}

describe("sigma renderer", () => {
  beforeEach(() => {
    Object.assign(sigma.state, { handlers: new Map(), cameraHandlers: new Map(), ratio: 1, killed: false, refreshes: 0 });
    discLabel.draw.mockClear();
  });

  it("draws each Node and Relation by id, the root at the centre, sized by relevance and coloured by Type", () => {
    const graph = aRenderableGraphModel();
    const container = document.createElement("div");

    createSigmaRenderer().mount(container, aView({ graph: { ...graph, nodes: graph.nodes.map((node) => ({ ...node, degree: node.id === OTHER_NODE_ID ? 40 : 1 })) } }), events());

    expect(sigma.state.container).toBe(container);
    expect([drawn().nodes(), drawn().edges()]).toEqual([[NODE_ID, OTHER_NODE_ID], [RELATION_ID]]);
    expect(position(NODE_ID)).toEqual({ x: 0, y: 0 });
    expect([drawn().getNodeAttributes(NODE_ID).size, drawn().getNodeAttributes(OTHER_NODE_ID).size]).toEqual([nodeSizeOf(1, 0), nodeSizeOf(40, 1)]);
    expect([drawn().getNodeAttributes(NODE_ID).color, drawn().getNodeAttributes(OTHER_NODE_ID).color]).toEqual([UNTYPED_COLOR, typeColorOf(TYPE_ID)]);
    expect(drawn().getNodeAttributes(NODE_ID).zIndex).toBeGreaterThan(drawn().getNodeAttributes(OTHER_NODE_ID).zIndex);
  });

  it("pulls the Nodes of a stronger Relation closer, the same way every time", () => {
    const graph = withNeighbors([
      { id: THIRD_ID, title: "Estoicismo", distance: 1, strength: 1 },
      { id: FOURTH_ID, title: "Epicuro", distance: 1, strength: 0.1 },
    ]);
    createSigmaRenderer().mount(document.createElement("div"), aView({ graph }), events());
    const first = [position(THIRD_ID), position(FOURTH_ID)];

    expect(distanceBetween(NODE_ID, THIRD_ID)).toBeLessThan(distanceBetween(NODE_ID, FOURTH_ID));
    createSigmaRenderer().mount(document.createElement("div"), aView({ graph }), events());
    expect([position(THIRD_ID), position(FOURTH_ID)]).toEqual(first);
  });

  it("keeps the Nodes still there in place, settles a new one near its neighbor and drops a gone one", () => {
    const renderer = createSigmaRenderer();
    renderer.mount(document.createElement("div"), aView(), events());
    const before = [position(NODE_ID), position(OTHER_NODE_ID)];

    renderer.update(aView({ graph: withNeighbors([{ id: THIRD_ID, title: "Estoicismo", distance: 2, from: OTHER_NODE_ID }]) }));
    expect([position(NODE_ID), position(OTHER_NODE_ID)]).toEqual(before);
    expect(distanceBetween(THIRD_ID, OTHER_NODE_ID)).toBeLessThan(RING_SPACING);

    renderer.update(aView());
    expect([drawn().nodes(), drawn().edges()]).toEqual([[NODE_ID, OTHER_NODE_ID], [RELATION_ID]]);
  });

  it("draws a Hard Relation straight and thick, a Soft one curved and thinner", () => {
    createSigmaRenderer().mount(document.createElement("div"), aView({ graph: withNeighbors([{ id: THIRD_ID, title: "Estoicismo", distance: 1, soft: true }]) }), events());

    expect([drawn().getEdgeAttributes(RELATION_ID), drawn().getEdgeAttributes(`${THIRD_ID}-edge`)].map(({ type, size }) => ({ type, size }))).toEqual([
      { type: "line", size: HARD_EDGE_SIZE },
      { type: "curved", size: SOFT_EDGE_SIZE },
    ]);
    expect(Object.keys(settings().edgeProgramClasses)).toEqual(["curved"]);
  });

  it("shows more as the zoom gets closer: titles near the root, then every title, then summaries and Relation kinds", () => {
    const graph = withNeighbors([{ id: THIRD_ID, title: "Estoicismo", distance: 2, from: OTHER_NODE_ID }]);
    const flagged = { ...graph, nodes: graph.nodes.map((node) => ({ ...node, agentUnreviewed: node.id === OTHER_NODE_ID })) };
    createSigmaRenderer().mount(document.createElement("div"), aView({ graph: flagged }), events());
    const labels = () => [NODE_ID, OTHER_NODE_ID, THIRD_ID].map((id) => shownNode(id).label);

    expect([labels(), shownEdge(RELATION_ID).label]).toEqual([["Leituras", "Meditações · mudança de agente não revisada", null], null]);
    zoomTo(MIDDLE_ZOOM);
    expect([labels(), shownEdge(RELATION_ID).label]).toEqual([["Leituras", "Meditações · mudança de agente não revisada", "Estoicismo"], null]);
    zoomTo(NEAR_ZOOM);
    expect([labels(), shownEdge(RELATION_ID).label]).toEqual([
      ["Leituras · resumo de Leituras", "Meditações · resumo de Meditações · mudança de agente não revisada", "Estoicismo · resumo de Estoicismo"],
      "cita",
    ]);
  });

  it("marks what an agent changed and nobody reviewed by a ring, a thicker Relation and text, at every zoom", () => {
    const graph = withNeighbors([{ id: THIRD_ID, title: "Estoicismo", distance: 2, from: OTHER_NODE_ID }]);
    const marked = {
      ...graph,
      nodes: graph.nodes.map((node) => ({ ...node, agentUnreviewed: node.id === THIRD_ID })),
      edges: graph.edges.map((edge) => ({ ...edge, agentUnreviewed: edge.id === `${THIRD_ID}-edge` })),
    };
    createSigmaRenderer().mount(document.createElement("div"), aView({ graph: marked }), events());
    const ring = { beginPath: vi.fn(), arc: vi.fn(), stroke: vi.fn(), lineWidth: 0, strokeStyle: "" };
    const drawLabel = (key: string) =>
      settings().defaultDrawNodeLabel(ring as unknown as CanvasRenderingContext2D, { key, x: 10, y: 20, size: 5, label: key, color: "#000" }, {});

    expect(shownNode(THIRD_ID)).toMatchObject({ label: "Estoicismo · mudança de agente não revisada", forceLabel: true });
    expect(shownEdge(`${THIRD_ID}-edge`)).toMatchObject({ label: "inspira · mudança de agente não revisada", size: HARD_EDGE_SIZE * AGENT_EDGE_SCALE, forceLabel: true });
    expect(shownEdge(RELATION_ID)).toMatchObject({ label: null, size: HARD_EDGE_SIZE });
    drawLabel(OTHER_NODE_ID);
    expect(ring.arc).not.toHaveBeenCalled();
    drawLabel(THIRD_ID);
    expect(ring.arc).toHaveBeenCalledExactlyOnceWith(10, 20, 5 + AGENT_RING_GAP, 0, 2 * Math.PI);
    expect(ring.stroke).toHaveBeenCalledOnce();
    expect(discLabel.draw).toHaveBeenCalledTimes(2);
  });

  it("marks nothing when the view does not ask for the agent highlight", () => {
    const graph = withNeighbors([{ id: THIRD_ID, title: "Estoicismo", distance: 2, from: OTHER_NODE_ID }]);
    const marked = {
      ...graph,
      nodes: graph.nodes.map((node) => ({ ...node, agentUnreviewed: true })),
      edges: graph.edges.map((edge) => ({ ...edge, agentUnreviewed: true })),
    };
    createSigmaRenderer().mount(document.createElement("div"), aView({ graph: marked, highlightAgent: false }), events());
    const ring = { beginPath: vi.fn(), arc: vi.fn(), stroke: vi.fn(), lineWidth: 0, strokeStyle: "" };

    settings().defaultDrawNodeLabel(ring as unknown as CanvasRenderingContext2D, { key: THIRD_ID, x: 0, y: 0, size: 5, label: "", color: "#000" }, {});

    expect(shownNode(THIRD_ID)).toMatchObject({ label: null, forceLabel: false });
    expect(shownEdge(RELATION_ID)).toMatchObject({ label: null, size: HARD_EDGE_SIZE });
    expect(ring.arc).not.toHaveBeenCalled();
  });

  it("selects on click, re-roots on double click instead of zooming, and reports the zoom", () => {
    const handlers = events();
    createSigmaRenderer().mount(document.createElement("div"), aView(), handlers);
    const preventSigmaDefault = vi.fn();

    sigma.state.handlers.get("clickNode")?.({ node: OTHER_NODE_ID });
    sigma.state.handlers.get("doubleClickNode")?.({ node: OTHER_NODE_ID, preventSigmaDefault });
    sigma.state.cameraHandlers.get("updated")?.({ ratio: 0.5 });

    expect([handlers.onSelect, handlers.onReroot, handlers.onZoomChange].map((handler) => vi.mocked(handler).mock.calls)).toEqual([
      [[OTHER_NODE_ID]],
      [[OTHER_NODE_ID]],
      [[2]],
    ]);
    expect(preventSigmaDefault).toHaveBeenCalledOnce();
  });

  it("follows the page's zoom within its bounds, labels the selected Node even far away and stops drawing when disposed of", () => {
    const renderer = createSigmaRenderer();
    const deeper = withNeighbors([{ id: THIRD_ID, title: "Estoicismo", distance: 2, from: OTHER_NODE_ID }]);
    renderer.mount(document.createElement("div"), aView({ graph: deeper, zoom: 2 }), events());
    expect([sigma.state.ratio, settings().minCameraRatio, settings().maxCameraRatio]).toEqual([0.5, 1 / MAX_ZOOM, 1 / MIN_ZOOM]);

    renderer.update(aView({ graph: deeper, zoom: 1, selectedId: THIRD_ID }));
    const selected = shownNode(THIRD_ID);
    const other = shownNode(OTHER_NODE_ID);
    renderer.dispose();

    expect([sigma.state.ratio, sigma.state.refreshes, sigma.state.killed]).toEqual([1, 1, true]);
    expect([selected.label, selected.highlighted, selected.forceLabel]).toEqual(["Estoicismo", true, true]);
    expect([other.highlighted, other.forceLabel]).toEqual([false, false]);
  });

  it("names and boxes the Node the keyboard is on, even far away", () => {
    const renderer = createSigmaRenderer();
    const deeper = withNeighbors([{ id: THIRD_ID, title: "Estoicismo", distance: 2, from: OTHER_NODE_ID }]);
    renderer.mount(document.createElement("div"), aView({ graph: deeper }), events());

    renderer.update(aView({ graph: deeper, focusedId: THIRD_ID }));
    const focused = shownNode(THIRD_ID);
    renderer.update(aView({ graph: deeper, focusedId: undefined }));
    const unfocused = shownNode(THIRD_ID);

    expect([focused.label, focused.highlighted, focused.forceLabel]).toEqual(["Estoicismo", true, true]);
    expect([unfocused.label, unfocused.highlighted]).toEqual([null, false]);
  });
});
