import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterAll, afterEach, beforeAll, expect } from "vitest";
import * as axeMatchers from "vitest-axe/matchers";
import { resetCsrfToken, setUnauthorizedHandler } from "@/api/client";
import { server } from "./msw/server";

expect.extend(axeMatchers);

// jsdom lacks what Radix Select calls on open (pointer capture, scrolling the chosen item into view).
Element.prototype.hasPointerCapture ??= () => false;
Element.prototype.setPointerCapture ??= () => undefined;
Element.prototype.releasePointerCapture ??= () => undefined;
Element.prototype.scrollIntoView ??= () => undefined;

// Any request without a handler fails the test, so no test talks to a real API by accident.
beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  cleanup();
  server.resetHandlers();
  // The client keeps the token and the handler in module state; no test inherits another's.
  resetCsrfToken();
  setUnauthorizedHandler(undefined);
  window.history.replaceState(null, "", "/");
});
afterAll(() => server.close());
