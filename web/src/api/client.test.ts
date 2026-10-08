import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { csrfTokenHandler, problem } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { api, ApiError, isApiError, resetCsrfToken, setUnauthorizedHandler, unwrap } from "./client";

const credentials = { email: "ada@example.test", password: "correct horse battery staple" };

describe("api client", () => {
  it("sends the CSRF token on unsafe methods only", async () => {
    const csrf = csrfTokenHandler();
    const seen: Record<string, string | null> = {};
    server.use(
      csrf.handler,
      http.post("/api/sessions", ({ request }) => {
        seen.post = request.headers.get("X-CSRF-TOKEN");
        return new HttpResponse(null, { status: 204 });
      }),
      http.get("/api/sessions/current", ({ request }) => {
        seen.get = request.headers.get("X-CSRF-TOKEN");
        return HttpResponse.json({ accountId: "a", email: credentials.email });
      }),
    );

    await api.GET("/api/sessions/current");
    await api.POST("/api/sessions", { body: credentials });

    expect(seen).toEqual({ get: null, post: "csrf-1" });
  });

  it("reads one token for parallel unsafe requests", async () => {
    const csrf = csrfTokenHandler();
    server.use(
      csrf.handler,
      http.post("/api/password-resets", () => new HttpResponse(null, { status: 202 })),
    );

    await Promise.all([1, 2, 3].map(() => api.POST("/api/password-resets", { body: { email: credentials.email } })));

    expect(csrf.state.reads).toBe(1);
  });

  it("reads a new token after a reset", async () => {
    const csrf = csrfTokenHandler();
    const tokens: (string | null)[] = [];
    server.use(
      csrf.handler,
      http.delete("/api/sessions/current", ({ request }) => {
        tokens.push(request.headers.get("X-CSRF-TOKEN"));
        return new HttpResponse(null, { status: 204 });
      }),
    );

    await api.DELETE("/api/sessions/current");
    resetCsrfToken();
    await api.DELETE("/api/sessions/current");

    expect(tokens).toEqual(["csrf-1", "csrf-2"]);
  });

  it("replays a request once with a new token when the token is rejected", async () => {
    const csrf = csrfTokenHandler();
    const attempts: { token: string | null; body: unknown }[] = [];
    server.use(
      csrf.handler,
      http.post("/api/sessions", async ({ request }) => {
        const token = request.headers.get("X-CSRF-TOKEN");
        attempts.push({ token, body: await request.json() });
        return token === "csrf-1" ? problem(400, "csrf_token_invalid") : new HttpResponse(null, { status: 204 });
      }),
    );

    const result = await api.POST("/api/sessions", { body: credentials });

    expect(result.response.status).toBe(204);
    expect(attempts).toEqual([
      { token: "csrf-1", body: credentials },
      { token: "csrf-2", body: credentials },
    ]);
  });

  it("gives up after one replay", async () => {
    const csrf = csrfTokenHandler();
    let attempts = 0;
    server.use(
      csrf.handler,
      http.post("/api/sessions", () => {
        attempts += 1;
        return problem(403, "csrf_token_invalid");
      }),
    );

    const failure = await api.POST("/api/sessions", { body: credentials }).then(unwrap).catch((error: unknown) => error);

    expect(attempts).toBe(2);
    expect(isApiError(failure, "csrf_token_invalid")).toBe(true);
  });

  it("turns Problem Details into an ApiError with code and field errors", async () => {
    server.use(
      csrfTokenHandler().handler,
      http.post("/api/sessions", () =>
        problem(400, "validation_failed", { title: "Validation failed", errors: { email: ["Required."] } }),
      ),
    );

    const failure = await api.POST("/api/sessions", { body: credentials }).then(unwrap).catch((error: unknown) => error);

    expect(failure).toBeInstanceOf(ApiError);
    expect(failure).toMatchObject({ status: 400, code: "validation_failed", fieldErrors: { email: ["Required."] } });
    expect(isApiError(failure, "validation_failed")).toBe(true);
    expect(isApiError(failure, "accounts.invalid_credentials")).toBe(false);
  });

  it("reads the details of a refusal from details, or from errors when there are none", async () => {
    server.use(
      csrfTokenHandler().handler,
      http.patch("/api/nodes/:nodeId", () => problem(409, "graph.node_version_conflict", { details: { version: ["2"] } })),
      http.delete("/api/types/:typeId", () =>
        problem(422, "graph.type_in_use_by_deleted_nodes", { errors: { deletedNodeCount: ["1"], lastPurgeAt: ["2026-11-04T00:00:00Z"] } }),
      ),
    );
    const id = "01a10492-43b8-7176-9da5-49f6f624aa1e";

    const conflict = await api.PATCH("/api/nodes/{nodeId}", { params: { path: { nodeId: id } }, body: { version: 1 } }).then(unwrap).catch((error: unknown) => error);
    const inUse = await api.DELETE("/api/types/{typeId}", { params: { path: { typeId: id } } }).then(unwrap).catch((error: unknown) => error);

    expect(conflict).toMatchObject({ status: 409, details: { version: ["2"] }, fieldErrors: {} });
    expect(inUse).toMatchObject({ status: 422, details: { deletedNodeCount: ["1"], lastPurgeAt: ["2026-11-04T00:00:00Z"] } });
  });

  it("reads Retry-After in seconds", async () => {
    server.use(http.get("/api/changesets", () => problem(429, "too_many_requests", {}, { "Retry-After": "12" })));

    const refusal = await api.GET("/api/changesets").then(unwrap).catch((error: unknown) => error);

    expect(refusal).toMatchObject({ status: 429, code: "too_many_requests", retryAfterSeconds: 12 });
  });

  it("reports a 401 to the unauthorized handler, and nothing else", async () => {
    const onUnauthorized = vi.fn();
    setUnauthorizedHandler(onUnauthorized);
    server.use(
      http.get("/api/sessions/current", () => new HttpResponse(null, { status: 401 })),
      csrfTokenHandler().handler,
      http.post("/api/sessions", () => problem(400, "accounts.invalid_credentials")),
    );

    await expect(api.GET("/api/sessions/current").then(unwrap)).rejects.toMatchObject({ status: 401 });
    await expect(api.POST("/api/sessions", { body: credentials }).then(unwrap)).rejects.toMatchObject({ status: 400 });

    expect(onUnauthorized).toHaveBeenCalledTimes(1);
  });
});
