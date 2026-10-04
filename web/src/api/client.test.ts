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
    expect(isApiError(failure, "invalid_credentials")).toBe(false);
  });

  it("reports a 401 to the unauthorized handler, and nothing else", async () => {
    const onUnauthorized = vi.fn();
    setUnauthorizedHandler(onUnauthorized);
    server.use(
      http.get("/api/sessions/current", () => new HttpResponse(null, { status: 401 })),
      csrfTokenHandler().handler,
      http.post("/api/sessions", () => problem(400, "invalid_credentials")),
    );

    await expect(api.GET("/api/sessions/current").then(unwrap)).rejects.toMatchObject({ status: 401 });
    await expect(api.POST("/api/sessions", { body: credentials }).then(unwrap)).rejects.toMatchObject({ status: 400 });

    expect(onUnauthorized).toHaveBeenCalledTimes(1);
  });
});
