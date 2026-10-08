import { describe, expect, it } from "vitest";
import { safeReturnTo } from "./safeReturnTo";

describe("safeReturnTo", () => {
  it.each(["/", "/nodes/1?tab=relations#top"])("keeps the relative path %s", (path) => {
    expect(safeReturnTo(path)).toBe(path);
  });

  it.each([null, undefined, "", "//evil.example", "/\\evil.example", "https://evil.example/", "javascript:alert(1)", "nodes"])(
    "falls back to the root for %s",
    (raw) => {
      expect(safeReturnTo(raw)).toBe("/");
    },
  );
});
