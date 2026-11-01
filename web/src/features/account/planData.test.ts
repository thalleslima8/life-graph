import { describe, expect, it } from "vitest";
import { ApiError } from "@/api/client";
import { aPlan } from "@/test/planFixtures";
import { refusedLimitText } from "./limitMessages";
import { isPlanLimitReached, limitStatus, PLAN_LIMIT_REACHED_CODE, refusedLimitOf } from "./planData";

const refusal = (kind: string, limit = "10") =>
  new ApiError(422, PLAN_LIMIT_REACHED_CODE, "Refused", { details: { kind: [kind], limit: [limit] } });

describe("a creation refused past the plan's limit", () => {
  it("is recognized, of its own kind only", () => {
    expect(isPlanLimitReached(refusal("collections"))).toBe(true);
    expect(isPlanLimitReached(refusal("collections"), "collections")).toBe(true);
    expect(isPlanLimitReached(refusal("collections"), "nodes")).toBe(false);
  });

  it("is not any other refusal", () => {
    expect(isPlanLimitReached(new ApiError(422, "graph.node_title_required", "Refused"))).toBe(false);
    expect(isPlanLimitReached(new Error("network"))).toBe(false);
    expect(isPlanLimitReached(null)).toBe(false);
  });

  it("carries the limit when the server sends one", () => {
    expect(refusedLimitOf(refusal("nodes", "500"))).toBe(500);
    expect(refusedLimitOf(refusal("nodes", "many"))).toBeUndefined();
    expect(refusedLimitOf(new ApiError(422, "other", "Refused"))).toBeUndefined();
  });

  it("is told with the limit and with what stays possible (DA-086)", () => {
    const text = refusedLimitText(refusal("collections"));
    expect(text).toContain("10 de 10 Collections");
    expect(text).toContain("Nada foi bloqueado");
    expect(refusedLimitText(refusal("unknown_kind"))).toContain("O limite do seu plano foi atingido.");
    expect(refusedLimitText(new ApiError(422, "other", "Refused"))).toBeUndefined();
  });
});

describe("the status of one limit", () => {
  it("is reached when the usage meets the limit, never past a plan without limit", () => {
    expect(limitStatus(aPlan({ used: { collections: 10 } }), "collections")).toEqual({
      kind: "collections",
      used: 10,
      max: 10,
      reached: true,
    });
    expect(limitStatus(aPlan({ used: { collections: 9 } }), "collections")?.reached).toBe(false);
    expect(limitStatus(aPlan({ used: { nodes: 9000 }, max: { nodes: null } }), "nodes")).toEqual({
      kind: "nodes",
      used: 9000,
      max: null,
      reached: false,
    });
  });

  it("is unknown while the plan is not loaded", () => {
    expect(limitStatus(undefined, "nodes")).toBeUndefined();
  });
});
