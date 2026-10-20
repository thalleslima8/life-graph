import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode, type RefObject } from "react";
import { Link, useLocation, useNavigate, useParams } from "react-router";
import { Button } from "@/components/ui/button";
import { GraphCanvas } from "./GraphCanvas";
import { graphKeys, isNotFound, typesQuery } from "./graphData";
import { GraphList } from "./GraphList";
import {
  AGENT_NEWS_ACTION,
  AGENT_NEWS_MESSAGE,
  AGENT_UNREVIEWED_LABEL,
  BACK_TO_NODES_LABEL,
  CREATE_RELATION_LABEL,
  DEPTH_LEGEND,
  GRAPH_DRAW_ERROR_MESSAGE,
  GRAPH_UPDATING_MESSAGE,
  GRAPH_VIEW_MODE_LABELS,
  LEGEND_AGENT_TEXT,
  LEGEND_HARD_TEXT,
  LEGEND_SIZE_TEXT,
  LEGEND_SOFT_TEXT,
  LEGEND_TITLE,
  LOAD_ERROR_MESSAGE,
  LOADING_MESSAGE,
  LOCAL_GRAPH_TITLE,
  NO_RELATIONS_MESSAGE,
  NO_TYPE_LABEL,
  NODE_NOT_FOUND_TITLE,
  OPEN_IN_INSPECTOR_LABEL,
  RETRY_LABEL,
  REVIEW_AGENT_CHANGES_LABEL,
  SELECTED_AGENT_NOTICE,
  SELECTED_GONE_MESSAGE,
  TYPE_NAME_LOADING_LABEL,
  TYPE_UNAVAILABLE_LABEL,
  VIEW_MODE_LEGEND,
  ZOOM_CONTROLS_LABEL,
  ZOOM_IN_LABEL,
  ZOOM_LEVEL_MESSAGES,
  ZOOM_OUT_LABEL,
  ZOOM_RESET_LABEL,
  localGraphLabel,
  localGraphTitle,
  nodeSummaryText,
  omittedMessage,
} from "./graphMessages";
import { AGENT_RING_COLOR, AGENT_RING_WIDTH, EDGE_COLOR, edgeStyleOf, typeColorOf, type EdgeLine } from "./graphVisuals";
import {
  GRAPH_DEPTHS,
  GRAPH_VIEW_MODES,
  INITIAL_ZOOM,
  MAX_ZOOM,
  MIN_ZOOM,
  graphPath,
  localGraphQuery,
  steppedZoom,
  useGraphView,
  zoomLevelOf,
  type GraphDepth,
  type GraphViewMode,
  type LocalGraph,
  type LocalGraphEdge,
  type LocalGraphNode,
} from "./localGraph";
import { NodeInspector } from "./NodeInspector";
import { useChangeSetProbe } from "./recentChanges";

type TypeNameOf = (typeId: string | null) => string;

/**
 * The local graph around one Node (DA-138, DA-142). States: loading, not found (the same for
 * every cause), error with a retry, a Node without Relations, and the graph. Another depth or
 * root keeps the previous graph on screen, busy, until the new one arrives. A selected Node opens
 * in the Inspector beside the graph; news from agents waits for "Atualizar" (DA-054, DA-142).
 * The graph is drawn or listed (FE-030); a drawing that fails falls back on the list, and stays
 * there in this browser even when Back returns to the drawing's URL.
 */
