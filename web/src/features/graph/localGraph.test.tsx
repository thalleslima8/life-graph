import { QueryClient } from "@tanstack/react-query";
import { act, render } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { createMemoryRouter, RouterProvider } from "react-router";
import { describe, expect, it } from "vitest";
import { aLocalGraph, NODE_ID, OTHER_NODE_ID } from "@/test/graphFixtures";
import { server } from "@/test/msw/server";
import {
  FAR_TITLED_DISTANCE,
  graphPath,
  INITIAL_ZOOM,
  levelOfDetail,
  localGraphQuery,
  MAX_ZOOM,
  MIDDLE_ZOOM,
  MIN_ZOOM,
  NEAR_ZOOM,
  steppedZoom,
  useGraphView,
  zoomLevelOf,
  type GraphView,
} from "./localGraph";

/** Renders the hook on the graph route, at `path`, and hands back what it returns. */
function renderGraphView(path: string) {
  let latest: ReturnType<typeof useGraphView> | undefined;
  function Probe() {
    latest = useGraphView();
    return null;
  }

  const router = createMemoryRouter([{ path: "/nodes/:nodeId/graph", Component: Probe }], { initialEntries: [path] });
  render(<RouterProvider router={router} />);
  const current = () => {
    if (!latest) {
      throw new Error("The hook did not render.");
    }

    return latest;
  };
  return {
    router,
    view: () => current()[0],
    set: (next: Partial<GraphView>) => act(() => current()[1](next)),
  };
}

describe("graph view in the URL", () => {
  it("reads depth 2 on the canvas, with nothing selected, from a bare URL", () => {
    const { view } = renderGraphView(graphPath(NODE_ID));

    expect(view()).toEqual({ depth: 2, view: "canvas" });
  });

  it("reads each valid value, and an invalid one as its default without resetting the others", () => {
    expect(renderGraphView(`${graphPath(NODE_ID)}?depth=3&view=list&selected=${OTHER_NODE_ID}`).view()).toEqual({
      depth: 3,
      view: "list",
      selected: OTHER_NODE_ID,
    });
    expect(renderGraphView(`${graphPath(NODE_ID)}?depth=9&view=list&selected=nope`).view()).toEqual({ depth: 2, view: "list" });
    expect(renderGraphView(`${graphPath(NODE_ID)}?depth=abc&view=3d`).view()).toEqual({ depth: 2, view: "canvas" });
  });

  it("writes only what differs from the default, as a new history entry", () => {
    const { router, set } = renderGraphView(graphPath(NODE_ID));

    set({ depth: 3 });
    expect([router.state.location.search, router.state.historyAction]).toEqual(["?depth=3", "PUSH"]);

    set({ depth: 2, view: "list" });
    expect(router.state.location.search).toBe("?view=list");
  });

  it("writes the selection in place of the current entry, and clears it", () => {
    const { router, set, view } = renderGraphView(`${graphPath(NODE_ID)}?depth=1`);

    set({ selected: OTHER_NODE_ID });
    expect([router.state.location.search, router.state.historyAction]).toEqual([`?depth=1&selected=${OTHER_NODE_ID}`, "REPLACE"]);

    set({ selected: undefined });
    expect(view()).toEqual({ depth: 1, view: "canvas" });
  });

  it("names the graph page of a Node", () => {
    expect(graphPath(NODE_ID)).toBe(`/nodes/${NODE_ID}/graph`);
  });
});

describe("local graph query", () => {
  it("asks for the depth and reads the counts as numbers", async () => {
    const asked: string[] = [];
    server.use(
      http.get("/api/nodes/:nodeId/graph", ({ request }) => {
        asked.push(new URL(request.url).search);
        return HttpResponse.json({ ...aLocalGraph({ truncated: true }), omittedNodeCount: "12", omittedEdgeCount: "3" });
      }),
    );

    const graph = await new QueryClient().fetchQuery(localGraphQuery(NODE_ID, 3));

    expect(asked).toEqual(["?depth=3"]);
    expect([graph.omittedNodeCount, graph.omittedEdgeCount, graph.nodes[1]?.distance]).toEqual([12, 3, 1]);
  });
});

describe("Semantic Zoom", () => {
  it("shows more as the zoom gets closer, from each threshold on", () => {
    expect([MIDDLE_ZOOM - 0.01, MIDDLE_ZOOM, NEAR_ZOOM - 0.01, NEAR_ZOOM, MAX_ZOOM].map(zoomLevelOf)).toEqual(["far", "middle", "middle", "near", "near"]);
  });

  it("keeps the titles of the root and its direct neighbors when far, and dots beyond them", () => {
    expect([0, FAR_TITLED_DISTANCE, FAR_TITLED_DISTANCE + 1, 3].map((distance) => levelOfDetail(INITIAL_ZOOM, distance))).toEqual(["middle", "middle", "far", "far"]);
  });

  it("gives every distance the same level once the zoom is middle or near", () => {
    expect([0, 1, 2, 3].map((distance) => [levelOfDetail(MIDDLE_ZOOM, distance), levelOfDetail(NEAR_ZOOM, distance)])).toEqual(Array(4).fill(["middle", "near"]));
  });

  it("steps the zoom by a factor, never past its bounds", () => {
    expect([steppedZoom(INITIAL_ZOOM, "in"), steppedZoom(1.5, "out"), steppedZoom(MAX_ZOOM, "in"), steppedZoom(MIN_ZOOM, "out")]).toEqual([1.5, 1, MAX_ZOOM, MIN_ZOOM]);
  });
});
