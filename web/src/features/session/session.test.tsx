import { QueryClient } from "@tanstack/react-query";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { api, setUnauthorizedHandler, unwrap } from "@/api/client";
import { csrfTokenHandler, sessionHandler, type FakeSession } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";
import { sessionQuery, sessionQueryKey } from "./session";

const ADA = { email: "ada@example.test" };
const GRACE = { email: "grace@example.test" };
const PASSWORD = "correct horse battery staple";

/** A fake API whose session follows login/logout, recording the CSRF token of each unsafe call. */
function fakeAccountApi(initial: FakeSession) {
  const state = { session: initial, csrfTokens: [] as (string | null)[] };
  const csrf = csrfTokenHandler();
  server.use(
    csrf.handler,
    sessionHandler(() => state.session),
    http.post("/api/sessions", async ({ request }) => {
      state.csrfTokens.push(request.headers.get("X-CSRF-TOKEN"));
      const { email } = (await request.json()) as { email: string };
      state.session = { email };
      return new HttpResponse(null, { status: 204 });
    }),
    http.delete("/api/sessions/current", ({ request }) => {
      state.csrfTokens.push(request.headers.get("X-CSRF-TOKEN"));
      state.session = null;
      return new HttpResponse(null, { status: 204 });
    }),
  );
  return state;
}

async function logIn(email: string) {
  await userEvent.type(await screen.findByLabelText("E-mail"), email);
  await userEvent.type(screen.getByLabelText("Senha"), PASSWORD);
  await userEvent.click(screen.getByRole("button", { name: "Entrar" }));
}

describe("session", () => {
  it("reads a 401 as nobody signed in, without calling the unauthorized handler", async () => {
    server.use(sessionHandler(() => null));
    const onUnauthorized = vi.fn();
    setUnauthorizedHandler(onUnauthorized);

    const session = await new QueryClient().fetchQuery(sessionQuery);

    expect(session).toBeNull();
    expect(onUnauthorized).not.toHaveBeenCalled();
  });

  it("sends an anonymous visitor to login with the page to come back to", async () => {
    fakeAccountApi(null);

    const { router } = renderRoute("/?tab=recent");

    expect(await screen.findByRole("heading", { level: 1, name: "Entrar" })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/login");
    expect(router.state.location.search).toBe("?returnTo=%2F%3Ftab%3Drecent");
  });

  it("returns to the requested page after login", async () => {
    fakeAccountApi(null);
    const { router } = renderRoute("/?tab=recent");

    await logIn(ADA.email);

    expect(await screen.findByRole("heading", { level: 1, name: "Life Graph" })).toBeInTheDocument();
    expect(router.state.location.search).toBe("?tab=recent");
    expect(screen.getByText(ADA.email)).toBeInTheDocument();
  });

  it("ignores a returnTo that leaves the site", async () => {
    fakeAccountApi(null);
    const { router } = renderRoute(`/login?returnTo=${encodeURIComponent("//evil.example/")}`);

    await logIn(ADA.email);

    await waitFor(() => expect(router.state.location.pathname).toBe("/"));
  });

  it("uses a new CSRF token after login and after logout", async () => {
    const fakeApi = fakeAccountApi(null);
    renderRoute("/login");

    await logIn(ADA.email);
    await userEvent.click(await screen.findByRole("button", { name: "Sair" }));
    await logIn(GRACE.email);
    await screen.findByText(GRACE.email);

    expect(fakeApi.csrfTokens).toEqual(["csrf-1", "csrf-2", "csrf-3"]);
  });

  it("sends a signed-in user away from the login page", async () => {
    fakeAccountApi(ADA);

    const { router } = renderRoute("/login");

    expect(await screen.findByRole("heading", { level: 1, name: "Life Graph" })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/");
  });

  it("drops the cache of an expired session, so the next user sees none of it", async () => {
    const fakeApi = fakeAccountApi(ADA);
    const { router, queryClient } = renderRoute("/");
    await screen.findByText(ADA.email);
    queryClient.setQueryData(["nodes"], ["a private node of ada"]);

    // The session expires server-side; the next authenticated call answers 401.
    fakeApi.session = null;
    await expect(api.GET("/api/sessions/current").then(unwrap)).rejects.toMatchObject({ status: 401 });

    expect(await screen.findByText("Sua sessão expirou. Entre novamente.")).toBeInTheDocument();
    expect(router.state.location.search).toBe("?returnTo=%2F&notice=session_expired");
    expect(queryClient.getQueryData(["nodes"])).toBeUndefined();

    await logIn(GRACE.email);

    expect(await screen.findByText(GRACE.email)).toBeInTheDocument();
    expect(queryClient.getQueryData(["nodes"])).toBeUndefined();
    expect(queryClient.getQueryData(sessionQueryKey)).toEqual(GRACE);
  });

  it("does not bounce an anonymous page on a 401", async () => {
    fakeAccountApi(null);
    const { router } = renderRoute("/forgot-password");
    await screen.findByRole("heading", { level: 1, name: "Esqueci minha senha" });

    await expect(api.GET("/api/sessions/current").then(unwrap)).rejects.toMatchObject({ status: 401 });

    expect(router.state.location.pathname).toBe("/forgot-password");
  });

  it("logs out to the login page and forgets the session", async () => {
    fakeAccountApi(ADA);
    const { router, queryClient } = renderRoute("/");

    await userEvent.click(await screen.findByRole("button", { name: "Sair" }));

    expect(await screen.findByRole("heading", { level: 1, name: "Entrar" })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/login");
    expect(queryClient.getQueryData(sessionQueryKey)).toBeNull();
  });
});