export function LocalGraphPage() {
  const { nodeId = "" } = useParams();
  const [view, setView] = useGraphView();
  const graph = useQuery(localGraphQuery(nodeId, view.depth));
  const typeNameOf = useTypeNames();
  const [zoom, setZoom] = useState(INITIAL_ZOOM);
  const [drawFailed, setDrawFailed] = useState(false);
  const location = useLocation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const probe = useChangeSetProbe();
  const viewRef = useRef<HTMLDivElement>(null);
  const inspectorRef = useRef<HTMLElement>(null);
  const [selectedGone, setSelectedGone] = useState(false);
  const focusOnSelect = useFocusOnSelect(view.selected, inspectorRef);
  useClearVanishedSelection({ graph: graph.data, current: !graph.isPlaceholderData, rootId: nodeId, depth: view.depth, selected: view.selected }, () => {
    setView({ selected: undefined });
    setSelectedGone(true);
    viewRef.current?.focus();
  });

  // The layout does not move while the person inspects: the graph is read again on request.
  const refresh = () => {
    probe.acknowledge();
    void queryClient.invalidateQueries({ queryKey: graphKeys.all });
  };

  const drawingFailed = () => {
    setDrawFailed(true);
    setView({ view: "list" });
  };

  const showAs = (mode: GraphViewMode) => {
    setDrawFailed(false);
    setView({ view: mode });
  };

  const select = (selected: string) => {
    setSelectedGone(false);
    setView({ selected });
    focusOnSelect(selected);
  };

  const closeInspector = () => {
    setView({ selected: undefined });
    viewRef.current?.focus();
  };

  // Re-rooting is navigation: the depth and the view stay, the selection does not.
  const reroot = useCallback(
    (rootId: string) => {
      const params = new URLSearchParams(location.search);
      params.delete("selected");
      const search = params.toString();
      void navigate({ pathname: graphPath(rootId), search: search ? `?${search}` : "" });
    },
    [location.search, navigate],
  );

  if (graph.isError && isNotFound(graph.error)) {
    return <NotFoundState />;
  }

  if (graph.isError) {
    return (
      <GraphShell title={LOCAL_GRAPH_TITLE}>
        <p role="alert" className="text-sm text-destructive">
          {LOAD_ERROR_MESSAGE}
        </p>
        <Button type="button" variant="secondary" onClick={() => void graph.refetch()}>
          {RETRY_LABEL}
        </Button>
      </GraphShell>
    );
  }

  if (graph.isPending) {
    return (
      <GraphShell title={LOCAL_GRAPH_TITLE}>
        <p role="status">{LOADING_MESSAGE}</p>
        <div aria-hidden="true" className="h-[60vh] min-h-80 w-full animate-pulse rounded-md bg-muted" />
      </GraphShell>
    );
  }

  const rootTitle = graph.data.nodes.find((node) => node.id === graph.data.rootNodeId)?.title ?? "";
  if (graph.data.nodes.length === 1 && graph.data.edges.length === 0) {
    return (
      <GraphShell title={localGraphTitle(rootTitle)}>
        <AgentNews hasNewer={probe.hasNewer} onRefresh={refresh} />
        <p>{NO_RELATIONS_MESSAGE}</p>
        <p>
          <Link to={`/nodes/${graph.data.rootNodeId}`} className="underline underline-offset-4">
            {CREATE_RELATION_LABEL}
          </Link>
        </p>
      </GraphShell>
    );
  }

  const updating = graph.isPlaceholderData;
  const mode: GraphViewMode = drawFailed ? "list" : view.view;
  const selectedNode = graph.data.nodes.find((node) => node.id === view.selected);
  return (
    <GraphShell title={localGraphTitle(rootTitle)} busy={updating}>
      <div className="flex flex-wrap items-end gap-6">
        <ViewModeControl mode={mode} onChange={showAs} />
        <DepthControl depth={view.depth} onChange={(depth) => setView({ depth })} />
        {mode === "canvas" && <ZoomControls zoom={zoom} onChange={setZoom} />}
        <Link to={`/nodes/${graph.data.rootNodeId}`} className="text-sm underline underline-offset-4">
          {OPEN_IN_INSPECTOR_LABEL}
        </Link>
      </div>
      <p role="status" className="text-sm text-muted-foreground">
        {updating ? GRAPH_UPDATING_MESSAGE : ""}
      </p>
      <AgentNews hasNewer={probe.hasNewer} onRefresh={refresh} />
      <p role="status" className="text-sm">
        {selectedGone ? SELECTED_GONE_MESSAGE : ""}
      </p>
      {graph.data.truncated && <p className="text-sm">{omittedMessage(graph.data.omittedNodeCount, graph.data.omittedEdgeCount)}</p>}
      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_24rem]">
        {/* tabIndex -1: focus comes back here when the Inspector closes or its Node goes away (FE-032). */}
        <div ref={viewRef} tabIndex={-1} className="min-w-0 space-y-4">
          <p role="alert" className="text-sm text-destructive">
            {drawFailed ? GRAPH_DRAW_ERROR_MESSAGE : ""}
          </p>
          {mode === "canvas" && (
            <p role="status" className="text-sm text-muted-foreground">
              {ZOOM_LEVEL_MESSAGES[zoomLevelOf(zoom)]}
            </p>
          )}
          <LocalGraphView
            mode={mode}
            graph={graph.data}
            rootTitle={rootTitle}
            selectedId={view.selected}
            zoom={zoom}
            typeNameOf={typeNameOf}
            onZoomChange={setZoom}
            onSelect={select}
            onReroot={reroot}
            onDrawingFailed={drawingFailed}
          />
          {mode === "canvas" && <GraphLegend graph={graph.data} typeNameOf={typeNameOf} />}
        </div>
        {view.selected && (
          <aside ref={inspectorRef} aria-labelledby="inspector-title" className="space-y-4 rounded-md border p-4">
            {selectedNode?.agentUnreviewed && (
              <p className="text-sm">
                <span aria-hidden="true">◎ </span>
                {SELECTED_AGENT_NOTICE}{" "}
                <Link to="/agent-changes?unreviewed=1" className="underline underline-offset-4">
                  {REVIEW_AGENT_CHANGES_LABEL}
                </Link>
              </p>
            )}
            <NodeInspector nodeId={view.selected} headingLevel={2} onClose={closeInspector} onNavigate={reroot} />
          </aside>
        )}
      </div>
    </GraphShell>
  );
}

