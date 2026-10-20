import { useEffect, useId, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { createSigmaRenderer } from "./graphRenderer";
import { distanceHeading, graphKeyboardHelp, SELECTED_NODE_LABEL } from "./graphMessages";
import { buildGraphIndex, isArrowKey, keyAction, nextFocus } from "./graphNavigation";
import type { GraphCanvasLabels, GraphRenderer, GraphRendererFactory, GraphRenderView } from "./graphRendererPort";
import { INITIAL_ZOOM, steppedZoom, type LocalGraph, type LocalGraphNode } from "./localGraph";

export type GraphCanvasProps = {
  graph: LocalGraph;
  rootId: string;
  selectedId?: string | undefined;
  highlightAgent: boolean;
  zoom: number;
  onZoomChange: (zoom: number) => void;
  onSelect: (nodeId: string) => void;
  /** Absent, a double click or Shift+Enter does nothing: the graph cannot be re-rooted (a visitor's view). */
  onReroot?: (nodeId: string) => void;
  labels: GraphCanvasLabels;
  /** The browser cannot draw the graph at all (no WebGL). */
  onUnavailable?: () => void;
  /** Drawing failed after the graph was first shown. */
  onRenderError?: (error: unknown) => void;
  /** Tests pass a fake: jsdom has no WebGL (DA-137). */
  rendererFactory?: GraphRendererFactory;
};

/**
 * The local graph drawn on a canvas (DA-142). Controlled: the page owns the selection, the
 * zoom and the root. The drawing lives in an imperative renderer outside React state
 * (DA-052), mounted once and then updated by id, so a refetch does not move the Nodes.
 * The drawing takes the keyboard (graphNavigation): the Node the keyboard is on is boxed in
 * the drawing and announced, since a canvas has no element per Node.
 */
export function GraphCanvas({
  graph,
  rootId,
  selectedId,
  highlightAgent,
  zoom,
  onZoomChange,
  onSelect,
  onReroot,
  labels,
  onUnavailable,
  onRenderError,
  rendererFactory = createSigmaRenderer,
}: GraphCanvasProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const rendererRef = useRef<GraphRenderer | null>(null);
  const drawnRef = useRef<GraphRenderView | null>(null);
  const helpId = useId();
  const keyboard = useKeyboardNode(graph, rootId, selectedId);
  const [hasFocus, setHasFocus] = useState(false);
  const [announcement, setAnnouncement] = useState("");
  const focusedId = hasFocus ? keyboard.nodeId : undefined;
  const view = useMemo<GraphRenderView>(
    () => ({ graph, rootId, selectedId, focusedId, highlightAgent, zoom, labels }),
    [graph, rootId, selectedId, focusedId, highlightAgent, zoom, labels],
  );
  // The renderer calls back with the latest props, without mounting again when they change.
  const latest = useRef({ view, onZoomChange, onSelect, onReroot, onUnavailable, onRenderError });
  useLayoutEffect(() => {
    latest.current = { view, onZoomChange, onSelect, onReroot, onUnavailable, onRenderError };
  });

  useEffect(() => {
    const container = containerRef.current;
    if (!container) {
      return;
    }

    const renderer = rendererFactory();
    try {
      renderer.mount(container, latest.current.view, {
        onSelect: (nodeId) => latest.current.onSelect(nodeId),
        onReroot: (nodeId) => latest.current.onReroot?.(nodeId),
        onZoomChange: (nextZoom) => latest.current.onZoomChange(nextZoom),
      });
    } catch {
      latest.current.onUnavailable?.();
      return;
    }

    rendererRef.current = renderer;
    drawnRef.current = latest.current.view;
    return () => {
      rendererRef.current = null;
      drawnRef.current = null;
      renderer.dispose();
    };
  }, [rendererFactory]);

  useEffect(() => {
    const renderer = rendererRef.current;
    if (!renderer || drawnRef.current === view) {
      return;
    }

    try {
      renderer.update(view);
      drawnRef.current = view;
    } catch (error) {
      latest.current.onRenderError?.(error);
    }
  }, [view]);

  const moveTo = (nodeId: string | undefined) => {
    const node = keyboard.moveTo(nodeId);
    setAnnouncement(announcementOf(node, { selectedId, highlightAgent, labels }));
  };

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.altKey || event.ctrlKey || event.metaKey) {
      return;
    }

    if (isArrowKey(event.key)) {
      event.preventDefault();
      moveTo(nextFocus(keyboard.index, keyboard.nodeId, event.key));
      return;
    }

    const action = keyAction(event.key, event.shiftKey);
    if (action === null) {
      return;
    }

    event.preventDefault();
    switch (action) {
      case "select":
        onSelect(keyboard.nodeId);
        break;
      case "reroot":
        if (keyboard.nodeId !== rootId) {
          onReroot?.(keyboard.nodeId);
        }
        break;
      case "zoomIn":
        onZoomChange(steppedZoom(zoom, "in"));
        break;
      case "zoomOut":
        onZoomChange(steppedZoom(zoom, "out"));
        break;
      case "zoomReset":
        onZoomChange(INITIAL_ZOOM);
        break;
      case "clear":
        moveTo(undefined);
        break;
    }
  };

  return (
    <div className="space-y-2">
      <p id={helpId} className="text-sm text-muted-foreground">
        {graphKeyboardHelp({ canReroot: onReroot !== undefined, canZoom: true })}
      </p>
      {/* role=application: the drawing handles its own keys (graphNavigation), as a map would. */}
      <div
        ref={containerRef}
        role="application"
        tabIndex={0}
        aria-label={labels.canvas}
        aria-describedby={helpId}
        onKeyDown={onKeyDown}
        onFocus={() => setHasFocus(true)}
        onBlur={() => setHasFocus(false)}
        className="h-[60vh] min-h-80 w-full rounded-md border bg-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
      />
      <p role="status" className="sr-only">
        {announcement}
      </p>
    </div>
  );
}

/**
 * The Node the keyboard is on: where the arrows last went while it is still in the graph,
 * otherwise the selected Node, otherwise the root.
 */
function useKeyboardNode(graph: LocalGraph, rootId: string, selectedId: string | undefined) {
  const index = useMemo(() => buildGraphIndex(graph, rootId), [graph, rootId]);
  const nodesById = useMemo(() => new Map(graph.nodes.map((node) => [node.id, node])), [graph.nodes]);
  const [movedTo, setMovedTo] = useState<string>();
  const start = selectedId !== undefined && nodesById.has(selectedId) ? selectedId : rootId;
  const nodeId = movedTo !== undefined && nodesById.has(movedTo) ? movedTo : start;
  return {
    index,
    nodeId,
    /** Moves to a Node, or back to the start with undefined; returns the Node it lands on. */
    moveTo: (next: string | undefined): LocalGraphNode | undefined => {
      setMovedTo(next);
      return nodesById.get(next ?? start);
    },
  };
}

function announcementOf(
  node: LocalGraphNode | undefined,
  { selectedId, highlightAgent, labels }: { selectedId: string | undefined; highlightAgent: boolean; labels: GraphCanvasLabels },
): string {
  if (!node) {
    return "";
  }

  return [
    node.title,
    labels.nodeSummary(node),
    distanceHeading(node.distance),
    ...(highlightAgent && node.agentUnreviewed ? [labels.agentUnreviewed] : []),
    ...(node.id === selectedId ? [SELECTED_NODE_LABEL] : []),
  ].join(", ");
}
