import type { LocalGraphEdge } from "./localGraph";

/**
 * How the local graph looks, without any library (DA-137), so the drawing and its legend
 * agree: a Node's size from its relevance (DA-139) and its distance from the root (the depth
 * cue of the 2.5D view), the line of a Hard or a Soft Relation, the colour of a Type (DA-140).
 * Every colour keeps at least 3:1 contrast on the white canvas (FE-034, WCAG 1.4.11), and no
 * information is in colour alone: a Soft Relation is also curved and thinner, a Type is also
 * written in the legend and in the near level's summary.
 */

/** The size of a Node no Relation reaches. */
export const MIN_NODE_SIZE = 4;
/** The size of a hub: from DEGREE_CAP Relations on, a Node grows no more. */
export const MAX_NODE_SIZE = 16;
/** The degree drawn at MAX_NODE_SIZE: a hub does not crush the rest (DA-139). */
export const DEGREE_CAP = 64;
/** How much smaller a Node is at the deepest distance and beyond. */
const DEEPEST_SCALE = 0.55;
/** How much smaller a Node is at each distance from the root: the depth cue of the 2.5D view. */
export const DEPTH_SCALES = [1, 0.85, 0.7, DEEPEST_SCALE] as const;

/** Relevance = the visible Hard degree (DA-139), on a log scale capped at DEGREE_CAP, then shrunk by distance. */
export function nodeSizeOf(degree: number, distance: number): number {
  const counted = Math.min(Math.max(degree, 0), DEGREE_CAP);
  const relevance = Math.log1p(counted) / Math.log1p(DEGREE_CAP);
  return (MIN_NODE_SIZE + (MAX_NODE_SIZE - MIN_NODE_SIZE) * relevance) * depthScaleOf(distance);
}

export function depthScaleOf(distance: number): number {
  return DEPTH_SCALES[Math.min(Math.max(distance, 0), DEPTH_SCALES.length - 1)] ?? DEEPEST_SCALE;
}

export type EdgeLine = "straight" | "curved";
export type EdgeStyle = { line: EdgeLine; size: number };

export const HARD_EDGE_SIZE = 2;
export const SOFT_EDGE_SIZE = 1;
/** One colour for every Relation: Hard and Soft differ by line and thickness, never by colour. */
export const EDGE_COLOR = "#64748b";

/**
 * A Node or Relation a GraphChangeSet of an agent touched and nobody reviewed yet (DA-141) is
 * marked by shape and text, never by colour alone (FE-034): a ring around the Node, a thicker
 * Relation, and the label text on both. The ring's colour still keeps 3:1 on the white canvas.
 */
export const AGENT_RING_COLOR = "#0f172a";
/** Pixels between the Node's disc and its ring. */
export const AGENT_RING_GAP = 2;
/** Pixels of the ring's line. */
export const AGENT_RING_WIDTH = 2;
/** A marked Relation is this many times thicker; a Soft one stays thinner than a Hard one. */
export const AGENT_EDGE_SCALE = 2;

/** A Hard Relation is a straight, thicker line; a Soft one (E9) is curved and thinner. */
export function edgeStyleOf(assertion: LocalGraphEdge["assertion"]): EdgeStyle {
  return assertion === "soft" ? { line: "curved", size: SOFT_EDGE_SIZE } : { line: "straight", size: HARD_EDGE_SIZE };
}

/** The colours a Type can take, each at least 3:1 on white. */
export const TYPE_COLORS = ["#1d4ed8", "#047857", "#b45309", "#be123c", "#6d28d9", "#0e7490", "#a21caf", "#4d7c0f"] as const;
/** The colour of a Node without a Type. */
export const UNTYPED_COLOR = "#475569";

const HASH_MULTIPLIER = 31;

/**
 * The colour of a Type, from its id alone: the same Type keeps its colour on every root and
 * depth. With more Types than colours, two Types can share one; the legend and the near
 * level's summary still name them.
 */
export function typeColorOf(typeId: string | null): string {
  if (typeId === null) {
    return UNTYPED_COLOR;
  }

  let hash = 0;
  for (const character of typeId) {
    hash = (hash * HASH_MULTIPLIER + character.charCodeAt(0)) % TYPE_COLORS.length;
  }

  return TYPE_COLORS[hash] ?? UNTYPED_COLOR;
}