/** The graph drawn or listed: the two views share the selection, the re-root and the texts (DA-142). */
function LocalGraphView({
  mode,
  graph,
  rootTitle,
  selectedId,
  zoom,
  typeNameOf,
  onZoomChange,
  onSelect,
  onReroot,
  onDrawingFailed,
}: {
  mode: GraphViewMode;
  graph: LocalGraph;
  rootTitle: string;
  selectedId: string | undefined;
  zoom: number;
  typeNameOf: TypeNameOf;
  onZoomChange: (zoom: number) => void;
  onSelect: (nodeId: string) => void;
  onReroot: (nodeId: string) => void;
  onDrawingFailed: () => void;
}) {
  const labels = useMemo(
    () => ({
      canvas: localGraphLabel(rootTitle, graph.nodes.length, graph.edges.length),
      agentUnreviewed: AGENT_UNREVIEWED_LABEL,
      nodeSummary: (node: LocalGraphNode) => nodeSummaryText(typeNameOf(node.typeId ?? null), node.degree),
    }),
    [rootTitle, graph.nodes.length, graph.edges.length, typeNameOf],
  );
  if (mode === "list") {
    return <GraphList graph={graph} rootId={graph.rootNodeId} selectedId={selectedId} highlightAgent onSelect={onSelect} onReroot={onReroot} labels={labels} />;
  }

  return (
    <GraphCanvas
      graph={graph}
      rootId={graph.rootNodeId}
      selectedId={selectedId}
      highlightAgent
      zoom={zoom}
      onZoomChange={onZoomChange}
      onSelect={onSelect}
      onReroot={onReroot}
      labels={labels}
      onUnavailable={onDrawingFailed}
      onRenderError={onDrawingFailed}
    />
  );
}

function ViewModeControl({ mode, onChange }: { mode: GraphViewMode; onChange: (mode: GraphViewMode) => void }) {
  return (
    <fieldset>
      <legend className="text-sm font-medium">{VIEW_MODE_LEGEND}</legend>
      <div className="mt-1 flex gap-4">
        {GRAPH_VIEW_MODES.map((option) => (
          <label key={option} className="flex items-center gap-1 text-sm">
            <input type="radio" name="view" value={option} checked={mode === option} onChange={() => onChange(option)} />
            {GRAPH_VIEW_MODE_LABELS[option]}
          </label>
        ))}
      </div>
    </fieldset>
  );
}

