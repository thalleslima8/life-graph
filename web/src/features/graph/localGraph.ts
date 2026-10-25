import { keepPreviousData, queryOptions } from "@tanstack/react-query";
import { useCallback, useMemo } from "react";
import { useSearchParams } from "react-router";
import { z } from "zod";
import { api, unwrap } from "@/api/client";
import type { components } from "@/api/schema.gen";
import { graphKeys, retryUnlessNotFound } from "./graphData";

/**
 * The local graph of the 2.5D view (DA-138, DA-142): the query of the subgraph around one
 * Node and the view state kept in the URL (FE-012). The zoom is not here: it is transient
 * view state, kept by the page.
 */

type Schemas = components["schemas"];

export type LocalGraphNode = Omit<Schemas["LocalGraphNode"], "distance" | "degree"> & { distance: number; degree: number };
export type LocalGraphEdge = Omit<Schemas["LocalGraphEdge"], "strength"> & { strength: number };
export type LocalGraph = Omit<Schemas["LocalGraph"], "nodes" | "edges" | "omittedNodeCount" | "omittedEdgeCount"> & {
  nodes: LocalGraphNode[];
  edges: LocalGraphEdge[];
  omittedNodeCount: number;
  omittedEdgeCount: number;
};

export const GRAPH_DEPTHS = [1, 2, 3] as const;
export type GraphDepth = (typeof GRAPH_DEPTHS)[number];
export const DEFAULT_DEPTH: GraphDepth = 2;

export const GRAPH_VIEW_MODES = ["canvas", "list"] as const;
export type GraphViewMode = (typeof GRAPH_VIEW_MODES)[number];
export const DEFAULT_VIEW_MODE: GraphViewMode = "canvas";

export type GraphView = { depth: GraphDepth; view: GraphViewMode; selected?: string };

/** 1 shows the whole graph; 2 shows it twice as large. The zoom is page state, not the URL's. */
export const INITIAL_ZOOM = 1;
export const MIN_ZOOM = 0.25;
export const MAX_ZOOM = 8;
/** How much one press of "Aproximar" or "Afastar" multiplies or divides the zoom. */
export const ZOOM_STEP = 1.5;

/** The zoom one step closer (`in`) or farther (`out`), within MIN_ZOOM and MAX_ZOOM. */
export function steppedZoom(zoom: number, direction: "in" | "out"): number {
  const next = direction === "in" ? zoom * ZOOM_STEP : zoom / ZOOM_STEP;
  return Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, next));
}

/**
 * The three levels of the basic Semantic Zoom (DA-140): how much of a Node the drawing shows.
 * `far` is a dot; `middle` adds its title and its Type's colour; `near` adds a short summary of
 * the Node and the kinds of its Relations. It is detail by zoom and distance from the root, not
 * a conceptual hierarchy: the model has none.
 */
export type LevelOfDetail = "far" | "middle" | "near";
/** From this zoom on, every Node shows its title. */
export const MIDDLE_ZOOM = 1.5;
/** From this zoom on, Nodes show a summary and Relations their kinds. */
export const NEAR_ZOOM = 3;
/** Zoomed out, the Nodes up to this distance from the root keep their titles: the root and its neighbors. */
export const FAR_TITLED_DISTANCE = 1;

/** The level the zoom gives every Node past FAR_TITLED_DISTANCE: what the person is shown. */
export function zoomLevelOf(zoom: number): LevelOfDetail {
  if (zoom >= NEAR_ZOOM) {
    return "near";
  }

  return zoom >= MIDDLE_ZOOM ? "middle" : "far";
}

/** The level of a Node at `distance` from the root, at this zoom. */
export function levelOfDetail(zoom: number, distance: number): LevelOfDetail {
  const level = zoomLevelOf(zoom);
  return level === "far" && distance <= FAR_TITLED_DISTANCE ? "middle" : level;
}

/** Where the graph opens with no Node named: the last focus, or the Raiz da Conta (DA-197). */
export const GRAPH_ENTRY_PATH = "/graph";

const LAST_FOCUS_KEY = "lifegraph.graph.lastFocus";

