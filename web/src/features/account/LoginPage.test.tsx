import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";

const EMAIL = "ada@example.test";
const PASSWORD = "correct horse battery staple";

function anonymousApiAnswering(login: () => Response | Promise<Response>) {
  const calls = { login: 0 };
  server.use(
    csrfTokenHandler().handler,
    sessionHandler(() => null),
    http.post("/api/sessions", async () => {
      calls.login += 1;
      return login();
    }),
  );
  return calls;
}

async function fillAndSubmit(email = EMAIL, password = PASSWORD) {
  await userEvent.type(await screen.findByLabelText("E-mail"), email);
  await userEvent.type(screen.getByLabelText("Senha"), password);
  await userEvent.click(screen.getByRole("button", { name: "Entrar" }));
}

describe("login page", () => {
  it("explains empty fields next to them, without calling the API", async () => {
    const calls = anonymousApiAnswering(() => new HttpResponse(null, { status: 204 }));
    renderRoute("/login");

    await userEvent.click(await screen.findByRole("button", { name: "Entrar" }));

    const email = screen.getByLabelText("E-mail");
    expect(email).toHaveAttribute("aria-invalid", "true");
    expect(email).toHaveAccessibleDescription("Informe o e-mail.");
    expect(screen.getByLabelText("Senha")).toHaveAccessibleDescription("Informe a senha.");
    expect(calls.login).toBe(0);
  });

  it("says the credentials are wrong without telling which", async () => {
    anonymousApiAnswering(() => problem(400, "invalid_credentials"));
    renderRoute("/login");

    await fillAndSubmit();

    expect(await screen.findByRole("alert")).toHaveTextContent("E-mail ou senha incorretos.");
  });

  it("shows the generic message when attempts are limited", async () => {
    anonymousApiAnswering(() => problem(429, "too_many_attempts"));
    renderRoute("/login");

    await fillAndSubmit();

    expect(await screen.findByRole("alert")).toHaveTextContent("Muitas tentativas. Aguarde alguns minutos e tente novamente.");
  });

  it("maps server field errors to the fields", async () => {
    anonymousApiAnswering(() => problem(400, "validation_failed", { errors: { email: ["Must be at most 256 characters."] } }));
    renderRoute("/login");

    await fillAndSubmit();

    expect(await screen.findByText("Must be at most 256 characters.")).toBeInTheDocument();
    expect(screen.getByLabelText("E-mail")).toHaveAttribute("aria-invalid", "true");
  });

  it("sends a double click once and disables the button meanwhile", async () => {
    let release: () => void = () => undefined;
    const calls = anonymousApiAnswering(
      () => new Promise<Response>((resolve) => (release = () => resolve(problem(400, "invalid_credentials")))),
    );
    renderRoute("/login");
    await userEvent.type(await screen.findByLabelText("E-mail"), EMAIL);
    await userEvent.type(screen.getByLabelText("Senha"), PASSWORD);
    const submit = screen.getByRole("button", { name: "Entrar" });

    await userEvent.dblClick(submit);

    expect(await screen.findByRole("button", { name: "Entrando…" })).toBeDisabled();
    release();
    await screen.findByRole("alert");
    expect(calls.login).toBe(1);
  });

  it.each([
    ["password_set", "Senha definida. Entre com seu e-mail e a nova senha."],
    ["password_reset", "Senha redefinida. Entre com a nova senha."],
    ["session_expired", "Sua sessão expirou. Entre novamente."],
  ])("shows the %s notice", async (notice, text) => {
    anonymousApiAnswering(() => new HttpResponse(null, { status: 204 }));
    renderRoute(`/login?notice=${notice}`);

    expect(await screen.findByRole("status")).toHaveTextContent(text);
  });

  it("moves focus to the page title", async () => {
    anonymousApiAnswering(() => new HttpResponse(null, { status: 204 }));
    renderRoute("/login");

    expect(await screen.findByRole("heading", { level: 1, name: "Entrar" })).toHaveFocus();
  });

  it("has no accessibility violations, also with errors shown", async () => {
    anonymousApiAnswering(() => problem(400, "invalid_credentials"));
    const { container } = renderRoute("/login?notice=password_set");
    await screen.findByRole("heading", { level: 1, name: "Entrar" });
    expect(await axe(container)).toHaveNoViolations();

    await userEvent.click(screen.getByRole("button", { name: "Entrar" }));

    expect(await axe(container)).toHaveNoViolations();
  });
});