function DepthControl({ depth, onChange }: { depth: GraphDepth; onChange: (depth: GraphDepth) => void }) {
  return (
    <fieldset>
      <legend className="text-sm font-medium">{DEPTH_LEGEND}</legend>
      <div className="mt-1 flex gap-4">
        {GRAPH_DEPTHS.map((option) => (
          <label key={option} className="flex items-center gap-1 text-sm">
            <input type="radio" name="depth" value={option} checked={depth === option} onChange={() => onChange(option)} />
            {option}
          </label>
        ))}
      </div>
    </fieldset>
  );
}

/** The names of the Account's Types, for the legend and the near level's summary (DA-140). */
function useTypeNames(): TypeNameOf {
  const { data, isPending } = useQuery(typesQuery);
  return useCallback(
    (typeId: string | null) => {
      if (typeId === null) {
        return NO_TYPE_LABEL;
      }

      return data?.find((type) => type.id === typeId)?.name ?? (isPending ? TYPE_NAME_LOADING_LABEL : TYPE_UNAVAILABLE_LABEL);
    },
    [data, isPending],
  );
}

function ZoomControls({ zoom, onChange }: { zoom: number; onChange: (zoom: number) => void }) {
  return (
    <div role="group" aria-label={ZOOM_CONTROLS_LABEL} className="flex gap-2">
      <Button type="button" variant="secondary" size="sm" disabled={zoom >= MAX_ZOOM} onClick={() => onChange(steppedZoom(zoom, "in"))}>
        {ZOOM_IN_LABEL}
      </Button>
      <Button type="button" variant="secondary" size="sm" disabled={zoom <= MIN_ZOOM} onClick={() => onChange(steppedZoom(zoom, "out"))}>
        {ZOOM_OUT_LABEL}
      </Button>
      <Button type="button" variant="secondary" size="sm" onClick={() => onChange(INITIAL_ZOOM)}>
        {ZOOM_RESET_LABEL}
      </Button>
    </div>
  );
}

/** What size, line and colour mean on the drawing: none of it is said by colour alone (FE-034). */
function GraphLegend({ graph, typeNameOf }: { graph: LocalGraph; typeNameOf: TypeNameOf }) {
  const typeIds = [...new Set(graph.nodes.map((node) => node.typeId ?? null))];
  const hasSoft = graph.edges.some((edge) => edge.assertion === "soft");
  const hasAgentChanges = graph.nodes.some((node) => node.agentUnreviewed) || graph.edges.some((edge) => edge.agentUnreviewed);
  return (
    <section aria-labelledby="graph-legend-title" className="space-y-2 text-sm">
      <h2 id="graph-legend-title" className="font-medium">
        {LEGEND_TITLE}
      </h2>
      <ul className="flex flex-wrap gap-x-6 gap-y-2">
        <li>{LEGEND_SIZE_TEXT}</li>
        <li className="flex items-center gap-2">
          <EdgeSample assertion="hard" />
          {LEGEND_HARD_TEXT}
        </li>
        {hasSoft && (
          <li className="flex items-center gap-2">
            <EdgeSample assertion="soft" />
            {LEGEND_SOFT_TEXT}
          </li>
        )}
        {hasAgentChanges && (
          <li className="flex items-center gap-2">
            <svg aria-hidden="true" viewBox="0 0 16 16" className="size-4">
              <circle cx="8" cy="8" r="6" fill="none" stroke={AGENT_RING_COLOR} strokeWidth={AGENT_RING_WIDTH} />
              <circle cx="8" cy="8" r="3" fill={EDGE_COLOR} />
            </svg>
            {LEGEND_AGENT_TEXT}
          </li>
        )}
        {typeIds.map((typeId) => (
          <li key={typeId ?? NO_TYPE_LABEL} className="flex items-center gap-2">
            {/* The Type palette is the drawing's (graphVisuals), not a design token. */}
            <span aria-hidden="true" className="inline-block size-3 rounded-full" style={{ backgroundColor: typeColorOf(typeId) }} />
            {typeNameOf(typeId)}
          </li>
        ))}
      </ul>
    </section>
  );
}

