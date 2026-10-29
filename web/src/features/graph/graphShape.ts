/**
 * The graph the drawing and the list show (DA-222): only what both the owner's local graph and
 * a visitor's SharedGraphView can give, so GraphCanvas and GraphList never depend on either
 * read. What only the owner's read has is optional: a visitor's graph leaves it out (DA-077).
 */

export type RenderableNode = {
  id: string;
  title: string;
  /** Null for a Node without a Type. */
  typeName: string | null;
  /** Hops from the root; 0 is the root. */
  distance: number;
  /** The owner's read only: colours the Node by its Type. */
  typeId?: string | null;
  /** The owner's read only: sizes the Node by its Relations. A visitor never sees counts (DA-077). */
  degree?: number;
  /** The owner's read only: touched by an agent and not reviewed (DA-141). */
  agentUnreviewed?: boolean;
};

export type RenderableEdge = {
  id: string;
  sourceNodeId: string;
  targetNodeId: string;
  kind: string;
  /** Absent in a visitor's graph, which has Hard Relations only (DA-077). */
  assertion?: "hard" | "soft";
  /** The owner's read only. */
  strength?: number;
  /** The owner's read only. */
  agentUnreviewed?: boolean;
  /** The owner's read only: why a Soft Relation was inferred. */
  explanation?: { method: string; modelId?: string | null; score?: number | string | null } | null;
};

export type RenderableGraph = {
  rootId: string;
  nodes: RenderableNode[];
  edges: RenderableEdge[];
};
