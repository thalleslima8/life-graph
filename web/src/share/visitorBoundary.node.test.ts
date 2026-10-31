// @vitest-environment node
import { fileURLToPath, URL } from "node:url";
import { ESLint } from "eslint";
import { describe, expect, it } from "vitest";
import { visitorBundleBreaches } from "../../build/visitorBundleGuard";

const webRoot = fileURLToPath(new URL("../..", import.meta.url));

// The first lint loads the type-aware configuration, which takes seconds; one linter serves all.
const LINT_TIMEOUT_MS = 60_000;
const eslint = new ESLint({ cwd: webRoot });

async function lintVisitorImport(specifier: string): Promise<string[]> {
  const [result] = await eslint.lintText(`import "${specifier}";\n`, {
    filePath: `${webRoot}/src/share/probe.ts`,
  });
  return (result?.messages ?? [])
    .filter((message) => message.ruleId === "no-restricted-imports")
    .map((message) => message.message);
}

describe("visitor boundary (DA-216)", { timeout: LINT_TIMEOUT_MS }, () => {
  it.each([
    "@/api/client",
    "../api/client",
    "@/app/routes",
    "@/features/session/sessionData",
    "@/features/graph/localGraph",
    "@/features/graph/graphMessages",
    "./csrfToken",
  ])("refuses importing %s into the visitor's page", async (specifier) => {
    expect(await lintVisitorImport(specifier)).toHaveLength(1);
  });

  it.each([
    "@/features/graph/GraphCanvas",
    "@/features/graph/graphView",
    "@/features/graph/graphViewMessages",
    "@/api/schema.gen",
    "@/components/ui/button",
  ])("lets the visitor's page import %s", async (specifier) => {
    expect(await lintVisitorImport(specifier)).toEqual([]);
  });

  it("flags a built chunk that carries the authenticated client or the CSRF exchange, and passes a clean one", () => {
    const clean = {
      fileName: "assets/visitor.js",
      code: "fetch('/api/shared')",
      moduleIds: [
        "/w/src/share/VisitorPage.tsx",
        "/w/src/features/graph/GraphCanvas.tsx",
      ],
    };
    const withClient = {
      fileName: "assets/visitor.js",
      code: "",
      moduleIds: ["/w/src/api/client.ts"],
    };
    const withCsrf = {
      fileName: "assets/other.js",
      code: 'headers.set("X-CSRF-TOKEN", token)',
      moduleIds: [],
    };

    expect(visitorBundleBreaches([clean])).toEqual([]);
    expect(visitorBundleBreaches([withClient, withCsrf])).toEqual([
      "assets/visitor.js: module /w/src/api/client.ts",
      'assets/other.js: text "X-CSRF-TOKEN"',
    ]);
  });
});
