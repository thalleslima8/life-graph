import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { csrfTokenHandler, problem, sessionHandler, type FakeSession } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";

const USER_ID = "01a10492-43b8-78bb-a8ee-95cc1948bd50";
const TOKEN = "Q2ZESjhB-token";
const NEW_PASSWORD = "a brand new long passphrase";

/** The two pages opened from an e-mailed link differ only in endpoint, wording and where success leads. */
const linkPages = [
  {
    path: "/confirm-email",
    endpoint: "/api/email-confirmations",
    passwordField: "password",
    title: "Definir senha",
    submit: "Definir senha",
    successNotice: "Senha definida. Entre com seu e-mail e a nova senha.",
  },
  {
    path: "/reset-password",
    endpoint: "/api/password-resets/completion",
    passwordField: "newPassword",
    title: "Redefinir senha",
    submit: "Redefinir senha",
    successNotice: "Senha redefinida. Entre com a nova senha.",
  },
] as const;

type LinkPage = (typeof linkPages)[number];

function openLink(page: LinkPage, fragment = `#userId=${USER_ID}&token=${TOKEN}`) {
  window.history.replaceState(null, "", `${page.path}${fragment}`);
  return renderRoute(page.path);
}

function apiFor(page: LinkPage, answer: () => Response | Promise<Response>, initialSession: FakeSession = null) {
  const state = { session: initialSession, bodies: [] as Record<string, unknown>[] };
  server.use(
    csrfTokenHandler().handler,
    sessionHandler(() => state.session),
    http.post(page.endpoint, async ({ request }) => {
      state.bodies.push((await request.json()) as Record<string, unknown>);
      return answer();
    }),
    http.delete("/api/sessions/current", () => {
      state.session = null;
      return new HttpResponse(null, { status: 204 });
    }),
  );
  return state;
}

async function choosePassword(page: LinkPage, password = NEW_PASSWORD, confirmation = password) {
  await userEvent.type(await screen.findByLabelText("Nova senha"), password);
  await userEvent.type(screen.getByLabelText("Repita a senha"), confirmation);
  await userEvent.click(screen.getByRole("button", { name: page.submit }));
}

describe.each(linkPages)("$path", (page) => {
  it("reads the link from the fragment and removes it from the address bar", async () => {
    apiFor(page, () => new HttpResponse(null, { status: 204 }));

    openLink(page);

    expect(await screen.findByRole("heading", { level: 1, name: page.title })).toBeInTheDocument();
    expect(window.location.hash).toBe("");
    expect(window.location.pathname).toBe(page.path);
  });

  it("sets the password with the link and sends the user to log in, without signing in", async () => {
    const api = apiFor(page, () => new HttpResponse(null, { status: 204 }));
    const { router } = openLink(page);

    await choosePassword(page);

    expect(await screen.findByRole("status")).toHaveTextContent(page.successNotice);
    expect(router.state.location.pathname).toBe("/login");
    expect(api.bodies).toEqual([{ userId: USER_ID, token: TOKEN, [page.passwordField]: NEW_PASSWORD }]);
  });

  it("checks the password rules and the repetition before sending", async () => {
    const api = apiFor(page, () => new HttpResponse(null, { status: 204 }));
    openLink(page);

    await choosePassword(page, "short", "different");

    expect(screen.getByLabelText("Nova senha")).toHaveAccessibleDescription("Use pelo menos 12 caracteres.");
    expect(screen.getByLabelText("Repita a senha")).toHaveAccessibleDescription("As senhas não conferem.");
    expect(api.bodies).toEqual([]);
  });

  it("explains a password the server rejects next to the field", async () => {
    apiFor(page, () => problem(422, "password_rejected"));
    openLink(page);

    await choosePassword(page);

    expect(await screen.findByLabelText("Nova senha")).toHaveAccessibleDescription(
      "Essa senha não atende à política. Use pelo menos 12 caracteres.",
    );
  });

  it("says the link is invalid or expired when the server refuses it", async () => {
    apiFor(page, () => problem(422, "invalid_or_expired_token"));
    openLink(page);

    await choosePassword(page);

    expect(await screen.findByRole("alert")).toHaveTextContent("Este link é inválido ou expirou.");
    expect(screen.queryByLabelText("Nova senha")).not.toBeInTheDocument();
  });

  it("says the link is invalid when the fragment is missing", async () => {
    apiFor(page, () => new HttpResponse(null, { status: 204 }));

    openLink(page, "");

    expect(await screen.findByRole("alert")).toHaveTextContent("Este link é inválido ou expirou.");
  });

  it("sends a double click once", async () => {
    let release: () => void = () => undefined;
    const api = apiFor(page, () => new Promise<Response>((resolve) => (release = () => resolve(new HttpResponse(null, { status: 204 })))));
    openLink(page);
    await userEvent.type(await screen.findByLabelText("Nova senha"), NEW_PASSWORD);
    await userEvent.type(screen.getByLabelText("Repita a senha"), NEW_PASSWORD);

    await userEvent.dblClick(screen.getByRole("button", { name: page.submit }));

    expect(await screen.findByRole("button", { name: "Salvando…" })).toBeDisabled();
    release();
    await screen.findByRole("status");
    expect(api.bodies).toHaveLength(1);
  });

  it("signed in, shows who and keeps the link until the user signs out", async () => {
    const api = apiFor(page, () => new HttpResponse(null, { status: 204 }), { email: "ada@example.test" });
    openLink(page);

    expect(await screen.findByRole("heading", { level: 1, name: "Você já está conectado" })).toBeInTheDocument();
    expect(screen.getByText("ada@example.test", { selector: "strong" })).toBeInTheDocument();
    expect(window.location.hash).toBe(`#userId=${USER_ID}&token=${TOKEN}`);

    await userEvent.click(within(screen.getByRole("main")).getByRole("button", { name: "Sair" }));
    await choosePassword(page);

    expect(await screen.findByRole("status")).toHaveTextContent(page.successNotice);
    expect(api.bodies).toEqual([{ userId: USER_ID, token: TOKEN, [page.passwordField]: NEW_PASSWORD }]);
  });

  it("keeps the page out of the Referer", async () => {
    apiFor(page, () => new HttpResponse(null, { status: 204 }));
    openLink(page);
    await screen.findByRole("heading", { level: 1, name: page.title });

    expect(document.querySelector('meta[name="referrer"]')).toHaveAttribute("content", "no-referrer");
  });

  it("has no accessibility violations, also with errors shown", async () => {
    apiFor(page, () => new HttpResponse(null, { status: 204 }));
    const { container } = openLink(page);
    await screen.findByRole("heading", { level: 1, name: page.title });
    expect(await axe(container)).toHaveNoViolations();

    await choosePassword(page, "short", "different");

    expect(await axe(container)).toHaveNoViolations();
  });
});
