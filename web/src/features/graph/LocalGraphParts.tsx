import { useMemo } from "react";
import { Button } from "@/components/ui/button";
import { SharedBadge } from "@/features/sharing/SharedBadge";
import { sharedBadgeStateOf, useSharedNodeIds } from "@/features/sharing/sharesData";
import { GraphCanvas } from "./GraphCanvas";
import type { TypeNameOf } from "./graphData";
import { GraphList } from "./GraphList";
import {
  AGENT_UNREVIEWED_LABEL,
  LEGEND_AGENT_TEXT,
  LEGEND_HARD_TEXT,
  LEGEND_SIZE_TEXT,
  LEGEND_SOFT_TEXT,
  LEGEND_TITLE,
  NO_TYPE_LABEL,
  ZOOM_CONTROLS_LABEL,
  ZOOM_IN_LABEL,
  ZOOM_OUT_LABEL,
  ZOOM_RESET_LABEL,
  localGraphLabel,
  nodeSummaryText,
} from "./graphMessages";
import type { RenderableNode } from "./graphShape";
import { INITIAL_ZOOM, MAX_ZOOM, MIN_ZOOM, steppedZoom } from "./graphView";
import { AGENT_RING_COLOR, AGENT_RING_WIDTH, EDGE_COLOR, edgeStyleOf, typeColorOf, type EdgeLine } from "./graphVisuals";
import { renderableGraphOf, type GraphViewMode, type LocalGraph, type LocalGraphEdge } from "./localGraph";

/**
 * The pieces of the local graph shared by the graph page and the Home (DA-318): the graph drawn
 * or listed, the zoom buttons and the legend.
 */

/** The graph drawn or listed: the two views share the selection, the re-root and the texts (DA-142). */
export function LocalGraphView({
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
  const shown = useMemo(() => renderableGraphOf(graph, typeNameOf), [graph, typeNameOf]);
  const labels = useMemo(
    () => ({
      canvas: localGraphLabel(rootTitle, graph.nodes.length, graph.edges.length),
      agentUnreviewed: AGENT_UNREVIEWED_LABEL,
      nodeSummary: (node: RenderableNode) => nodeSummaryText(node.typeName ?? NO_TYPE_LABEL, node.degree ?? 0),
    }),
    [rootTitle, graph.nodes.length, graph.edges.length],
  );
  // Only the list shows the badge, so only the list asks about the Nodes on screen (DA-299).
  const shared = useSharedNodeIds(mode === "list" ? shown.nodes.map((node) => node.id) : []);
  if (mode === "list") {
    return (
      <GraphList
        graph={shown}
        selectedId={selectedId}
        highlightAgent
        onSelect={onSelect}
        onReroot={onReroot}
        labels={labels}
        nodeBadge={(nodeId) => <SharedBadge state={sharedBadgeStateOf(nodeId, shared)} />}
      />
    );
  }

  return (
    <GraphCanvas
      graph={shown}
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

export function ZoomControls({ zoom, onChange }: { zoom: number; onChange: (zoom: number) => void }) {
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
export function GraphLegend({ graph, typeNameOf }: { graph: LocalGraph; typeNameOf: TypeNameOf }) {
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
