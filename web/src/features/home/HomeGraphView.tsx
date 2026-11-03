import { useState } from "react";
import { useNavigate } from "react-router";
import { useTypeNames } from "@/features/graph/graphData";
import { GRAPH_DRAW_ERROR_MESSAGE, ZOOM_LEVEL_MESSAGES, omittedMessage } from "@/features/graph/graphMessages";
import { graphPath, INITIAL_ZOOM, zoomLevelOf } from "@/features/graph/graphView";
import { GraphLegend, LocalGraphView, ZoomControls } from "@/features/graph/LocalGraphParts";
import { graphViewPath, type LocalGraph } from "@/features/graph/localGraph";
import { HOME_GRAPH_DEPTH } from "./homeData";

/**
 * The root's graph drawn, or listed when the drawing fails. Selecting or re-rooting opens the
 * graph page (DA-315): the Home has no Inspector and no depth of its own.
 */
export function HomeGraphView({ rootId, rootTitle, graph }: { rootId: string; rootTitle: string; graph: LocalGraph }) {
  const typeNameOf = useTypeNames();
  const navigate = useNavigate();
  const [zoom, setZoom] = useState(INITIAL_ZOOM);
  const [drawFailed, setDrawFailed] = useState(false);
  const mode = drawFailed ? "list" : "canvas";

  const select = (selected: string) => void navigate(graphViewPath(rootId, { depth: HOME_GRAPH_DEPTH, selected }));

  const reroot = (nodeId: string) => void navigate(graphPath(nodeId));

  return (
    <div className="min-w-0 space-y-4">
      {mode === "canvas" && <ZoomControls zoom={zoom} onChange={setZoom} />}
      {graph.truncated && <p className="text-sm">{omittedMessage(graph.omittedNodeCount, graph.omittedEdgeCount)}</p>}
      {drawFailed && (
        <p role="alert" className="text-sm text-destructive">
          {GRAPH_DRAW_ERROR_MESSAGE}
        </p>
      )}
      {mode === "canvas" && (
        <p role="status" className="text-sm text-muted-foreground">
          {ZOOM_LEVEL_MESSAGES[zoomLevelOf(zoom)]}
        </p>
      )}
      <LocalGraphView
        mode={mode}
        graph={graph}
        rootTitle={rootTitle}
        selectedId={undefined}
        zoom={zoom}
        typeNameOf={typeNameOf}
        onZoomChange={setZoom}
        onSelect={select}
        onReroot={reroot}
        onDrawingFailed={() => setDrawFailed(true)}
      />
      {mode === "canvas" && <GraphLegend graph={graph} typeNameOf={typeNameOf} />}
    </div>
  );
}
