import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { aCollection } from "@/test/graphFixtures";
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
      ["Tags", "/tags"],
      ["Agentes conectados", "/agentes"],
    ]) {
      expect(await within(navigation).findByRole("link", { name })).toHaveAttribute("href", href);
    }
  });

  it("lists the first eight pinned Collections in the menu, then all of them", async () => {
    server.use(
      http.get("/api/collections", () =>
        HttpResponse.json({
          data: [
            aCollection({ id: "unpinned", name: "Solta" }),
            ...Array.from({ length: 9 }, (_, index) =>
              aCollection({ id: `c${index}`, name: `Fixada ${index}`, pinned: true, needsReview: index === 0 ? [{ criterionIndex: 0, reason: "tag_missing" }] : null }),
            ),
          ],
          page: { nextCursor: null },
        }),
      ),
    );
    renderRoute("/");

    const navigation = await screen.findByRole("navigation", { name: "Principal" });
    expect(await within(navigation).findByRole("link", { name: "Fixada 0 (Precisa de revisão)" })).toHaveAttribute("href", "/collections/c0");
    expect(within(navigation).getByRole("link", { name: "Fixada 7" })).toBeInTheDocument();
    expect(within(navigation).queryByRole("link", { name: "Fixada 8" })).not.toBeInTheDocument();
    expect(within(navigation).queryByRole("link", { name: "Solta" })).not.toBeInTheDocument();
    expect(within(navigation).getByRole("link", { name: "Todas as Collections" })).toHaveAttribute("href", "/collections");
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
