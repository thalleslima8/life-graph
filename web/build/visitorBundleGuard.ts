import type { Plugin } from "vite";

/**
 * What the visitor's bundle must never carry (DA-216, DA-222): the app's authenticated client,
 * its session and account code, its router and app shell, and any trace of the CSRF exchange.
 * The lint rule stops a direct import from `src/share`; this check also catches one that
 * arrives through another module.
 */
export const FORBIDDEN_VISITOR_MODULES: readonly RegExp[] = [
  /\/src\/api\/client\.ts$/,
  /\/src\/app\//,
  /\/src\/features\/(session|account|agents|sharing)\//,
];

/** Strings only the authenticated client and its CSRF exchange contain. */
export const FORBIDDEN_VISITOR_TEXT: readonly string[] = ["X-CSRF-TOKEN", "/api/csrf-token", "csrf_token_invalid", "/api/session"];

export type BuiltChunk = { fileName: string; code: string; moduleIds: readonly string[] };

/** Each forbidden module or text found in the built chunks, as `file: what`. */
export function visitorBundleBreaches(chunks: readonly BuiltChunk[]): string[] {
  return chunks.flatMap((chunk) => [
    ...chunk.moduleIds
      .map((moduleId) => moduleId.replaceAll("\\", "/"))
      .filter((moduleId) => FORBIDDEN_VISITOR_MODULES.some((pattern) => pattern.test(moduleId)))
      .map((moduleId) => `${chunk.fileName}: module ${moduleId}`),
    ...FORBIDDEN_VISITOR_TEXT.filter((text) => chunk.code.includes(text)).map((text) => `${chunk.fileName}: text "${text}"`),
  ]);
}

/** Fails the visitor's build when its bundle carries anything of the authenticated app. */
export function visitorBundleGuard(): Plugin {
  return {
    name: "lifegraph-visitor-bundle-guard",
    apply: "build",
    generateBundle(_options, bundle) {
      const chunks = Object.values(bundle).flatMap((output) =>
        output.type === "chunk"
          ? [{ fileName: output.fileName, code: output.code, moduleIds: Object.entries(output.modules).filter(([, module]) => module.renderedLength > 0).map(([moduleId]) => moduleId) }]
          : [],
      );
      const breaches = visitorBundleBreaches(chunks);
      if (breaches.length > 0) {
        this.error(`The visitor bundle carries authenticated app code (DA-216):\n${breaches.join("\n")}`);
      }
    },
  };
}