/** The Node the local graph was last rooted at in this browser; undefined when none or storage is unavailable. */
export function readLastFocus(): string | undefined {
  try {
    return window.localStorage.getItem(LAST_FOCUS_KEY) ?? undefined;
  } catch {
    return undefined;
  }
}

/** Remembers the Node the local graph is rooted at, a convenience of this browser only. */
export function rememberLastFocus(nodeId: string): void {
  try {
    window.localStorage.setItem(LAST_FOCUS_KEY, nodeId);
  } catch {
    // No storage: the graph opens at the root next time.
  }
}

/** Forgets a last focus that no longer opens (the Node is gone or not this Account's). */
export function forgetLastFocus(): void {
  try {
    window.localStorage.removeItem(LAST_FOCUS_KEY);
  } catch {
    // No storage: nothing was remembered.
  }
}

/** The page of the local graph rooted at a Node. */
export function graphPath(nodeId: string): string {
  return `/nodes/${nodeId}/graph`;
}

export const localGraphQuery = (nodeId: string, depth: GraphDepth) =>
  queryOptions({
    queryKey: graphKeys.localGraph(nodeId, depth),
    queryFn: async ({ signal }): Promise<LocalGraph> =>
      fromWire(unwrap(await api.GET("/api/nodes/{nodeId}/graph", { params: { path: { nodeId }, query: { depth } }, signal }))),
    retry: retryUnlessNotFound,
    // Another depth or root keeps the graph on screen until the new one arrives: no blank frame.
    placeholderData: keepPreviousData,
  });

// The API's integers arrive typed loosely by the OpenAPI generator (see versionOf).
function fromWire(graph: Schemas["LocalGraph"]): LocalGraph {
  return {
    ...graph,
    nodes: graph.nodes.map((node) => ({ ...node, distance: Number(node.distance), degree: Number(node.degree) })),
    edges: graph.edges.map((edge) => ({ ...edge, strength: Number(edge.strength) })),
    omittedNodeCount: Number(graph.omittedNodeCount),
    omittedEdgeCount: Number(graph.omittedEdgeCount),
  };
}

const DEPTH_BY_PARAM = { "1": 1, "2": 2, "3": 3 } as const satisfies Record<string, GraphDepth>;

// Each parameter falls back on its own: one bad value never resets the others (FE-012).
const graphViewSchema = z.object({
  depth: z.enum(["1", "2", "3"]).optional().catch(undefined),
  view: z.enum(GRAPH_VIEW_MODES).optional().catch(undefined),
  selected: z.uuid().optional().catch(undefined),
});

function readView(params: URLSearchParams): GraphView {
  const parsed = graphViewSchema.parse({
    depth: params.get("depth") ?? undefined,
    view: params.get("view") ?? undefined,
    selected: params.get("selected") ?? undefined,
  });
  return {
    depth: parsed.depth === undefined ? DEFAULT_DEPTH : DEPTH_BY_PARAM[parsed.depth],
    view: parsed.view ?? DEFAULT_VIEW_MODE,
    ...(parsed.selected === undefined ? {} : { selected: parsed.selected }),
  };
}

/**
 * The depth, the view and the selected Node of the graph page, in the URL. An invalid value
 * reads as the default, and only what differs from the default is written. Selecting replaces
 * the history entry, so Back leaves the graph instead of walking through every click.
 */
export function useGraphView(): [GraphView, (next: Partial<GraphView>) => void] {
  const [params, setParams] = useSearchParams();
  const view = useMemo(() => readView(params), [params]);
  const setView = useCallback(
    (next: Partial<GraphView>) => {
      const onlySelection = Object.keys(next).every((key) => key === "selected");
      setParams(
        (current) => {
          const merged = { ...readView(current), ...next };
          const written = new URLSearchParams();
          if (merged.depth !== DEFAULT_DEPTH) {
            written.set("depth", String(merged.depth));
          }

          if (merged.view !== DEFAULT_VIEW_MODE) {
            written.set("view", merged.view);
          }

          if (merged.selected) {
            written.set("selected", merged.selected);
          }

          return written;
        },
        { replace: onlySelection },
      );
    },
    [setParams],
  );
  return [view, setView];
}
