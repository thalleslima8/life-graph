import type { RenderableGraph, RenderableNode } from "./graphShape";

/**
 * The port between GraphCanvas and the library that draws the graph (DA-137, DA-142). The
 * library stays behind it, in graphRenderer.ts: swapping it for the reserve candidate changes
 * only the adapter.
 */

export type GraphCanvasLabels = {
  /** The accessible name of the view: the drawing, or the list. */
  canvas: string;
  /** Next to the title of what an agent changed and the person has not reviewed (DA-141). */
  agentUnreviewed: string;
  /** The short summary of a Node the near level of the Semantic Zoom adds to its title (DA-140). */
  nodeSummary: (node: RenderableNode) => string;
};

/** What the renderer draws: the props of GraphCanvas that change the picture. */
export type GraphRenderView = {
  graph: RenderableGraph;
  /** The graph's root, `graph.rootId`. */
  rootId: string;
  selectedId: string | undefined;
  /** The Node the keyboard is on while the drawing has focus (DA-142); undefined otherwise. */
  focusedId: string | undefined;
  highlightAgent: boolean;
  /** 1 is the initial zoom; 2 shows things twice as large. It sets the level of detail (DA-140). */
  zoom: number;
  labels: GraphCanvasLabels;
};

export type GraphRendererEvents = {
  onSelect: (nodeId: string) => void;
  onReroot: (nodeId: string) => void;
  onZoomChange: (zoom: number) => void;
};

export type GraphRenderer = {
  /** Draws into the container. Throws when the browser cannot draw (no WebGL). */
  mount: (container: HTMLElement, view: GraphRenderView, events: GraphRendererEvents) => void;
  /** Redraws by id: a Node still there keeps its position, a new one starts near its neighbors. */
  update: (view: GraphRenderView) => void;
  dispose: () => void;
};

export type GraphRendererFactory = () => GraphRenderer;
