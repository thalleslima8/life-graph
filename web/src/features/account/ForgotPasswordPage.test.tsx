import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";

function apiAnswering(request: () => Response) {
  const emails: string[] = [];
  server.use(
    csrfTokenHandler().handler,
    sessionHandler(() => null),
    http.post("/api/password-resets", async ({ request: sent }) => {
      emails.push(((await sent.json()) as { email: string }).email);
      return request();
    }),
  );
  return emails;
}

async function requestLink(email: string) {
  await userEvent.type(await screen.findByLabelText("E-mail"), email);
  await userEvent.click(screen.getByRole("button", { name: "Enviar link" }));
}

describe("forgot password page", () => {
  it("answers with the same neutral message for any e-mail", async () => {
    const emails = apiAnswering(() => new HttpResponse(null, { status: 202 }));
    renderRoute("/forgot-password");

    await requestLink("ada@example.test");

    expect(await screen.findByRole("status")).toHaveTextContent(
      "Se houver uma conta ativa com esse e-mail, enviamos um link para redefinir a senha.",
    );
    expect(screen.queryByLabelText("E-mail")).not.toBeInTheDocument();
    expect(emails).toEqual(["ada@example.test"]);
  });

  it("shows the generic message when requests are limited", async () => {
    apiAnswering(() => problem(429, "accounts.too_many_attempts"));
    renderRoute("/forgot-password");

    await requestLink("ada@example.test");

    expect(await screen.findByRole("alert")).toHaveTextContent("Muitas tentativas. Aguarde alguns minutos e tente novamente.");
  });

  it("checks the e-mail format before sending", async () => {
    const emails = apiAnswering(() => new HttpResponse(null, { status: 202 }));
    renderRoute("/forgot-password");

    await requestLink("not-an-email");

    expect(screen.getByLabelText("E-mail")).toHaveAccessibleDescription("Informe um e-mail válido.");
    expect(emails).toEqual([]);
  });

  it("has no accessibility violations", async () => {
    apiAnswering(() => new HttpResponse(null, { status: 202 }));
    const { container } = renderRoute("/forgot-password");
    await screen.findByRole("heading", { level: 1, name: "Esqueci minha senha" });

    expect(await axe(container)).toHaveNoViolations();
  });
});
