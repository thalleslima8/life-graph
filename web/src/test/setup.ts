import "@testing-library/jest-dom/vitest";
import { cleanup, configure } from "@testing-library/react";
import { afterAll, afterEach, beforeAll, expect } from "vitest";
import * as axeMatchers from "vitest-axe/matchers";
import { server } from "./msw/server";

// A `*.node.test.ts` file runs without a DOM (the visitor's build and lint tests); the app's
// client reads the page's origin when it loads, so it is only loaded where there is a page.
const hasDom = typeof window !== "undefined";
const client = hasDom ? await import("@/api/client") : undefined;

// The first render of a test file also loads its lazy route's modules, which takes over a
// second on a busy runner; the 1 s default made those first queries flaky (GEN-072).
const ASYNC_QUERY_TIMEOUT_MS = 3000;
configure({ asyncUtilTimeout: ASYNC_QUERY_TIMEOUT_MS });

expect.extend(axeMatchers);

// jsdom lacks what Radix Select calls on open (pointer capture, scrolling the chosen item into view).
if (hasDom) {
  Element.prototype.hasPointerCapture ??= () => false;
  Element.prototype.setPointerCapture ??= () => undefined;
  Element.prototype.releasePointerCapture ??= () => undefined;
  Element.prototype.scrollIntoView ??= () => undefined;
}

// Any request without a handler fails the test, so no test talks to a real API by accident.
beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  cleanup();
  server.resetHandlers();
  // The client keeps the token and the handler in module state; no test inherits another's.
  client?.resetCsrfToken();
  client?.setUnauthorizedHandler(undefined);
  if (hasDom) {
    window.history.replaceState(null, "", "/");
  }
});
afterAll(() => server.close());
