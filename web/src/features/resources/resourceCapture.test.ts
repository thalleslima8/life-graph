import { describe, expect, it } from "vitest";
import { captureUrlSchema, outcomeOf, RESOURCE_URL_MAX_LENGTH, type CaptureReceipt } from "./resourceCapture";

function aCaptureReceipt(flags: Pick<CaptureReceipt, "created" | "alreadyExists" | "alreadyInInbox">): CaptureReceipt {
  return { nodeId: "01920000-0000-7000-8000-000000000001", changeSetId: null, ...flags };
}

describe("outcomeOf", () => {
  it.each([
    [{ created: true, alreadyExists: false, alreadyInInbox: false }, "created"],
    [{ created: false, alreadyExists: true, alreadyInInbox: true }, "already-in-inbox"],
    [{ created: false, alreadyExists: true, alreadyInInbox: false }, "reentered"],
    // Combinations the server never sends: the same order, never a throw.
    [{ created: false, alreadyExists: false, alreadyInInbox: false }, "created"],
    [{ created: true, alreadyExists: true, alreadyInInbox: false }, "reentered"],
    [{ created: true, alreadyExists: false, alreadyInInbox: true }, "already-in-inbox"],
    [{ created: false, alreadyExists: false, alreadyInInbox: true }, "already-in-inbox"],
    [{ created: true, alreadyExists: true, alreadyInInbox: true }, "already-in-inbox"],
  ] as const)("reads %o as %s", (flags, outcome) => {
    expect(outcomeOf(aCaptureReceipt(flags))).toBe(outcome);
  });
});

describe("captureUrlSchema", () => {
  it("asks for something to send and leaves the link's validity to the server", () => {
    expect(captureUrlSchema.safeParse("   ").success).toBe(false);
    expect(captureUrlSchema.safeParse("x".repeat(RESOURCE_URL_MAX_LENGTH + 1)).success).toBe(false);
    expect(captureUrlSchema.parse("  ftp://example.test  ")).toBe("ftp://example.test");
  });
});
