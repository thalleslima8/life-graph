import type { RenderableGraph } from "./graphShape";

/**
 * Where each Node of the local graph starts, without any library (DA-137): the root at the
 * centre and each distance on its own ring, in the order the server ranked them. A Node
 * already placed keeps its position, so a refetch or another depth does not move what the
 * person is looking at (DA-142); a new Node starts next to a neighbor already placed.
 * Deterministic: the same graph gives the same positions (GEN-072).
 */

export type Point = { x: number; y: number };

export const RING_SPACING = 100;
/** How far from its placed neighbor a new Node starts. */
export const NEIGHBOR_OFFSET = RING_SPACING / 4;

export function placeNodes(graph: RenderableGraph, rootId: string, placed: ReadonlyMap<string, Point> = new Map()): Map<string, Point> {
  const positions = new Map<string, Point>();
  const rings = new Map<number, string[]>();
  for (const node of graph.nodes) {
    const ring = rings.get(node.distance) ?? [];
    ring.push(node.id);
    rings.set(node.distance, ring);
  }

  const neighbors = neighborsOf(graph);
  for (const node of graph.nodes) {
    const previous = placed.get(node.id);
    if (previous) {
      positions.set(node.id, previous);
      continue;
    }

    const anchor = neighbors.get(node.id)?.map((id) => placed.get(id)).find((point) => point !== undefined);
    if (anchor) {
      const ring = rings.get(node.distance) ?? [];
      const angle = (2 * Math.PI * ring.indexOf(node.id)) / Math.max(ring.length, 1);
      positions.set(node.id, { x: anchor.x + NEIGHBOR_OFFSET * Math.cos(angle), y: anchor.y + NEIGHBOR_OFFSET * Math.sin(angle) });
      continue;
    }

    positions.set(node.id, node.id === rootId ? { x: 0, y: 0 } : ringPosition(rings.get(node.distance) ?? [node.id], node.id, node.distance));
  }

  return positions;
}

function ringPosition(ring: string[], id: string, distance: number): Point {
  const angle = (2 * Math.PI * ring.indexOf(id)) / ring.length;
  const radius = RING_SPACING * Math.max(distance, 1);
  return { x: radius * Math.cos(angle), y: radius * Math.sin(angle) };
}

function neighborsOf(graph: RenderableGraph): Map<string, string[]> {
  const neighbors = new Map<string, string[]>();
  const link = (from: string, to: string) => {
    const list = neighbors.get(from) ?? [];
    list.push(to);
    neighbors.set(from, list);
  };
  for (const edge of graph.edges) {
    link(edge.sourceNodeId, edge.targetNodeId);
    link(edge.targetNodeId, edge.sourceNodeId);
  }

  return neighbors;
}
