import EdgeCurveProgram from "@sigma/edge-curve";
import { MultiDirectedGraph } from "graphology";
import forceAtlas2 from "graphology-layout-forceatlas2";
import Sigma from "sigma";
import { drawDiscNodeLabel, type NodeLabelDrawingFunction } from "sigma/rendering";
import type { EdgeDisplayData, NodeDisplayData } from "sigma/types";
import { placeNodes, type Point } from "./graphLayout";
import type { GraphRenderer, GraphRendererEvents, GraphRenderView } from "./graphRendererPort";
import {
  AGENT_EDGE_SCALE,
  AGENT_RING_COLOR,
  AGENT_RING_GAP,
  AGENT_RING_WIDTH,
  EDGE_COLOR,
  edgeStyleOf,
  nodeSizeOf,
  typeColorOf,
  type EdgeLine,
} from "./graphVisuals";
import type { RenderableNode } from "./graphShape";
import { levelOfDetail, MAX_ZOOM, MIN_ZOOM } from "./graphView";

/**
 * The Sigma.js adapter of the GraphRenderer port (DA-137): the only file that imports the
 * library (an architecture test holds it). The graph lives here, outside React state (DA-052).
 * Positions come from ForceAtlas2 weighted by each Relation's strength, so related Nodes end
 * close; it has no randomness, so the seeded start gives the same layout (GEN-072). What each
 * Node shows follows the Semantic Zoom (DA-140), decided at each frame from the camera.
 */

type NodeAttributes = Point & { label: string; size: number; color: string; zIndex: number; fixed?: boolean };
type EdgeAttributes = { label: string; size: number; color: string; type: string; weight: number };

/** ForceAtlas2 passes run on each layout: enough for a few hundred Nodes to settle. */
const LAYOUT_ITERATIONS = 100;
/** How strongly a Relation's strength pulls its Nodes together (ForceAtlas2's edge weight influence). */
const STRENGTH_INFLUENCE = 1;
/** Sigma's edge program for each line of graphVisuals. */
const EDGE_PROGRAMS: Record<EdgeLine, string> = { straight: "line", curved: "curved" };
/** A zoom this close to the camera's is the same: the camera is not moved again for a rounding error. */
const ZOOM_TOLERANCE = 1e-3;
const SUMMARY_SEPARATOR = " · ";
const FULL_TURN = 2 * Math.PI;

/**
 * Sigma draws each shown label with this; it gets the Node's key too. A Node an agent changed
 * and nobody reviewed gets a ring first (shape, not colour alone: FE-034), then the usual label.
 */
export function drawLabelWithAgentRing(isMarked: (nodeId: string) => boolean): NodeLabelDrawingFunction<NodeAttributes, EdgeAttributes> {
  return (context, data, settings) => {
    const nodeId = "key" in data ? data.key : undefined;
    if (typeof nodeId === "string" && isMarked(nodeId)) {
      context.beginPath();
      context.arc(data.x, data.y, data.size + AGENT_RING_GAP, 0, FULL_TURN);
      context.lineWidth = AGENT_RING_WIDTH;
      context.strokeStyle = AGENT_RING_COLOR;
      context.stroke();
    }

    drawDiscNodeLabel(context, data, settings);
  };
}

