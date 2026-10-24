import { describe, expect, it } from "vitest";
import { aLocalGraphModel } from "@/test/fakeGraphRenderer";
import { buildGraphIndex, isArrowKey, keyAction, nextFocus } from "./graphNavigation";
import type { LocalGraph, LocalGraphEdge, LocalGraphNode } from "./localGraph";

const ROOT = "root";

function aNodeAt(id: string, title: string, distance: number): LocalGraphNode {
  return { id, title, typeId: null, distance, degree: 1, agentUnreviewed: false };
}

function aRelation(id: string, sourceNodeId: string, targetNodeId: string): LocalGraphEdge {
  const [template] = aLocalGraphModel().edges;
  if (!template) {
    throw new Error("The model graph has a Relation.");
  }

  return { ...template, id, sourceNodeId, targetNodeId };
}

/**
 * Root ─ Zeno, Root ─ Aristóteles, Zeno ─ Crisipo, Aristóteles ─ Zeno (same distance), and a
 * Relation of Crisipo to itself.
 */
function aGraph(): LocalGraph {
  return {
    ...aLocalGraphModel(),
    rootNodeId: ROOT,
    nodes: [aNodeAt("crisipo", "Crisipo", 2), aNodeAt("zeno", "Zeno", 1), aNodeAt(ROOT, "Raiz", 0), aNodeAt("aristoteles", "Aristóteles", 1)],
    edges: [
      aRelation("e1", ROOT, "zeno"),
      aRelation("e2", "aristoteles", ROOT),
      aRelation("e3", "zeno", "crisipo"),
      aRelation("e4", "aristoteles", "zeno"),
      aRelation("e5", "crisipo", "crisipo"),
    ],
  };
}

describe("graph index", () => {
  it("orders the Nodes by distance from the root, then by title", () => {
    const index = buildGraphIndex(aGraph(), ROOT);

    expect(index.order).toEqual([ROOT, "aristoteles", "zeno", "crisipo"]);
    expect([index.distance("crisipo"), index.distance("missing")]).toEqual([2, undefined]);
  });

  it("lists each Node's neighbors once, either direction, in reading order and never itself", () => {
    const index = buildGraphIndex(aGraph(), ROOT);

    expect([index.neighbors(ROOT), index.neighbors("zeno"), index.neighbors("crisipo")]).toEqual([
      ["aristoteles", "zeno"],
      [ROOT, "aristoteles", "crisipo"],
      ["zeno"],
    ]);
  });

  it("refuses a root that is not in the graph", () => {
    expect(() => buildGraphIndex(aGraph(), "missing")).toThrow("The root missing is not in the graph.");
  });
});

describe("next focus", () => {
  const index = buildGraphIndex(aGraph(), ROOT);

  it("walks every Node in reading order with ↓ and ↑, stopping at the ends", () => {
    expect([nextFocus(index, ROOT, "ArrowDown"), nextFocus(index, "zeno", "ArrowDown"), nextFocus(index, "crisipo", "ArrowDown")]).toEqual([
      "aristoteles",
      "crisipo",
      "crisipo",
    ]);
    expect([nextFocus(index, "aristoteles", "ArrowUp"), nextFocus(index, ROOT, "ArrowUp")]).toEqual([ROOT, ROOT]);
  });

  it("goes to a neighbor farther from the root with → and nearer it with ←, staying where none is", () => {
    expect([nextFocus(index, ROOT, "ArrowRight"), nextFocus(index, "zeno", "ArrowRight"), nextFocus(index, "crisipo", "ArrowRight")]).toEqual([
      "aristoteles",
      "crisipo",
      "crisipo",
    ]);
    expect([nextFocus(index, "crisipo", "ArrowLeft"), nextFocus(index, "zeno", "ArrowLeft"), nextFocus(index, ROOT, "ArrowLeft")]).toEqual([
      "zeno",
      ROOT,
      ROOT,
    ]);
  });

  it("starts from the root when the focus is on a Node no longer in the graph", () => {
    expect(nextFocus(index, "gone", "ArrowDown")).toBe(ROOT);
  });
});

describe("key action", () => {
  it("selects on Enter, re-roots on Shift+Enter, zooms on + − 0 and maps Escape to clear (back to the selected Node or the root)", () => {
    expect([["Enter", false], ["Enter", true], ["+", true], ["-", false], ["0", false], ["Escape", false]].map(([key, shift]) => keyAction(String(key), Boolean(shift)))).toEqual([
      "select",
      "reroot",
      "zoomIn",
      "zoomOut",
      "zoomReset",
      "clear",
    ]);
  });

  it("does nothing on any other key, and tells the arrows apart", () => {
    expect([keyAction("a", false), keyAction("Tab", true), keyAction("ArrowUp", false)]).toEqual([null, null, null]);
    expect([isArrowKey("ArrowLeft"), isArrowKey("Enter")]).toEqual([true, false]);
  });
});
