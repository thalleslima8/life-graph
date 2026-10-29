import { useQuery } from "@tanstack/react-query";
import { useLayoutEffect, useMemo, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { GraphCanvas } from "@/features/graph/GraphCanvas";
import { GraphList } from "@/features/graph/GraphList";
import type { RenderableNode } from "@/features/graph/graphShape";
import { INITIAL_ZOOM, steppedZoom } from "@/features/graph/graphView";
import { relationText } from "@/features/graph/graphViewMessages";
import { ReportAbuseLink } from "./ReportAbuseLink";
import { fetchSharedGraph, fetchSharedNode } from "./visitorApi";
import {
  VisitorNodeInspector,
  type VisitorInspectedNode,
} from "./VisitorNodeInspector";
import {
  LINK_UNAVAILABLE_MESSAGE,
  LINK_UNAVAILABLE_TITLE,
  sharedGraphLabel,
  VISITOR_CANVAS_LABEL,
  VISITOR_DRAW_ERROR_MESSAGE,
  VISITOR_LIST_LABEL,
  VISITOR_LOADING_MESSAGE,
  VISITOR_NO_TYPE_LABEL,
  VISITOR_VIEW_MODE_LEGEND,
  VISITOR_ZOOM_CONTROLS_LABEL,
  VISITOR_ZOOM_IN_LABEL,
  VISITOR_ZOOM_OUT_LABEL,
  VISITOR_ZOOM_RESET_LABEL,
} from "./visitorMessages";

type ViewMode = "canvas" | "list";

/** The link's token, from the fragment of `/s#<token>` (DA-221); empty when there is none. */
function tokenInFragment(): string {
  return window.location.hash.slice(1);
}

// The selection is not written to the URL (FE-012 does not apply): the page only opens with the
// token, which leaves the URL on arrival, so no URL of it is shareable.
const sharedKeys = {
  graph: ["shared", "graph"] as const,
  node: (nodeId: string) => ["shared", "node", nodeId] as const,
};

/**
 * A SharedGraphView as its visitor sees it (DA-077, DA-221, DA-222): the token comes from the
 * fragment and leaves the address bar before the first paint, and travels only in the
 * Authorization header. Read-only: the list, the drawing without re-rooting and the Inspector.
 * Every failure, a suspended root included, is the same neutral "link indisponível".
 */
export function VisitorPage() {
  const [token] = useState(tokenInFragment);
  useLayoutEffect(() => {
    if (window.location.hash !== "") {
      window.history.replaceState(
        window.history.state,
        "",
        `${window.location.pathname}${window.location.search}`,
      );
    }
  }, []);

  const shared = useQuery({
    queryKey: sharedKeys.graph,
    queryFn: ({ signal }) => fetchSharedGraph(token, signal),
    enabled: token !== "",
    retry: false,
  });
  const [selectedId, setSelectedId] = useState<string>();
  const [mode, setMode] = useState<ViewMode>("canvas");
  const [drawFailed, setDrawFailed] = useState(false);
  const [zoom, setZoom] = useState(INITIAL_ZOOM);
  const selected = useQuery({
    queryKey: sharedKeys.node(selectedId ?? ""),
    queryFn: ({ signal }) => fetchSharedNode(token, selectedId ?? "", signal),
    enabled: token !== "" && selectedId !== undefined,
    retry: false,
  });

  const graph = shared.data?.graph;
  const rootTitle =
    graph?.nodes.find((node) => node.id === graph.rootId)?.title ?? "";
  const labels = useMemo(
    () => ({
      canvas: sharedGraphLabel(rootTitle, graph?.nodes.length ?? 0),
      agentUnreviewed: "",
      nodeSummary: (node: RenderableNode) =>
        node.typeName ?? VISITOR_NO_TYPE_LABEL,
    }),
    [rootTitle, graph?.nodes.length],
  );
  const inspected = useMemo<VisitorInspectedNode>(() => {
    if (selected.isError) {
      return { status: "unavailable" };
    }

    if (!selected.data) {
      return { status: "loading" };
    }

    const titleOf = (nodeId: string) =>
      graph?.nodes.find((node) => node.id === nodeId)?.title ?? "";
    return {
      status: "ready",
      details: selected.data,
      relations: selected.data.relations.map((relation) => ({
        id: relation.id,
        text: relationText(
          titleOf(relation.sourceNodeId),
          relation.kind,
          titleOf(relation.targetNodeId),
        ),
      })),
    };
  }, [selected.isError, selected.data, graph]);

  if (
    token === "" ||
    shared.isError ||
    (shared.data && shared.data.graph.nodes.length === 0)
  ) {
    return (
      <VisitorLayout>
        <h1 className="text-2xl font-semibold">{LINK_UNAVAILABLE_TITLE}</h1>
        <p role="alert">{LINK_UNAVAILABLE_MESSAGE}</p>
      </VisitorLayout>
    );
  }

  if (!graph) {
    return (
      <VisitorLayout>
        <p role="status" className="text-muted-foreground">
          {VISITOR_LOADING_MESSAGE}
        </p>
      </VisitorLayout>
    );
  }

  const shownMode: ViewMode = drawFailed ? "list" : mode;
  return (
    <VisitorLayout publicRef={shared.data?.publicRef}>
      <h1 className="text-2xl font-semibold">{rootTitle}</h1>
      <fieldset className="flex flex-wrap items-center gap-4">
        <legend className="sr-only">{VISITOR_VIEW_MODE_LEGEND}</legend>
        {(["canvas", "list"] as const).map((option) => (
          <label key={option} className="flex items-center gap-2 text-sm">
            <input
              type="radio"
              name="visitor-view-mode"
              value={option}
              checked={shownMode === option}
              disabled={option === "canvas" && drawFailed}
              onChange={() => setMode(option)}
            />
            {option === "canvas" ? VISITOR_CANVAS_LABEL : VISITOR_LIST_LABEL}
          </label>
        ))}
      </fieldset>
      {drawFailed && <p role="status">{VISITOR_DRAW_ERROR_MESSAGE}</p>}
      <div className="grid gap-6 lg:grid-cols-[2fr_1fr]">
        <div className="space-y-2">
          {shownMode === "canvas" ? (
            <>
              <div
                role="group"
                aria-label={VISITOR_ZOOM_CONTROLS_LABEL}
                className="flex gap-2"
              >
                <Button
                  type="button"
                  variant="secondary"
                  size="sm"
                  onClick={() => setZoom(steppedZoom(zoom, "in"))}
                >
                  {VISITOR_ZOOM_IN_LABEL}
                </Button>
                <Button
                  type="button"
                  variant="secondary"
                  size="sm"
                  onClick={() => setZoom(steppedZoom(zoom, "out"))}
                >
                  {VISITOR_ZOOM_OUT_LABEL}
                </Button>
                <Button
                  type="button"
                  variant="secondary"
                  size="sm"
                  onClick={() => setZoom(INITIAL_ZOOM)}
                >
                  {VISITOR_ZOOM_RESET_LABEL}
                </Button>
              </div>
              <GraphCanvas
                graph={graph}
                selectedId={selectedId}
                highlightAgent={false}
                zoom={zoom}
                onZoomChange={setZoom}
                onSelect={setSelectedId}
                labels={labels}
                onUnavailable={() => setDrawFailed(true)}
                onRenderError={() => setDrawFailed(true)}
              />
            </>
          ) : (
            <GraphList
              graph={graph}
              selectedId={selectedId}
              onSelect={setSelectedId}
              labels={labels}
            />
          )}
        </div>
        {selectedId !== undefined && (
          <VisitorNodeInspector
            node={inspected}
            headingLevel={2}
            onClose={() => setSelectedId(undefined)}
          />
        )}
      </div>
    </VisitorLayout>
  );
}

/** Every state of the page, the neutral "indisponível" included, carries "Denunciar esta página" (DA-218). */
function VisitorLayout({
  publicRef,
  children,
}: {
  publicRef?: string | undefined;
  children: ReactNode;
}) {
  return (
    <div className="mx-auto max-w-6xl space-y-6 px-4 py-6">
      <main className="space-y-6">{children}</main>
      <footer className="border-t pt-4">
        <ReportAbuseLink publicRef={publicRef} />
      </footer>
    </div>
  );
}
