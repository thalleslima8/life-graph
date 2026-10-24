import { describe, expect, it } from "vitest";

// Every source file of the SPA, tests aside, as text.
const sources = import.meta.glob<string>(["/src/**/*.{ts,tsx}", "!/src/**/*.test.{ts,tsx}"], { query: "?raw", import: "default", eager: true });

const GRAPH_LIBRARY_IMPORT = /(?:\bfrom\s*|\bimport\s*\(?\s*)["'](?:sigma|@sigma\/[^"']+|graphology(?:-[^"']+)?)(?:\/[^"']*)?["']/;

describe("graph library boundary", () => {
  // DA-137: the library stays behind the GraphRenderer port, so swapping it changes one file.
  it("lets only the renderer adapter import the graph library", () => {
    const importers = Object.entries(sources)
      .filter(([, text]) => GRAPH_LIBRARY_IMPORT.test(text))
      .map(([path]) => path);

    expect(importers).toEqual(["/src/features/graph/graphRenderer.ts"]);
  });
});
