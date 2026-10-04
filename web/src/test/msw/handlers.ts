import { http, HttpResponse } from "msw";

/** A signed-in user for the session endpoint, or null for nobody. */
export type FakeSession = { email: string } | null;

export const ACCOUNT_ID = "01a10492-43b8-7176-9da5-49f6f624aa1e";

export function problem(status: number, code: string, extra: Record<string, unknown> = {}) {
  return HttpResponse.json({ status, title: code, code, ...extra }, { status, headers: { "Content-Type": "application/problem+json" } });
}

export function sessionHandler(getSession: () => FakeSession) {
  return http.get("/api/sessions/current", () => {
    const session = getSession();
    return session ? HttpResponse.json({ accountId: ACCOUNT_ID, email: session.email }) : new HttpResponse(null, { status: 401 });
  });
}

/** Hands out csrf-1, csrf-2, … and counts the reads. */
export function csrfTokenHandler() {
  const state = { reads: 0 };
  const handler = http.get("/api/csrf-token", () => {
    state.reads += 1;
    return HttpResponse.json({ token: `csrf-${state.reads}` });
  });
  return { handler, state };
}
