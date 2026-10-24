import { act, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { csrfTokenHandler, sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";
import type { SearchResponse, SearchResult } from "./searchData";

function aResult(overrides: Partial<SearchResult> = {}): SearchResult {
  return { id: "n1", title: "Filosofia estoica", typeName: "Livro", snippet: "Notas sobre Sêneca.", match: "both", ...overrides };
}

function serveSearch(response: SearchResponse, seen: string[] = []) {
  server.use(
    http.get("/api/search", ({ request }) => {
      seen.push(new URL(request.url).search);
      return HttpResponse.json(response);
    }),
  );
  return seen;
}

describe("search page", () => {
  beforeEach(() => {
    server.use(sessionHandler(() => ({ email: "ada@example.test" })), csrfTokenHandler().handler);
  });

  it("lists the Nodes found with their Type, how each was found and the snippet, linking to the Node", async () => {
    const seen = serveSearch({
      items: [aResult(), aResult({ id: "n2", title: "Marco Aurélio", typeName: null, snippet: "", match: "semantic" })],
      degraded: false,
    });
    const { container } = renderRoute("/search?q=filosofia");

    const list = await screen.findByRole("list", { name: "Resultados" });
    const rows = within(list).getAllByRole("listitem");
    expect(rows[0]).toHaveTextContent("Filosofia estoicaLivroPalavras e significadoNotas sobre Sêneca.");
    expect(within(list).getByRole("link", { name: "Filosofia estoica" })).toHaveAttribute("href", "/nodes/n1");
    expect(rows[1]).toHaveTextContent("Marco AurélioSem TypePelo significado");
    expect(screen.getByText("2 resultados")).toBeInTheDocument();
    expect(seen).toEqual(["?q=filosofia&limit=25"]);
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();
  });

  it("says the results come only from the words when the search is degraded, and still shows them", async () => {
    serveSearch({ items: [aResult({ match: "text" })], degraded: true });
    renderRoute("/search?q=filosofia");

    expect(await screen.findByText(/estes resultados vêm só das palavras/)).toHaveAttribute("role", "status");
    expect(screen.getByRole("link", { name: "Filosofia estoica" })).toBeInTheDocument();
    expect(screen.getByText("Pelas palavras")).toBeInTheDocument();
  });

  it("says nothing was found for the words", async () => {
    serveSearch({ items: [], degraded: false });
    renderRoute("/search?q=xyz");

    expect(await screen.findByText("Nenhum Node encontrado para “xyz”.")).toBeInTheDocument();
  });

  it("asks for at least 2 characters and does not search with fewer", async () => {
    const seen = serveSearch({ items: [], degraded: false });
    renderRoute("/search?q=f");

    expect(await screen.findByText("Digite pelo menos 2 caracteres e busque.")).toBeInTheDocument();
    expect(seen).toEqual([]);
  });

  it("says the search failed when the server does not answer", async () => {
    server.use(http.get("/api/search", () => new HttpResponse(null, { status: 500 })));
    renderRoute("/search?q=filosofia");

    expect(await screen.findByRole("alert")).toHaveTextContent("Não foi possível buscar agora");
  });

  it("searches from the header box on submit and never while typing", async () => {
    const seen = serveSearch({ items: [aResult()], degraded: false });
    const { router } = renderRoute("/tags");
    server.use(http.get("/api/tags", () => HttpResponse.json({ data: [], page: { nextCursor: null } })));

    const box = await screen.findByRole("searchbox", { name: "Buscar no grafo" });
    await userEvent.type(box, "  filosofia ");
    expect(seen).toEqual([]);

    await userEvent.click(screen.getByRole("button", { name: "Buscar" }));

    expect(await screen.findByRole("link", { name: "Filosofia estoica" })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/search");
    expect(router.state.location.search).toBe("?q=filosofia");
  });

  it("shows the words of the address in the header box and follows them when the address changes", async () => {
    serveSearch({ items: [aResult()], degraded: false });
    const { router } = renderRoute("/search?q=filosofia");

    expect(await screen.findByRole("searchbox", { name: "Buscar no grafo" })).toHaveValue("filosofia");

    await act(() => router.navigate("/search?q=estoicismo"));

    expect(await screen.findByDisplayValue("estoicismo")).toBe(screen.getByRole("searchbox", { name: "Buscar no grafo" }));
  });

  it("does not leave the page when the header box is empty", async () => {
    server.use(http.get("/api/tags", () => HttpResponse.json({ data: [], page: { nextCursor: null } })));
    const { router } = renderRoute("/tags");

    await userEvent.click(await screen.findByRole("button", { name: "Buscar" }));

    expect(router.state.location.pathname).toBe("/tags");
  });
});
