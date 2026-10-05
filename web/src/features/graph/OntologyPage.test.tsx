import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { aDefinition, aReceipt, aType, DEFINITION_ID, ontologyHandlers, TYPE_ID } from "@/test/graphFixtures";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";

const OPTION = { id: "01920000-0000-7000-8000-0000000000e1", label: "Lendo" };

function ontologyApi(types = [aType()]) {
  const sent: { method: string; path: string; body: unknown }[] = [];
  const record = async ({ request }: { request: Request }) => {
    const text = await request.text();
    sent.push({ method: request.method, path: new URL(request.url).pathname, body: text ? JSON.parse(text) : null });
    return HttpResponse.json(aReceipt());
  };
  server.use(
    sessionHandler(() => ({ email: "ada@example.test" })),
    csrfTokenHandler().handler,
    ...ontologyHandlers({ types, definitions: [aDefinition({ valueKind: "select", options: [OPTION] })] }),
    http.post("/api/property-definitions", record),
    http.patch("/api/types/:typeId", record),
    http.delete("/api/types/:typeId/properties/:definitionId", record),
    http.patch("/api/property-definitions/:id", record),
    http.put("/api/types/:typeId/properties/:definitionId", record),
  );
  return sent;
}

describe("types and properties", () => {
  it("explains that a Type used only by deleted Nodes cannot go yet, and when it can", async () => {
    ontologyApi();
    server.use(
      http.delete("/api/types/:typeId", () =>
        problem(422, "graph.type_in_use_by_deleted_nodes", { errors: { deletedNodeCount: ["2"], lastPurgeAt: ["2026-11-04T12:00:00Z"] } }),
      ),
    );
    renderRoute("/types");

    await userEvent.click(await screen.findByRole("button", { name: "Apagar Type Livro" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("2 Nodes apagados ainda usam");
    expect(alert).toHaveTextContent("4 de novembro de 2026");
  });

  it("explains a refused value-kind change of a property that has values", async () => {
    ontologyApi();
    server.use(http.patch("/api/property-definitions/:id", () => problem(422, "graph.property_has_values")));
    renderRoute("/types");

    await userEvent.click(await screen.findByRole("button", { name: "Editar Status" }));
    await userEvent.click(screen.getAllByRole("combobox", { name: "Tipo de valor" })[1] as HTMLElement);
    await userEvent.click(await screen.findByRole("option", { name: "Texto" }));
    await userEvent.click(screen.getByRole("button", { name: "Salvar" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("o tipo de valor não muda");
  });

  it("creates a Select property with one option per line", async () => {
    const sent = ontologyApi();
    renderRoute("/types");

    await userEvent.type(await screen.findByLabelText("Nova propriedade"), "Prioridade");
    await userEvent.click(screen.getAllByRole("combobox", { name: "Tipo de valor" })[0] as HTMLElement);
    await userEvent.click(await screen.findByRole("option", { name: "Seleção" }));
    await userEvent.type(screen.getByLabelText("Opções (uma por linha)"), "Alta{enter}Baixa");
    await userEvent.click(screen.getByRole("button", { name: "Criar propriedade" }));

    await screen.findByLabelText("Nova propriedade");
    expect(sent).toContainEqual({
      method: "POST",
      path: "/api/property-definitions",
      body: { name: "Prioridade", valueKind: "select", options: [{ label: "Alta" }, { label: "Baixa" }] },
    });
  });

  it("keeps the id of an option it keeps when editing the options", async () => {
    const sent = ontologyApi();
    renderRoute("/types");

    await userEvent.click(await screen.findByRole("button", { name: "Editar Status" }));
    await userEvent.type(screen.getByLabelText("Opções (uma por linha)"), "{enter}Lido");
    await userEvent.click(screen.getByRole("button", { name: "Salvar" }));

    await screen.findByRole("button", { name: "Editar Status" });
    expect(sent).toContainEqual({
      method: "PATCH",
      path: `/api/property-definitions/${DEFINITION_ID}`,
      body: { name: "Status", valueKind: "select", options: [OPTION, { label: "Lido" }] },
    });
  });

  it("attaches a property to a Type", async () => {
    const sent = ontologyApi();
    renderRoute("/types");

    await userEvent.click(await screen.findByRole("combobox", { name: "Anexar propriedade" }));
    await userEvent.click(await screen.findByRole("option", { name: "Status" }));
    await userEvent.click(screen.getByRole("button", { name: "Anexar" }));

    await screen.findByRole("button", { name: "Anexar" });
    expect(sent).toContainEqual({ method: "PUT", path: `/api/types/${TYPE_ID}/properties/${DEFINITION_ID}`, body: null });
  });

  it("renames a Type", async () => {
    const sent = ontologyApi();
    renderRoute("/types");

    const name = await screen.findByRole("textbox", { name: "Renomear Livro" });
    await userEvent.clear(name);
    await userEvent.type(name, "Livros");
    await userEvent.click(screen.getByRole("button", { name: "Renomear" }));

    await waitFor(() => expect(sent).toContainEqual({ method: "PATCH", path: `/api/types/${TYPE_ID}`, body: { name: "Livros" } }));
  });

  it("detaches a property from a Type", async () => {
    const sent = ontologyApi([aType({ properties: [{ propertyDefinitionId: DEFINITION_ID, name: "Status", valueKind: "select" }] })]);
    renderRoute("/types");

    await userEvent.click(await screen.findByRole("button", { name: "Desanexar Status de Livro" }));

    await waitFor(() =>
      expect(sent).toContainEqual({ method: "DELETE", path: `/api/types/${TYPE_ID}/properties/${DEFINITION_ID}`, body: null }),
    );
  });

  it("has no accessibility violations", async () => {
    ontologyApi();
    const { container } = renderRoute("/types");
    await screen.findByRole("list", { name: "Types" });

    expect(await axe(container)).toHaveNoViolations();
  });
});