const EDGE_SAMPLE_PATHS: Record<EdgeLine, string> = { straight: "M2 8 H30", curved: "M2 12 Q16 0 30 12" };

function EdgeSample({ assertion }: { assertion: LocalGraphEdge["assertion"] }) {
  const style = edgeStyleOf(assertion);
  return (
    <svg aria-hidden="true" viewBox="0 0 32 16" className="h-4 w-8">
      <path d={EDGE_SAMPLE_PATHS[style.line]} fill="none" stroke={EDGE_COLOR} strokeWidth={style.size} />
    </svg>
  );
}

/** "Há novidades de agentes — Atualizar": a live region, so the news is announced (DA-142). */
function AgentNews({ hasNewer, onRefresh }: { hasNewer: boolean; onRefresh: () => void }) {
  return (
    <div role="status" className="text-sm">
      {hasNewer && (
        <p className="flex flex-wrap items-center gap-2">
          {AGENT_NEWS_MESSAGE}
          <Button type="button" variant="secondary" size="sm" onClick={onRefresh}>
            {AGENT_NEWS_ACTION}
          </Button>
        </p>
      )}
    </div>
  );
}

/**
 * Selecting moves focus to the Inspector's heading once the panel shows that Node (FE-032).
 * Returns what to call on each selection, the same Node again included.
 */
function useFocusOnSelect(selected: string | undefined, inspectorRef: RefObject<HTMLElement | null>): (nodeId: string) => void {
  const requested = useRef<string | null>(null);
  const [request, setRequest] = useState(0);

  useEffect(() => {
    if (selected !== undefined && requested.current === selected) {
      requested.current = null;
      inspectorRef.current?.querySelector<HTMLElement>("#inspector-title")?.focus();
    }
  }, [selected, request, inspectorRef]);

  return useCallback((nodeId: string) => {
    requested.current = nodeId;
    setRequest((count) => count + 1);
  }, []);
}

type ShownGraph = { graph: LocalGraph | undefined; current: boolean; rootId: string; depth: GraphDepth; selected: string | undefined };

/**
 * A selected Node that a fresh read of the same graph no longer has went away (deleted, hidden):
 * the selection is cleared (DA-142). Another depth or root is not a reason, and a deep link to a
 * Node outside the graph keeps the Inspector, whose gone state covers a deleted one.
 */
function useClearVanishedSelection(shown: ShownGraph, onVanished: () => void) {
  const lastShown = useRef<{ key: string; nodeIds: Set<string> } | null>(null);
  const vanished = useRef(onVanished);
  useEffect(() => {
    vanished.current = onVanished;
  });

  const { graph, current, rootId, depth, selected } = shown;
  useEffect(() => {
    if (!graph || !current) {
      return;
    }

    const key = `${rootId}:${depth}`;
    const nodeIds = new Set(graph.nodes.map((node) => node.id));
    const wasShown = lastShown.current?.key === key && selected !== undefined && lastShown.current.nodeIds.has(selected);
    lastShown.current = { key, nodeIds };
    if (wasShown && selected !== undefined && !nodeIds.has(selected)) {
      vanished.current();
    }
  }, [graph, current, rootId, depth, selected]);
}

function GraphShell({ title, busy = false, children }: { title: string; busy?: boolean; children: ReactNode }) {
  return (
    <section aria-labelledby="graph-title" aria-busy={busy} className="space-y-4">
      <h1 id="graph-title" className="text-2xl font-semibold break-words">
        {title}
      </h1>
      {children}
    </section>
  );
}

function NotFoundState() {
  return (
    <GraphShell title={NODE_NOT_FOUND_TITLE}>
      <p>
        <Link to="/nodes" className="underline underline-offset-4">
          {BACK_TO_NODES_LABEL}
        </Link>
      </p>
    </GraphShell>
  );
}
