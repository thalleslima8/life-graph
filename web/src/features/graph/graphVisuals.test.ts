import { describe, expect, it } from "vitest";
import { TYPE_ID } from "@/test/graphFixtures";
import {
  AGENT_RING_COLOR,
  DEGREE_CAP,
  EDGE_COLOR,
  edgeStyleOf,
  HARD_EDGE_SIZE,
  MAX_NODE_SIZE,
  MIN_NODE_SIZE,
  nodeSizeOf,
  SOFT_EDGE_SIZE,
  TYPE_COLORS,
  typeColorOf,
  UNTYPED_COLOR,
} from "./graphVisuals";

/** WCAG 2.x contrast ratio of a #rrggbb colour on white. */
function contrastOnWhite(hex: string): number {
  const channels = [1, 3, 5].map((start) => Number.parseInt(hex.slice(start, start + 2), 16) / 255);
  const [red = 0, green = 0, blue = 0] = channels.map((channel) => (channel <= 0.03928 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4));
  const luminance = 0.2126 * red + 0.7152 * green + 0.0722 * blue;
  return 1.05 / (luminance + 0.05);
}

describe("graph visuals", () => {
  it("grows a Node with its Relations, from the minimum to a cap a hub does not pass", () => {
    const sizes = [0, 1, 8, DEGREE_CAP, 1000].map((degree) => nodeSizeOf(degree, 0));

    expect([sizes[0], sizes[3], sizes[4]]).toEqual([MIN_NODE_SIZE, MAX_NODE_SIZE, MAX_NODE_SIZE]);
    expect(sizes).toEqual([...sizes].sort((smaller, larger) => smaller - larger));
    expect(new Set(sizes.slice(0, 4)).size).toBe(4);
  });

  it("draws a Node smaller the farther it is from the root", () => {
    const sizes = [0, 1, 2, 3, 7].map((distance) => nodeSizeOf(8, distance));

    expect(sizes.slice(0, 4)).toEqual([...sizes.slice(0, 4)].sort((smaller, larger) => larger - smaller));
    expect(new Set(sizes.slice(0, 4)).size).toBe(4);
    expect(sizes[4]).toBe(sizes[3]);
  });

  it("tells a Soft Relation from a Hard one by line and thickness", () => {
    expect([edgeStyleOf("hard"), edgeStyleOf("soft")]).toEqual([
      { line: "straight", size: HARD_EDGE_SIZE },
      { line: "curved", size: SOFT_EDGE_SIZE },
    ]);
    expect(SOFT_EDGE_SIZE).toBeLessThan(HARD_EDGE_SIZE);
  });

  it("gives a Type the same colour from its palette every time, and a Node without one its own", () => {
    expect(typeColorOf(TYPE_ID)).toBe(typeColorOf(TYPE_ID));
    expect(TYPE_COLORS).toContain(typeColorOf(TYPE_ID));
    expect(typeColorOf(null)).toBe(UNTYPED_COLOR);
  });

  it("keeps every colour at 3:1 or more on the white canvas", () => {
    for (const color of [...TYPE_COLORS, UNTYPED_COLOR, EDGE_COLOR, AGENT_RING_COLOR]) {
      expect(contrastOnWhite(color), color).toBeGreaterThanOrEqual(3);
    }
  });
});
