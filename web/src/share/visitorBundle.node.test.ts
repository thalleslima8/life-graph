// @vitest-environment node
import { fileURLToPath, URL } from "node:url";
import { build, type Rollup } from "vite";
import { describe, expect, it } from "vitest";
import { visitorBundleBreaches } from "../../build/visitorBundleGuard";

const configFile = fileURLToPath(
  new URL("../../vite.visitor.config.ts", import.meta.url),
);

/** Builds the visitor's entry in memory, as `npm run build` does, guard included. */
async function buildVisitor(): Promise<Rollup.RollupOutput> {
  const output = await build({
    configFile,
    logLevel: "silent",
    build: { write: false },
  });
  if (!("output" in output)) {
    throw new Error("The visitor's build returns its output once.");
  }

  return output;
}

describe("visitor bundle (DA-216)", () => {
  it("carries no authenticated client, session or CSRF code, and nothing inline the strict CSP would block", async () => {
    const { output } = await buildVisitor();
    const chunks = output.flatMap((file) =>
      file.type === "chunk"
        ? [
            {
              fileName: file.fileName,
              code: file.code,
              moduleIds: file.moduleIds,
            },
          ]
        : [],
    );
    const page = output.find((file) => file.fileName === "visitor.html");
    const html = page?.type === "asset" ? String(page.source) : "";

    expect(
      chunks.some((chunk) =>
        chunk.moduleIds.some((moduleId) =>
          moduleId.endsWith("/src/share/VisitorPage.tsx"),
        ),
      ),
    ).toBe(true);
    expect(visitorBundleBreaches(chunks)).toEqual([]);
    expect(html).toContain(
      '<meta name="robots" content="noindex, nofollow" />',
    );
    expect(html).not.toMatch(/<script(?![^>]*\bsrc=)[^>]*>/);
    expect(html).not.toMatch(/\sstyle=/);
    expect(output.some((file) => file.fileName === "index.html")).toBe(false);
  }, 300_000);
});