export function createSigmaRenderer(): GraphRenderer {
  const graph = new MultiDirectedGraph<NodeAttributes, EdgeAttributes>();
  const nodesById = new Map<string, RenderableNode>();
  // A Relation shows what its nearer end shows.
  const edgeDistances = new Map<string, number>();
  const markedEdges = new Set<string>();
  let sigma: Sigma<NodeAttributes, EdgeAttributes> | undefined;
  let current: GraphRenderView | undefined;

  function draw(view: GraphRenderView) {
    const placed = new Map(graph.mapNodes((id, attributes) => [id, { x: attributes.x, y: attributes.y }] as const));
    const positions = placeNodes(view.graph, view.rootId, placed);
    const kept = new Set(view.graph.nodes.map((node) => node.id));
    graph.forEachNode((id) => {
      if (!kept.has(id)) {
        graph.dropNode(id);
      }
    });
    nodesById.clear();
    for (const node of view.graph.nodes) {
      nodesById.set(node.id, node);
      graph.mergeNode(node.id, {
        ...(positions.get(node.id) ?? { x: 0, y: 0 }),
        label: node.title,
        size: nodeSizeOf(node.degree ?? 0, node.distance),
        color: typeColorOf(node.typeId !== undefined ? node.typeId : node.typeName),
        // Nearer Nodes are drawn over farther ones.
        zIndex: -node.distance,
        // What is on screen stays put; the first layout keeps the root at the centre.
        fixed: placed.has(node.id) || (placed.size === 0 && node.id === view.rootId),
      });
    }

    graph.clearEdges();
    edgeDistances.clear();
    markedEdges.clear();
    for (const edge of view.graph.edges) {
      const style = edgeStyleOf(edge.assertion);
      graph.addEdgeWithKey(edge.id, edge.sourceNodeId, edge.targetNodeId, {
        label: edge.kind,
        size: style.size,
        color: EDGE_COLOR,
        type: EDGE_PROGRAMS[style.line],
        weight: edge.strength ?? 1,
      });
      edgeDistances.set(edge.id, Math.min(distanceOf(edge.sourceNodeId), distanceOf(edge.targetNodeId)));
      if (edge.agentUnreviewed) {
        markedEdges.add(edge.id);
      }
    }

    if (graph.someNode((_id, attributes) => attributes.fixed !== true)) {
      forceAtlas2.assign(graph, {
        iterations: LAYOUT_ITERATIONS,
        getEdgeWeight: "weight",
        settings: { ...forceAtlas2.inferSettings(graph), edgeWeightInfluence: STRENGTH_INFLUENCE },
      });
    }

    graph.forEachNode((id) => graph.removeNodeAttribute(id, "fixed"));
  }

  function distanceOf(nodeId: string): number {
    return nodesById.get(nodeId)?.distance ?? 0;
  }

  function isMarkedNode(nodeId: string): boolean {
    return current?.highlightAgent === true && nodesById.get(nodeId)?.agentUnreviewed === true;
  }

  function zoomNow(): number {
    return sigma ? 1 / sigma.getCamera().getState().ratio : (current?.zoom ?? 1);
  }

  function nodeShown(id: string, attributes: NodeAttributes): Partial<NodeDisplayData> {
    const node = nodesById.get(id);
    if (!node || !current) {
      return attributes;
    }

    const isSelected = id === current.selectedId;
    // The keyboard's Node is named and boxed at every level, as the selected one is (FE-032).
    const isFocused = id === current.focusedId;
    const level = levelOfDetail(zoomNow(), node.distance);
    const parts = [node.title];
    if (level === "near") {
      parts.push(current.labels.nodeSummary(node));
    }

    // A marked Node is named at every level: the ring alone would not say what it means.
    const isMarked = isMarkedNode(id);
    if (isMarked) {
      parts.push(current.labels.agentUnreviewed);
    }

    return {
      ...attributes,
      label: level === "far" && !isSelected && !isFocused && !isMarked ? null : parts.join(SUMMARY_SEPARATOR),
      forceLabel: isSelected || isFocused || isMarked || id === current.rootId,
      highlighted: isSelected || isFocused,
    };
  }

  function edgeShown(id: string, attributes: EdgeAttributes): Partial<EdgeDisplayData> {
    if (current?.highlightAgent && markedEdges.has(id)) {
      return {
        ...attributes,
        label: `${attributes.label}${SUMMARY_SEPARATOR}${current.labels.agentUnreviewed}`,
        size: attributes.size * AGENT_EDGE_SCALE,
        forceLabel: true,
      };
    }

    const level = levelOfDetail(zoomNow(), edgeDistances.get(id) ?? 0);
    return level === "near" ? attributes : { ...attributes, label: null };
  }

  function zoomTo(zoom: number) {
    const camera = sigma?.getCamera();
    if (camera && Math.abs(1 / camera.getState().ratio - zoom) > ZOOM_TOLERANCE) {
      camera.setState({ ratio: 1 / zoom });
    }
  }

  return {
    mount(container: HTMLElement, view: GraphRenderView, events: GraphRendererEvents) {
      draw(view);
      current = view;
      sigma = new Sigma(graph, container, {
        allowInvalidContainer: true,
        zIndex: true,
        renderEdgeLabels: true,
        // The level of detail decides which titles show, not the size on screen.
        labelRenderedSizeThreshold: 0,
        minCameraRatio: 1 / MAX_ZOOM,
        maxCameraRatio: 1 / MIN_ZOOM,
        edgeProgramClasses: { curved: EdgeCurveProgram },
        nodeReducer: nodeShown,
        edgeReducer: edgeShown,
        defaultDrawNodeLabel: drawLabelWithAgentRing(isMarkedNode),
      });
      sigma.on("clickNode", ({ node }) => events.onSelect(node));
      sigma.on("doubleClickNode", ({ node, preventSigmaDefault }) => {
        preventSigmaDefault();
        events.onReroot(node);
      });
      sigma.getCamera().on("updated", (state) => events.onZoomChange(1 / state.ratio));
      zoomTo(view.zoom);
    },
    update(view: GraphRenderView) {
      // The zoom, the selection or the texts alone change what shows, not where Nodes are.
      if (view.graph !== current?.graph || view.rootId !== current.rootId) {
        draw(view);
      }

      current = view;
      zoomTo(view.zoom);
      sigma?.refresh();
    },
    dispose() {
      sigma?.kill();
      sigma = undefined;
    },
  };
}
