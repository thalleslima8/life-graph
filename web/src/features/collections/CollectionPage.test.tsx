import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { aCollection, aNode, aReceipt, aTag, aType, COLLECTION_ID, NODE_ID, ontologyHandlers, TAG_ID, TYPE_ID } from "@/test/graphFixtures";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";
import type { CollectionItem } from "./collectionsData";

const PATH = `/collections/${COLLECTION_ID}`;
const dynamic = {
  version: 1 as const,
  match: "any" as const,
  criteria: [
    { type: "type" as const, typeId: TYPE_ID },
    { type: "tag" as const, tagId: TAG_ID },
  ],
};

function serve(collection: CollectionItem, members = [aNode()]) {
  server.use(
    ...ontologyHandlers({ types: [aType()] }),
    http.get("/api/tags", () => HttpResponse.json({ data: [aTag()], page: { nextCursor: null } })),
    http.get("/api/changesets", () => HttpResponse.json({ data: [], latestCursor: null, page: { nextCursor: null } })),
    http.get("/api/collections", () => HttpResponse.json({ data: [collection], page: { nextCursor: null } })),
    http.get(`/api/collections/${COLLECTION_ID}`, () => HttpResponse.json(collection)),
    http.get(`/api/collections/${COLLECTION_ID}/members`, () => HttpResponse.json({ data: members, page: { nextCursor: null } })),
  );
}

describe("collection page", () => {
  beforeEach(() => {
    server.use(
      sessionHandler(() => ({ email: "ada@example.test" })),
      csrfTokenHandler().handler,
    );
  });

  it("shows a manual Collection's Nodes and removes one", async () => {
    let removed = false;
    serve(aCollection());
    server.use(
      http.delete(`/api/collections/${COLLECTION_ID}/members/${NODE_ID}`, () => {
        removed = true;
        return HttpResponse.json(aReceipt());
      }),
    );
    renderRoute(PATH);

    const members = await screen.findByRole("list", { name: "Nodes de Quero ler" });
    expect(within(members).getByRole("link", { name: "Leituras" })).toHaveAttribute("href", `/nodes/${NODE_ID}`);
    expect(screen.getByText("Collection manual")).toBeInTheDocument();

    await userEvent.click(within(members).getByRole("button", { name: "Remover Leituras da Collection" }));

    await waitFor(() => expect(removed).toBe(true));
  });

  it("says how to fill an empty manual Collection and that an empty dynamic one has no match", async () => {
    serve(aCollection(), []);
    const { unmount } = renderRoute(PATH);
    expect(await screen.findByText("Adicione Nodes pelo Inspector.")).toBeInTheDocument();
    unmount();

    serve(aCollection({ criteria: dynamic }), []);
    renderRoute(PATH);
    expect(await screen.findByText("Nenhum Node atende aos critérios.")).toBeInTheDocument();
  });

  it("describes a dynamic Collection's criteria and offers no removal", async () => {
    serve(aCollection({ criteria: dynamic }));
    renderRoute(PATH);

    expect(await screen.findByText("Um Node entra quando atende a qualquer um dos critérios:")).toBeInTheDocument();
    expect(await screen.findByText(`Type é ${aType().name}`)).toBeInTheDocument();
    expect(await screen.findByText("Tem a Tag #leitura")).toBeInTheDocument();
    const members = await screen.findByRole("list", { name: "Nodes de Quero ler" });
    expect(within(members).queryByRole("button")).not.toBeInTheDocument();
  });

  it("warns which criterion needs review", async () => {
    serve(aCollection({ criteria: dynamic, needsReview: [{ criterionIndex: 1, reason: "tag_missing" }] }));
    renderRoute(PATH);

    expect(await screen.findByRole("note")).toHaveTextContent("Critério 2:");
  });

  it("keeps the criteria on screen when the Nodes fail to load, and tries again", async () => {
    let calls = 0;
    serve(aCollection({ criteria: dynamic }));
    server.use(
      http.get(`/api/collections/${COLLECTION_ID}/members`, () =>
        ++calls <= 2 ? problem(500, "internal") : HttpResponse.json({ data: [aNode()], page: { nextCursor: null } }),
      ),
    );
    renderRoute(PATH);

    await userEvent.click(await screen.findByRole("button", { name: "Tentar de novo" }));

    expect(screen.getByText("Um Node entra quando atende a qualquer um dos critérios:")).toBeInTheDocument();
    expect(await screen.findByRole("list", { name: "Nodes de Quero ler" })).toBeInTheDocument();
  });

  it("shows not found for a Collection that does not exist", async () => {
    serve(aCollection());
    server.use(http.get(`/api/collections/${COLLECTION_ID}`, () => problem(404, "graph.collection_not_found")));
    renderRoute(PATH);

    expect(await screen.findByRole("heading", { level: 1, name: "Collection não encontrada" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Voltar para as Collections" })).toHaveAttribute("href", "/collections");
  });

  it("deletes the Collection after confirming and goes back to the list", async () => {
    let deleted = false;
    serve(aCollection());
    server.use(
      http.delete(`/api/collections/${COLLECTION_ID}`, () => {
        deleted = true;
        return HttpResponse.json(aReceipt());
      }),
    );
    renderRoute(PATH);

    await userEvent.click(await screen.findByRole("button", { name: "Apagar" }));
    await userEvent.click(await screen.findByRole("button", { name: "Apagar Collection" }));

    expect(await screen.findByRole("heading", { level: 1, name: "Collections" })).toBeInTheDocument();
    expect(deleted).toBe(true);
  });

  it("has no accessibility violations", async () => {
    serve(aCollection({ criteria: dynamic, needsReview: [{ criterionIndex: 0, reason: "type_missing" }] }));
    const { container } = renderRoute(PATH);
    await screen.findByRole("list", { name: "Nodes de Quero ler" });

    expect(await axe(container)).toHaveNoViolations();
  });
});
