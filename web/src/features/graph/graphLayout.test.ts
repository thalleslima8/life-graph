import { describe, expect, it } from "vitest";
import { aRenderableGraphModel } from "@/test/fakeGraphRenderer";
import { NODE_ID, OTHER_NODE_ID } from "@/test/graphFixtures";
import { NEIGHBOR_OFFSET, placeNodes, RING_SPACING } from "./graphLayout";
import type { RenderableGraph } from "./graphShape";

const THIRD_ID = "01920000-0000-7000-8000-000000000003";

/** The root, one neighbor and, unless left out, a Node two Relations away behind it. */
function aChain({ withThird = true } = {}): RenderableGraph {
  const model = aRenderableGraphModel();
  return {
    ...model,
    nodes: [
      ...model.nodes,
      ...(withThird ? [{ id: THIRD_ID, title: "Estoicismo", typeName: null, typeId: null, distance: 2, degree: 1, agentUnreviewed: false }] : []),
    ],
    edges: [
      ...model.edges,
      ...(withThird
        ? [{ id: "e2", sourceNodeId: OTHER_NODE_ID, targetNodeId: THIRD_ID, kind: "cita", assertion: "hard" as const, strength: 1, agentUnreviewed: false }]
        : []),
    ],
  };
}

describe("graph layout", () => {
  it("puts the root at the centre and each distance on its own ring, the same each time", () => {
    const positions = placeNodes(aChain(), NODE_ID);

    expect(positions.get(NODE_ID)).toEqual({ x: 0, y: 0 });
    expect(Math.hypot(positions.get(OTHER_NODE_ID)?.x ?? 0, positions.get(OTHER_NODE_ID)?.y ?? 0)).toBeCloseTo(RING_SPACING);
    expect(Math.hypot(positions.get(THIRD_ID)?.x ?? 0, positions.get(THIRD_ID)?.y ?? 0)).toBeCloseTo(2 * RING_SPACING);
    expect(placeNodes(aChain(), NODE_ID)).toEqual(positions);
  });

  it("keeps a placed Node where it is and starts a new one next to its placed neighbor", () => {
    const placed = new Map([
      [NODE_ID, { x: 7, y: 7 }],
      [OTHER_NODE_ID, { x: 40, y: -10 }],
    ]);

    const positions = placeNodes(aChain(), NODE_ID, placed);

    expect([positions.get(NODE_ID), positions.get(OTHER_NODE_ID)]).toEqual([placed.get(NODE_ID), placed.get(OTHER_NODE_ID)]);
    const third = positions.get(THIRD_ID) ?? { x: 0, y: 0 };
    expect(Math.hypot(third.x - 40, third.y + 10)).toBeCloseTo(NEIGHBOR_OFFSET);
  });

  it("places only the Nodes of the graph it is given", () => {
    const positions = placeNodes(aChain({ withThird: false }), NODE_ID, new Map([[THIRD_ID, { x: 1, y: 1 }]]));

    expect([...positions.keys()]).toEqual([NODE_ID, OTHER_NODE_ID]);
  });
});
