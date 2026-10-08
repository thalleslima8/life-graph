import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";

describe("app shell", () => {
  beforeEach(() => {
    server.use(sessionHandler(() => ({ email: "ada@example.test" })));
  });

  it("renders the home page at the root for a signed-in user", async () => {
    renderRoute("/");

    expect(await screen.findByRole("heading", { level: 1, name: "Life Graph" })).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Principal" })).toBeInTheDocument();
  });

  it("links the graph screens in the navigation", async () => {
    renderRoute("/");

    const navigation = await screen.findByRole("navigation", { name: "Principal" });
    for (const [name, href] of [
      ["Nodes", "/nodes"],
      ["Inbox", "/nodes?inbox=1"],
      ["Recent Changes", "/changes"],
      ["Types", "/types"],
      ["Agentes conectados", "/agentes"],
    ]) {
      expect(await within(navigation).findByRole("link", { name })).toHaveAttribute("href", href);
    }
  });

  it("shows the not found page for an unknown route and links back home", async () => {
    renderRoute("/does-not-exist");

    expect(await screen.findByRole("heading", { level: 1, name: "Página não encontrada" })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("link", { name: "Voltar para o início" }));

    expect(await screen.findByRole("heading", { level: 1, name: "Life Graph" })).toHaveFocus();
  });

  it.each(["/", "/does-not-exist"])("has no accessibility violations at %s", async (path) => {
    const { container } = renderRoute(path);
    await screen.findByRole("heading", { level: 1 });

    expect(await axe(container)).toHaveNoViolations();
  });
});
