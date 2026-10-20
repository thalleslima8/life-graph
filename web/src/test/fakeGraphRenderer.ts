import type { GraphRendererEvents, GraphRendererFactory, GraphRenderView } from "@/features/graph/graphRendererPort";
import type { LocalGraph } from "@/features/graph/localGraph";
import { aLocalGraph } from "./graphFixtures";

/**
 * A GraphRenderer that draws nothing and records what it was asked: jsdom has no WebGL
 * (DA-137). One instance, shared by a test and the module it mocks; reset before each test.
 */
function createFakeGraphRenderer() {
  const recorded = {
    mounts: [] as { container: HTMLElement; view: GraphRenderView }[],
    updates: [] as GraphRenderView[],
    disposed: 0,
    events: undefined as GraphRendererEvents | undefined,
    failMount: false,
    failUpdate: false,
  };
  const factory: GraphRendererFactory = () => ({
    mount: (container, view, events) => {
      if (recorded.failMount) {
        throw new Error("WebGL is not available.");
      }

      recorded.mounts.push({ container, view });
      recorded.events = events;
    },
    update: (view) => {
      if (recorded.failUpdate) {
        throw new Error("The WebGL context was lost.");
      }

      recorded.updates.push(view);
    },
    dispose: () => {
      recorded.disposed += 1;
    },
  });
  return {
    recorded,
    factory,
    /** What the person does on the drawing, as the renderer would report it. */
    events(): GraphRendererEvents {
      if (!recorded.events) {
        throw new Error("No renderer was mounted.");
      }

      return recorded.events;
    },
    reset() {
      Object.assign(recorded, { mounts: [], updates: [], disposed: 0, events: undefined, failMount: false, failUpdate: false });
    },
  };
}

export const fakeGraphRenderer = createFakeGraphRenderer();

/** The local graph as the SPA holds it, after the query read it. */
export function aLocalGraphModel(): LocalGraph {
  const wire = aLocalGraph();
  return {
    ...wire,
    nodes: wire.nodes.map((node) => ({ ...node, distance: Number(node.distance), degree: Number(node.degree) })),
    edges: wire.edges.map((edge) => ({ ...edge, strength: Number(edge.strength) })),
    omittedNodeCount: 0,
    omittedEdgeCount: 0,
  };
}
