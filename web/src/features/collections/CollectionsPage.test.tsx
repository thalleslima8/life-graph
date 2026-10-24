import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { aCollection, aReceipt, COLLECTION_ID, TYPE_ID } from "@/test/graphFixtures";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";
import type { CollectionItem } from "./collectionsData";

function serveCollections(collections: CollectionItem[]) {
  server.use(http.get("/api/collections", () => HttpResponse.json({ data: collections, page: { nextCursor: null } })));
}

const byType = { version: 1 as const, match: "all" as const, criteria: [{ type: "type" as const, typeId: TYPE_ID }] };

describe("collections page", () => {
  beforeEach(() => {
    server.use(
      sessionHandler(() => ({ email: "ada@example.test" })),
      csrfTokenHandler().handler,
    );
  });

  it("lists each Collection with its kind and what needs review", async () => {
    serveCollections([
      aCollection(),
      aCollection({ id: "c2", name: "Livros", criteria: byType, needsReview: [{ criterionIndex: 0, reason: "type_missing" }] }),
    ]);
    renderRoute("/collections");

    const list = await screen.findByRole("list", { name: "Collections" });
    const rows = within(list)
      .getAllByRole("listitem")
      .filter((row) => row.parentElement === list);
    expect(rows[0]).toHaveTextContent("Quero ler · Manual");
    expect(rows[1]).toHaveTextContent("Livros · Dinâmica");
    expect(rows[1]).toHaveTextContent("Critério 1:");
  });

  it("pins a Collection by sending it whole with pinned set", async () => {
    let sent: unknown;
    serveCollections([aCollection()]);
    server.use(
      http.put(`/api/collections/${COLLECTION_ID}`, async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json(aReceipt());
      }),
    );
    renderRoute("/collections");

    await userEvent.click(await screen.findByRole("button", { name: "Fixar Quero ler no menu" }));

    await waitFor(() => expect(sent).toEqual({ name: "Quero ler", criteria: null, pinned: true }));
  });

  it("marks a pinned Collection past the eighth as outside the menu", async () => {
    serveCollections(Array.from({ length: 9 }, (_, index) => aCollection({ id: `c${index}`, name: `C${index}`, pinned: true })));
    renderRoute("/collections");

    const list = await screen.findByRole("list", { name: "Collections" });
    expect(within(list).getByRole("link", { name: "C8" }).closest("li")).toHaveTextContent("Fixada (fora do menu)");
    expect(within(list).getByRole("link", { name: "C7" }).closest("li")).not.toHaveTextContent("fora do menu");
  });

  it("shows a create hint when there are no Collections", async () => {
    renderRoute("/collections");

    expect(await screen.findByText("Nenhuma Collection ainda. Crie uma acima.")).toBeInTheDocument();
  });

  it("offers to try again when the list fails", async () => {
    let calls = 0;
    server.use(
      http.get("/api/collections", () =>
        ++calls === 1 ? problem(500, "internal") : HttpResponse.json({ data: [aCollection()], page: { nextCursor: null } }),
      ),
    );
    renderRoute("/collections");

    await userEvent.click(await screen.findByRole("button", { name: "Tentar de novo" }));

    expect(await screen.findByRole("list", { name: "Collections" })).toBeInTheDocument();
  });

  it("creates a manual Collection and opens it", async () => {
    let sent: unknown;
    serveCollections([]);
    server.use(
      http.post("/api/collections", async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json(
          {
            changeSetId: "cs",
            changes: [{ kind: "collection", entityId: COLLECTION_ID, version: 1, operation: "created" }],
            relationsLeftDeleted: [],
          },
          { status: 201 },
        );
      }),
      http.get(`/api/collections/${COLLECTION_ID}`, () => HttpResponse.json(aCollection())),
      http.get(`/api/collections/${COLLECTION_ID}/members`, () => HttpResponse.json({ data: [], page: { nextCursor: null } })),
      http.get("/api/changesets", () => HttpResponse.json({ data: [], latestCursor: null, page: { nextCursor: null } })),
    );
    renderRoute("/collections");

    await userEvent.type(await screen.findByRole("textbox", { name: "Nome" }), "Quero ler");
    await userEvent.click(screen.getByRole("button", { name: "Criar Collection" }));

    expect(await screen.findByRole("heading", { level: 1, name: "Quero ler" })).toBeInTheDocument();
    expect(sent).toEqual({ name: "Quero ler", criteria: null, pinned: false });
  });

  it("has no accessibility violations", async () => {
    serveCollections([aCollection({ pinned: true, criteria: byType, needsReview: [{ criterionIndex: 0, reason: "type_missing" }] })]);
    const { container } = renderRoute("/collections");
    await screen.findByRole("list", { name: "Collections" });

    expect(await axe(container)).toHaveNoViolations();
  });
});
