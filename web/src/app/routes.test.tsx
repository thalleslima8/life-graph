import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { renderRoute } from "@/test/renderRoute";

describe("app shell", () => {
  it("renders the home page at the root", () => {
    renderRoute("/");

    expect(screen.getByRole("heading", { level: 1, name: "Life Graph" })).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Principal" })).toBeInTheDocument();
  });

  it("shows the not found page for an unknown route and links back home", async () => {
    renderRoute("/does-not-exist");

    expect(screen.getByRole("heading", { level: 1, name: "Página não encontrada" })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("link", { name: "Voltar para o início" }));

    expect(screen.getByRole("heading", { level: 1, name: "Life Graph" })).toBeInTheDocument();
  });

  it.each(["/", "/does-not-exist"])("has no accessibility violations at %s", async (path) => {
    const { container } = renderRoute(path);

    expect(await axe(container)).toHaveNoViolations();
  });
});
