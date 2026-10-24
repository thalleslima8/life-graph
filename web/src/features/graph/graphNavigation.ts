import type { LocalGraph } from "./localGraph";

/**
 * The keyboard model of the local graph (DA-142), shared by the drawing and the list so the two
 * cannot drift. Pure: no React, no drawing library. ↑ and ↓ walk every Node in reading order (by
 * distance from the root, then by title); → goes to a neighbor farther from the root and ← to one
 * nearer it.
 */

export const ARROW_KEYS = ["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight"] as const;
export type ArrowKey = (typeof ARROW_KEYS)[number];

export type KeyAction = "select" | "reroot" | "zoomIn" | "zoomOut" | "zoomReset" | "clear";

export type GraphIndex = {
  /** How many Relations from the root, as the read walked it; undefined for a Node not in the graph. */
  distance: (nodeId: string) => number | undefined;
  /** The Nodes one Relation away, in reading order. */
  neighbors: (nodeId: string) => readonly string[];
  /** Every Node in reading order: the root first, then by distance, then by title. */
  order: readonly string[];
};

const TITLE_COLLATOR = new Intl.Collator("pt-BR");

export function isArrowKey(key: string): key is ArrowKey {
  return (ARROW_KEYS as readonly string[]).includes(key);
}

/** Indexes the graph for keyboard navigation. A root missing from the graph is a broken invariant, so it throws. */
export function buildGraphIndex(graph: LocalGraph, rootId: string): GraphIndex {
  if (!graph.nodes.some((node) => node.id === rootId)) {
    throw new Error(`The root ${rootId} is not in the graph.`);
  }

  const distances = new Map(graph.nodes.map((node) => [node.id, node.distance]));
  const order = [...graph.nodes]
    .sort((left, right) => left.distance - right.distance || TITLE_COLLATOR.compare(left.title, right.title) || left.id.localeCompare(right.id))
    .map((node) => node.id);
  const position = new Map(order.map((id, index) => [id, index]));

  const adjacent = new Map<string, Set<string>>(order.map((id) => [id, new Set<string>()]));
  for (const edge of graph.edges) {
    if (edge.sourceNodeId !== edge.targetNodeId) {
      adjacent.get(edge.sourceNodeId)?.add(edge.targetNodeId);
      adjacent.get(edge.targetNodeId)?.add(edge.sourceNodeId);
    }
  }

  const neighbors = new Map(
    [...adjacent].map(([id, ids]) => [id, [...ids].filter((neighbor) => position.has(neighbor)).sort((left, right) => (position.get(left) ?? 0) - (position.get(right) ?? 0))]),
  );

  return {
    distance: (nodeId) => distances.get(nodeId),
    neighbors: (nodeId) => neighbors.get(nodeId) ?? [],
    order,
  };
}

/** Where an arrow key moves the keyboard focus from `focusedId`; where it is when the arrow leads nowhere. */
export function nextFocus(index: GraphIndex, focusedId: string, key: ArrowKey): string {
  const at = index.order.indexOf(focusedId);
  const distance = index.distance(focusedId);
  if (at === -1 || distance === undefined) {
    return index.order[0] ?? focusedId;
  }

  switch (key) {
    case "ArrowDown":
      return index.order[Math.min(at + 1, index.order.length - 1)] ?? focusedId;
    case "ArrowUp":
      return index.order[Math.max(at - 1, 0)] ?? focusedId;
    case "ArrowRight":
      return index.neighbors(focusedId).find((neighbor) => (index.distance(neighbor) ?? 0) > distance) ?? focusedId;
    case "ArrowLeft":
      return index.neighbors(focusedId).find((neighbor) => (index.distance(neighbor) ?? 0) < distance) ?? focusedId;
  }
}

const ACTION_BY_KEY: ReadonlyMap<string, KeyAction> = new Map<string, KeyAction>([
  ["+", "zoomIn"],
  ["-", "zoomOut"],
  ["0", "zoomReset"],
  ["Escape", "clear"],
]);

/**
 * What a key other than an arrow does: Enter selects, Shift+Enter re-roots, + − 0 zoom. Escape is
 * "clear" (DA-142's name): it clears where the arrows went, taking the keyboard back to the selected
 * Node or the root; the selection and the Inspector stay (DA-143).
 */
export function keyAction(key: string, shift: boolean): KeyAction | null {
  if (key === "Enter") {
    return shift ? "reroot" : "select";
  }

  return ACTION_BY_KEY.get(key) ?? null;
}
