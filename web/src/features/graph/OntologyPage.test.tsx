import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import type { PropertyDefinitionItem } from "@/features/graph/graphData";
import { aDefinition, aReceipt, aType, DEFINITION_ID, ontologyHandlers, TYPE_ID } from "@/test/graphFixtures";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";

const OPTION = { id: "01920000-0000-7000-8000-0000000000e1", label: "Lendo", archived: false };
const SECOND_OPTION = { id: "01920000-0000-7000-8000-0000000000e2", label: "Lido", archived: false };

type Usage = { nodeCount: number; typeCount: number; tombstoneCount: number; lastPurgeAt: string | null; optionCounts: Record<string, number> };

function aUsage(overrides: Partial<Usage> = {}): Usage {
  return { nodeCount: 0, typeCount: 0, tombstoneCount: 0, lastPurgeAt: null, optionCounts: {}, ...overrides };
}

function usageApi(...usages: Usage[]) {
  let reads = 0;
  server.use(
    http.get("/api/property-definitions/:id/usage", () => {
      const usage = usages[Math.min(reads, usages.length - 1)];
      reads += 1;
      return HttpResponse.json(usage);
    }),
  );
}

function definitionsApi(definitions: PropertyDefinitionItem[]) {
  server.use(...ontologyHandlers({ types: [aType()], definitions }));
}

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
    http.delete("/api/property-definitions/:id", async ({ request }) => {
      const url = new URL(request.url);
      sent.push({ method: "DELETE", path: `${url.pathname}${url.search}`, body: null });
      return HttpResponse.json(aReceipt());
    }),
  );
  usageApi(aUsage());
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

  it("offers only the safe value kinds while Nodes hold values, and says so", async () => {
    ontologyApi();
    definitionsApi([aDefinition({ name: "Páginas", valueKind: "number" })]);
    usageApi(aUsage({ nodeCount: 3 }));
    renderRoute("/types");

    await userEvent.click(await screen.findByRole("button", { name: "Editar Páginas" }));
    const form = within(screen.getByRole("form", { name: "Propriedade Páginas" }));
    await waitFor(() => expect(form.getByText("Há Nodes com valores: só as mudanças que convertem sem perda.")).toBeInTheDocument());
    await userEvent.click(form.getByRole("combobox", { name: "Tipo de valor" }));

    expect(await screen.findAllByRole("option")).toHaveLength(2);
    expect(screen.getByRole("option", { name: "Número" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Texto" })).toBeInTheDocument();
  });

  it("offers every value kind when no Node holds a value", async () => {
    ontologyApi();
    definitionsApi([aDefinition({ name: "Páginas", valueKind: "number" })]);
    renderRoute("/types");

    await userEvent.click(await screen.findByRole("button", { name: "Editar Páginas" }));
    const form = within(screen.getByRole("form", { name: "Propriedade Páginas" }));
    await waitFor(() => expect(form.queryByText(/só as mudanças que convertem sem perda/)).not.toBeInTheDocument());
    await userEvent.click(form.getByRole("combobox", { name: "Tipo de valor" }));

    expect(await screen.findAllByRole("option")).toHaveLength(8);
  });

  it("shows a value-kind change the server blocks on the kind field", async () => {
    ontologyApi();
    definitionsApi([aDefinition({ name: "Páginas", valueKind: "number" })]);
    server.use(http.patch("/api/property-definitions/:id", () => problem(422, "graph.property_kind_change_blocked")));
    renderRoute("/types");

    await userEvent.click(await screen.findByRole("button", { name: "Editar Páginas" }));
    const form = within(screen.getByRole("form", { name: "Propriedade Páginas" }));
    await userEvent.click(form.getByRole("combobox", { name: "Tipo de valor" }));
    await userEvent.click(await screen.findByRole("option", { name: "Sim/Não" }));
    await userEvent.click(form.getByRole("button", { name: "Salvar" }));

    const alert = await form.findByRole("alert");
    expect(alert).toHaveTextContent("Crie uma nova propriedade");
    expect(form.getByRole("combobox", { name: "Tipo de valor" })).toHaveAttribute("aria-invalid", "true");
    expect(form.getByRole("combobox", { name: "Tipo de valor" })).toHaveAccessibleDescription(/Crie uma nova propriedade/);
  });

  it("asks before turning dates into midnight UTC, with how many Nodes hold one", async () => {
    const sent = ontologyApi();
    definitionsApi([aDefinition({ name: "Lançamento", valueKind: "date" })]);
    usageApi(aUsage({ nodeCount: 2, tombstoneCount: 1 }));
    renderRoute("/types");

    await userEvent.click(await screen.findByRole("button", { name: "Editar Lançamento" }));
    const form = within(screen.getByRole("form", { name: "Propriedade Lançamento" }));
    await waitFor(() => expect(form.getByText(/só as mudanças que convertem sem perda/)).toBeInTheDocument());
    await userEvent.click(form.getByRole("combobox", { name: "Tipo de valor" }));
    await userEvent.click(await screen.findByRole("option", { name: "Data e hora" }));
    await userEvent.click(form.getByRole("button", { name: "Salvar" }));

    const dialog = await screen.findByRole("dialog", { name: "Converter Data em Data e hora?" });
    expect(dialog).toHaveTextContent("As datas de 3 Nodes, apagados incluídos, passam a ser essa data à meia-noite UTC.");
    expect(sent.some((request) => request.method === "PATCH")).toBe(false);
    await userEvent.click(within(dialog).getByRole("button", { name: "Converter" }));

    await waitFor(() =>
      expect(sent).toContainEqual({
        method: "PATCH",
        path: `/api/property-definitions/${DEFINITION_ID}`,
        body: { name: "Lançamento", valueKind: "date_time", options: [] },
      }),
    );
  });

  it("hides a Type's Nodes from agents", async () => {
    const sent = ontologyApi();
    renderRoute("/types");

    await userEvent.click(await screen.findByRole("checkbox", { name: "Oculto para agentes" }));

    await waitFor(() => expect(sent).toContainEqual({ method: "PATCH", path: `/api/types/${TYPE_ID}`, body: { hiddenFromAgents: true } }));
  });

  it("creates a Select property with the options added one by one", async () => {
    const sent = ontologyApi();
    renderRoute("/types");

    await userEvent.type(await screen.findByLabelText("Nova propriedade"), "Prioridade");
    await userEvent.click(screen.getAllByRole("combobox", { name: "Tipo de valor" })[0] as HTMLElement);
    await userEvent.click(await screen.findByRole("option", { name: "Seleção" }));
    await userEvent.click(screen.getByRole("button", { name: "Adicionar opção" }));
    await userEvent.type(screen.getByLabelText("Opção 1"), "Alta");
    await userEvent.click(screen.getByRole("button", { name: "Adicionar opção" }));
    await userEvent.type(screen.getByLabelText("Opção 2"), "Baixa");
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
    const form = within(screen.getByRole("form", { name: "Propriedade Status" }));
    await userEvent.click(form.getByRole("button", { name: "Adicionar opção" }));
    await userEvent.type(form.getByLabelText("Opção 2"), "Lido");
    await userEvent.click(form.getByRole("button", { name: "Salvar" }));

    await screen.findByRole("button", { name: "Editar Status" });
    expect(sent).toContainEqual({
      method: "PATCH",
      path: `/api/property-definitions/${DEFINITION_ID}`,
      body: { name: "Status", valueKind: "select", options: [{ id: OPTION.id, label: "Lendo" }, { label: "Lido" }] },
    });
  });

  it("keeps an option Nodes chose from being removed, with the count, and archives it instead", async () => {
    const sent = ontologyApi();
    definitionsApi([aDefinition({ valueKind: "select", options: [OPTION, SECOND_OPTION] })]);
    usageApi(aUsage({ nodeCount: 4, optionCounts: { [OPTION.id]: 4 } }));
    renderRoute("/types");

    await userEvent.click(await screen.findByRole("button", { name: "Editar Status" }));
    const form = within(screen.getByRole("form", { name: "Propriedade Status" }));
    const removeChosen = form.getByRole("button", { name: "Remover opção Lendo" });
    await waitFor(() => expect(removeChosen).toBeDisabled());
    expect(removeChosen).toHaveAccessibleDescription("Escolhida por 4 Nodes, apagados incluídos: arquive em vez de remover.");
    expect(form.getByRole("button", { name: "Remover opção Lido" })).toBeEnabled();

    await userEvent.click(form.getByRole("button", { name: "Arquivar opção Lendo" }));
    expect(form.getByRole("button", { name: "Restaurar opção Lendo" })).toBeInTheDocument();
    await userEvent.click(form.getByRole("button", { name: "Remover opção Lido" }));
    await userEvent.click(form.getByRole("button", { name: "Salvar" }));

    await waitFor(() =>
      expect(sent).toContainEqual({
        method: "PATCH",
        path: `/api/property-definitions/${DEFINITION_ID}`,
        body: { name: "Status", valueKind: "select", options: [{ id: OPTION.id, label: "Lendo", archived: true }] },
      }),
    );
  });

  it("restores an archived option", async () => {
    const sent = ontologyApi();
    definitionsApi([aDefinition({ valueKind: "select", options: [{ ...OPTION, archived: true }] })]);
    renderRoute("/types");

    expect(await screen.findByText(/Lendo \(arquivada\)/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Editar Status" }));
    const form = within(screen.getByRole("form", { name: "Propriedade Status" }));
    await userEvent.click(form.getByRole("button", { name: "Restaurar opção Lendo" }));
    await userEvent.click(form.getByRole("button", { name: "Salvar" }));

    await waitFor(() =>
      expect(sent).toContainEqual({
        method: "PATCH",
        path: `/api/property-definitions/${DEFINITION_ID}`,
        body: { name: "Status", valueKind: "select", options: [{ id: OPTION.id, label: "Lendo" }] },
      }),
    );
  });

  it("says in how many Types a property is used, archives it and no longer offers it to attach", async () => {
    const sent = ontologyApi();
    const archived = aDefinition({ id: "01920000-0000-7000-8000-0000000000b9", name: "Antiga", isArchived: true });
    definitionsApi([aDefinition({ typeCount: 2 }), archived]);
    renderRoute("/types");

    expect(await screen.findByText("Usada em 2 Types")).toBeInTheDocument();
    expect(screen.getByText("Em nenhum Type")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Arquivar propriedade Status" }));
    await waitFor(() =>
      expect(sent).toContainEqual({ method: "PATCH", path: `/api/property-definitions/${DEFINITION_ID}`, body: { archived: true } }),
    );

    await userEvent.click(screen.getByRole("button", { name: "Restaurar propriedade Antiga" }));
    await waitFor(() =>
      expect(sent).toContainEqual({ method: "PATCH", path: `/api/property-definitions/${archived.id}`, body: { archived: false } }),
    );

    await userEvent.click(screen.getByRole("combobox", { name: "Anexar propriedade" }));
    expect(await screen.findByRole("option", { name: "Status" })).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: "Antiga" })).not.toBeInTheDocument();
  });

  it("confirms a delete with how many Nodes lose their value, and asks again when the count changed", async () => {
    const sent = ontologyApi();
    usageApi(aUsage({ nodeCount: 2 }), aUsage({ nodeCount: 3 }));
    let deletes = 0;
    server.use(
      http.delete("/api/property-definitions/:id", ({ request }) => {
        const url = new URL(request.url);
        sent.push({ method: "DELETE", path: `${url.pathname}${url.search}`, body: null });
        deletes += 1;
        return deletes === 1 ? problem(409, "graph.property_usage_changed") : HttpResponse.json(aReceipt());
      }),
    );
    renderRoute("/types");

    await userEvent.click(await screen.findByRole("button", { name: "Apagar propriedade Status" }));
    const dialog = within(await screen.findByRole("dialog", { name: "Apagar a propriedade Status?" }));
    expect(await dialog.findByText("Os valores de 2 Nodes serão removidos. Dá para desfazer depois.")).toBeInTheDocument();
    await userEvent.click(dialog.getByRole("button", { name: "Apagar propriedade" }));

    expect(await dialog.findByRole("alert")).toHaveTextContent("A quantidade de Nodes com valores mudou");
    expect(await dialog.findByText("Os valores de 3 Nodes serão removidos. Dá para desfazer depois.")).toBeInTheDocument();
    await waitFor(() => expect(dialog.getByRole("button", { name: "Apagar propriedade" })).toBeEnabled());
    await userEvent.click(dialog.getByRole("button", { name: "Apagar propriedade" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(sent.filter((request) => request.method === "DELETE").map((request) => request.path)).toEqual([
      `/api/property-definitions/${DEFINITION_ID}?expectedValueCount=2`,
      `/api/property-definitions/${DEFINITION_ID}?expectedValueCount=3`,
    ]);
  });

  it("keeps a property deleted Nodes still hold from being deleted, with the count and the purge date", async () => {
    ontologyApi();
    usageApi(aUsage({ tombstoneCount: 2, lastPurgeAt: "2026-11-04T12:00:00Z" }));
    renderRoute("/types");

    await userEvent.click(await screen.findByRole("button", { name: "Apagar propriedade Status" }));
    const dialog = within(await screen.findByRole("dialog", { name: "Apagar a propriedade Status?" }));

    expect(await dialog.findByText(/2 Nodes apagados ainda guardam valores/)).toHaveTextContent("4 de novembro de 2026");
    expect(dialog.getByText(/faça o Purge desses Nodes/)).toBeInTheDocument();
    expect(dialog.getByRole("button", { name: "Apagar propriedade" })).toBeDisabled();
  });

  it("keeps the delete waiting when the count is unavailable, and reads it again on retry", async () => {
    ontologyApi();
    let reads = 0;
    server.use(
      http.get("/api/property-definitions/:id/usage", () => {
        reads += 1;
        return reads <= 2 ? problem(500, "unexpected_error") : HttpResponse.json(aUsage({ nodeCount: 1 }));
      }),
    );
    renderRoute("/types");

    await userEvent.click(await screen.findByRole("button", { name: "Apagar propriedade Status" }));
    const dialog = within(await screen.findByRole("dialog", { name: "Apagar a propriedade Status?" }));

    expect(await dialog.findByText(/Contagem indisponível/)).toBeInTheDocument();
    expect(dialog.getByRole("button", { name: "Apagar propriedade" })).toBeDisabled();
    await userEvent.click(dialog.getByRole("button", { name: "Tentar de novo" }));

    expect(await dialog.findByText("Os valores de 1 Node serão removidos. Dá para desfazer depois.")).toBeInTheDocument();
    expect(dialog.getByRole("button", { name: "Apagar propriedade" })).toBeEnabled();
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
    const sent = ontologyApi([aType({ properties: [{ propertyDefinitionId: DEFINITION_ID, name: "Status", valueKind: "select", isSystem: false, isArchived: false }] })]);
    renderRoute("/types");

    await userEvent.click(await screen.findByRole("button", { name: "Desanexar Status de Livro" }));

    await waitFor(() =>
      expect(sent).toContainEqual({ method: "DELETE", path: `/api/types/${TYPE_ID}/properties/${DEFINITION_ID}`, body: null }),
    );
  });

  it("shows the system Type and its system properties as locked, and lets a user property come and go", async () => {
    const resourceId = "01920000-0000-7000-8000-0000000000a2";
    const urlId = "01920000-0000-7000-8000-0000000000b2";
    const types = [
      aType(),
      aType({
        id: resourceId,
        name: "Resource",
        isSystem: true,
        properties: [
          { propertyDefinitionId: urlId, name: "url", valueKind: "url", isSystem: true, isArchived: false },
          { propertyDefinitionId: DEFINITION_ID, name: "Status", valueKind: "select", isSystem: false, isArchived: false },
        ],
      }),
    ];
    const sent = ontologyApi(types);
    server.use(
      ...ontologyHandlers({
        types,
        definitions: [aDefinition({ valueKind: "select", options: [OPTION] }), aDefinition({ id: urlId, name: "url", valueKind: "url", isSystem: true, isArchived: false })],
      }),
    );
    renderRoute("/types");

    const resource = within(await screen.findByRole("list", { name: "Propriedades de Resource" }));
    expect(resource.getByText("Sistema, travado")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Apagar Type Resource" })).not.toBeInTheDocument();
    expect(screen.queryByRole("textbox", { name: "Renomear Resource" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Desanexar url de Resource" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Editar url" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Apagar propriedade url" })).not.toBeInTheDocument();
    expect(screen.getAllByText("Sistema, travado").length).toBeGreaterThanOrEqual(3);
    expect(screen.getByRole("button", { name: "Apagar Type Livro" })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("combobox", { name: "Anexar propriedade" }));
    expect(screen.queryByRole("option", { name: "url" })).not.toBeInTheDocument();
    await userEvent.keyboard("{Escape}");

    await userEvent.click(screen.getByRole("button", { name: "Desanexar Status de Resource" }));
    await waitFor(() =>
      expect(sent).toContainEqual({ method: "DELETE", path: `/api/types/${resourceId}/properties/${DEFINITION_ID}`, body: null }),
    );
  });

  it("has no accessibility violations", async () => {
    ontologyApi();
    const { container } = renderRoute("/types");
    await screen.findByRole("list", { name: "Types" });

    expect(await axe(container)).toHaveNoViolations();
  });

  it("has no accessibility violations with the editor open and an option in use", async () => {
    ontologyApi();
    usageApi(aUsage({ nodeCount: 1, optionCounts: { [OPTION.id]: 1 } }));
    const { container } = renderRoute("/types");

    await userEvent.click(await screen.findByRole("button", { name: "Editar Status" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Remover opção Lendo" })).toBeDisabled());

    expect(await axe(container)).toHaveNoViolations();
  });

  it("has no accessibility violations in the delete dialog", async () => {
    ontologyApi();
    renderRoute("/types");

    await userEvent.click(await screen.findByRole("button", { name: "Apagar propriedade Status" }));
    const dialog = await screen.findByRole("dialog", { name: "Apagar a propriedade Status?" });
    await within(dialog).findByText(/Nenhum Node guarda valor/);

    expect(await axe(dialog)).toHaveNoViolations();
  });
});
