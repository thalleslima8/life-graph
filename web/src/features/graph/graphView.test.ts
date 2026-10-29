import { describe, expect, it } from "vitest";
import { NODE_ID } from "@/test/graphFixtures";
import {
  FAR_TITLED_DISTANCE,
  graphPath,
  INITIAL_ZOOM,
  levelOfDetail,
  MAX_ZOOM,
  MIDDLE_ZOOM,
  MIN_ZOOM,
  NEAR_ZOOM,
  steppedZoom,
  zoomLevelOf,
} from "./graphView";

describe("graph page path", () => {
  it("names the graph page of a Node", () => {
    expect(graphPath(NODE_ID)).toBe(`/nodes/${NODE_ID}/graph`);
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
