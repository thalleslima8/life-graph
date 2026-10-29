import { useId, useMemo, useRef, type KeyboardEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { ASSERTION_LABELS, distanceHeading, graphKeyboardHelp, REROOT_LABEL, relationsOfLabel, relationText, softRelationExplanationText } from "./graphViewMessages";
import { buildGraphIndex, isArrowKey, keyAction, nextFocus } from "./graphNavigation";
import type { GraphCanvasLabels } from "./graphRendererPort";
import type { RenderableEdge, RenderableGraph, RenderableNode } from "./graphShape";

export type GraphListProps = {
  /** The root is `graph.rootId`. */
  graph: RenderableGraph;
  selectedId?: string | undefined;
  /** Marks what an agent changed and nobody reviewed; off unless asked (a visitor's view never asks). */
  highlightAgent?: boolean;
  onSelect: (nodeId: string) => void;
  /** Absent, Shift+Enter does nothing and no Node offers "Centralizar aqui" (a visitor's view). */
  onReroot?: (nodeId: string) => void;
  labels: GraphCanvasLabels;
  /** A mark beside a Node's title, such as the owner's "compartilhado" badge; the visitor's view has none. */
  nodeBadge?: (nodeId: string) => ReactNode;
};

/**
 * The local graph as a list (FE-030, DA-142): the conforming alternative to the drawing, and
 * where the page falls back when the drawing fails. Nodes come grouped by distance from the
 * root, each with its Relations in words; Hard, Soft and what an agent changed are said in
 * text, never by colour alone (FE-034). Every Node is a button in the tab order, and the
 * arrows move between them by the drawing's own model (graphNavigation).
 */
export function GraphList({ graph, selectedId, highlightAgent = false, onSelect, onReroot, labels, nodeBadge }: GraphListProps) {
  const rootId = graph.rootId;
  const helpId = useId();
  const index = useMemo(() => buildGraphIndex(graph, rootId), [graph, rootId]);
  const nodesById = useMemo(() => new Map(graph.nodes.map((node) => [node.id, node])), [graph.nodes]);
  const groups = useMemo(() => groupByDistance(index.order, nodesById), [index, nodesById]);
  const relationsByNode = useMemo(() => relationsOf(graph.edges), [graph.edges]);
  const buttons = useRef(new Map<string, HTMLButtonElement>());
  const start = selectedId !== undefined && nodesById.has(selectedId) ? selectedId : rootId;

  const onKeyDown = (event: KeyboardEvent<HTMLButtonElement>, nodeId: string) => {
    if (event.altKey || event.ctrlKey || event.metaKey) {
      return;
    }

    if (isArrowKey(event.key)) {
      event.preventDefault();
      buttons.current.get(nextFocus(index, nodeId, event.key))?.focus();
      return;
    }

    // Zooming means nothing in a list.
    const action = keyAction(event.key, event.shiftKey);
    if (action === "select") {
      event.preventDefault();
      onSelect(nodeId);
    } else if (action === "reroot") {
      event.preventDefault();
      if (nodeId !== rootId) {
        onReroot?.(nodeId);
      }
    } else if (action === "clear") {
      event.preventDefault();
      buttons.current.get(start)?.focus();
    }
  };

  const titleOf = (nodeId: string) => nodesById.get(nodeId)?.title ?? "";
  return (
    <section aria-label={labels.canvas} aria-describedby={helpId} className="space-y-4">
      <p id={helpId} className="text-sm text-muted-foreground">
        {graphKeyboardHelp({ canReroot: onReroot !== undefined, canZoom: false })}
      </p>
      {groups.map(({ distance, nodes }) => (
        <div key={distance} className="space-y-2">
          <h2 className="text-lg font-medium">{distanceHeading(distance)}</h2>
          <ul className="space-y-3">
            {nodes.map((node) => (
              <li key={node.id} className="space-y-1">
                <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
                  <button
                    ref={(button) => {
                      if (button) {
                        buttons.current.set(node.id, button);
                      } else {
                        buttons.current.delete(node.id);
                      }
                    }}
                    type="button"
                    aria-current={node.id === selectedId ? "true" : undefined}
                    onClick={() => onSelect(node.id)}
                    onKeyDown={(event) => onKeyDown(event, node.id)}
                    className="rounded-sm font-medium underline-offset-4 hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 aria-[current=true]:underline"
                  >
                    {node.title}
                  </button>
                  <span className="text-sm text-muted-foreground">{labels.nodeSummary(node)}</span>
                  {highlightAgent && node.agentUnreviewed && <AgentMark text={labels.agentUnreviewed} />}
                  {nodeBadge?.(node.id)}
                  {onReroot && node.id !== rootId && (
                    <Button type="button" variant="ghost" size="sm" onClick={() => onReroot(node.id)}>
                      {REROOT_LABEL}
                      <span className="sr-only">: {node.title}</span>
                    </Button>
                  )}
                </div>
                <RelationList
                  nodeTitle={node.title}
                  relations={relationsByNode.get(node.id) ?? []}
                  titleOf={titleOf}
                  agentText={highlightAgent ? labels.agentUnreviewed : null}
                />
              </li>
            ))}
          </ul>
        </div>
      ))}
    </section>
  );
}

function RelationList({
  nodeTitle,
  relations,
  titleOf,
  agentText,
}: {
  nodeTitle: string;
  relations: readonly RenderableEdge[];
  titleOf: (nodeId: string) => string;
  /** Null when the view does not ask for the agent highlight. */
  agentText: string | null;
}) {
  if (relations.length === 0) {
    return null;
  }

  return (
    <ul aria-label={relationsOfLabel(nodeTitle)} className="ml-4 list-disc space-y-0.5 pl-4 text-sm">
      {relations.map((edge) => (
        <li key={edge.id}>
          {relationText(titleOf(edge.sourceNodeId), edge.kind, titleOf(edge.targetNodeId))}
          {edge.assertion && ` · ${ASSERTION_LABELS[edge.assertion]}`}
          {edge.explanation && ` · ${softRelationExplanationText(edge.explanation)}`}
          {agentText !== null && edge.agentUnreviewed && (
            <>
              {" · "}
              <AgentMark text={agentText} />
            </>
          )}
        </li>
      ))}
    </ul>
  );
}

/** What an agent changed and nobody reviewed: an icon and its text, never colour alone (FE-034). */
function AgentMark({ text }: { text: string }) {
  return (
    <span className="text-sm">
      <span aria-hidden="true">◎ </span>
      {text}
    </span>
  );
}

function groupByDistance(order: readonly string[], nodesById: ReadonlyMap<string, RenderableNode>): { distance: number; nodes: RenderableNode[] }[] {
  const groups: { distance: number; nodes: RenderableNode[] }[] = [];
  for (const nodeId of order) {
    const node = nodesById.get(nodeId);
    if (!node) {
      continue;
    }

    const last = groups.at(-1);
    if (last?.distance === node.distance) {
      last.nodes.push(node);
    } else {
      groups.push({ distance: node.distance, nodes: [node] });
    }
  }

  return groups;
}

/** Each Node's Relations, either direction, in the order the read gave them. */
function relationsOf(edges: readonly RenderableEdge[]): Map<string, RenderableEdge[]> {
  const byNode = new Map<string, RenderableEdge[]>();
  for (const edge of edges) {
    for (const nodeId of new Set([edge.sourceNodeId, edge.targetNodeId])) {
      const relations = byNode.get(nodeId);
      if (relations) {
        relations.push(edge);
      } else {
        byNode.set(nodeId, [edge]);
      }
    }
  }

  return byNode;
}
