import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { MemoryRouter } from "react-router";
import { describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { aCollection, aDefinition, aNode, aReceipt, aTag, aType, COLLECTION_ID, NODE_ID, ontologyHandlers, OTHER_NODE_ID, RELATION_ID, TAG_ID, TYPE_ID } from "@/test/graphFixtures";
import { csrfTokenHandler, problem } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import type { NodeDetail, PropertyDefinitionItem, RelationItem } from "./graphData";
import { NodeInspector } from "./NodeInspector";

const citation: RelationItem = {
  id: RELATION_ID,
  kind: "cita",
  sourceNodeId: NODE_ID,
  sourceNodeTitle: "Leituras",
  targetNodeId: OTHER_NODE_ID,
  targetNodeTitle: "Meditações",
  assertion: "hard",
  origin: "user",
  confidence: null,
  strength: 1,
  createdAt: "2026-10-05T12:00:00Z",
};

function renderInspector(
  props: { headingLevel?: 1 | 2 | 3 | 4 | 5; readOnly?: boolean },
  node: NodeDetail = aNode({ body: "Notas de leitura", inInbox: true }),
  definitions: PropertyDefinitionItem[] = [],
) {
  server.use(
    csrfTokenHandler().handler,
    ...ontologyHandlers({ types: [aType()], definitions }),
    http.get("/api/nodes", () => HttpResponse.json({ data: [], page: { nextCursor: null } })),
    http.get("/api/nodes/:nodeId", () => HttpResponse.json(node)),
    http.get("/api/nodes/:nodeId/relations", () => HttpResponse.json({ data: [citation], page: { nextCursor: null } })),
  );
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <NodeInspector nodeId={NODE_ID} {...props} />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe("embedded node inspector", () => {
  it("puts its heading at the host's level and its sections one below", async () => {
    renderInspector({ headingLevel: 3 });

    expect(await screen.findByRole("heading", { level: 3, name: "Leituras" })).toBeInTheDocument();
    expect(await screen.findByRole("heading", { level: 4, name: "Editar" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 4, name: "Relations" })).toBeInTheDocument();
  });

  it("shows the Node without any write control when read only", async () => {
    renderInspector({ readOnly: true });

    const content = await screen.findByRole("region", { name: "Conteúdo" });
    expect(within(content).getByText("Notas de leitura")).toBeInTheDocument();
    expect(await screen.findByRole("link", { name: "Meditações" })).toBeInTheDocument();
    expect(screen.queryByRole("form", { name: "Editar" })).not.toBeInTheDocument();
    expect(screen.queryByRole("form", { name: "Nova Relation" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Arquivar" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Remover/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Ver no grafo" })).not.toBeInTheDocument();
  });

  describe("a Resource", () => {
    const KIND_ID = "01920000-0000-7000-8000-0000000000b3";
    const URL_ID = "01920000-0000-7000-8000-0000000000b4";
    const CANONICAL_ID = "01920000-0000-7000-8000-0000000000b5";
    const definitions = [
      aDefinition({ id: KIND_ID, name: "resource_kind", isSystem: true }),
      aDefinition({ id: URL_ID, name: "url", valueKind: "url", isSystem: true }),
      aDefinition({ id: CANONICAL_ID, name: "canonical_url", valueKind: "url", isSystem: true }),
    ];
    const resource = aNode({
      properties: [
        { propertyDefinitionId: KIND_ID, name: "resource_kind", valueKind: "text", value: "podcast" },
        { propertyDefinitionId: URL_ID, name: "url", valueKind: "url", value: "https://example.com/a" },
        { propertyDefinitionId: CANONICAL_ID, name: "canonical_url", valueKind: "url", value: "https://example.com/a" },
      ],
    });

    it("shows a kind outside the closed list as other", async () => {
      renderInspector({ readOnly: true }, resource, definitions);

      const content = await screen.findByRole("region", { name: "Conteúdo" });
      expect(await within(content).findByText("other")).toBeInTheDocument();
      expect(within(content).queryByText("podcast")).not.toBeInTheDocument();
    });

    it("lets the person edit url but not the canonical URL the server sets", async () => {
      renderInspector({}, resource, definitions);

      expect(await screen.findByRole("textbox", { name: "url" })).toBeInTheDocument();
      expect(screen.queryByRole("textbox", { name: "canonical_url" })).not.toBeInTheDocument();
      expect(screen.getByText("canonical_url")).toBeInTheDocument();
    });
  });

  describe("tags", () => {
    const NEW_TAG_ID = "01920000-0000-7000-8000-0000000000a8";
    const tagged = aNode({ tags: [{ id: TAG_ID, name: "leitura" }] });

    function serveTags(tags = [aTag(), aTag({ id: NEW_TAG_ID, name: "projeto" })]) {
      server.use(http.get("/api/tags", () => HttpResponse.json({ data: tags, page: { nextCursor: null } })));
    }

    it("shows the Node's Tags as chips only when read only", async () => {
      renderInspector({ readOnly: true }, tagged);

      const chips = await screen.findByRole("list", { name: "Tags do Node" });
      expect(within(chips).getByText("#leitura")).toBeInTheDocument();
      expect(screen.queryByRole("combobox", { name: "Adicionar Tag" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Remover a Tag leitura" })).not.toBeInTheDocument();
    });

    it("applies an existing Tag picked from the list, without a version", async () => {
      serveTags();
      let appliedPath: string | undefined;
      server.use(
        http.put("/api/nodes/:nodeId/tags/:tagId", ({ params, request }) => {
          appliedPath = `${String(params.nodeId)}/${String(params.tagId)}?${new URL(request.url).search}`;
          return HttpResponse.json(aReceipt());
        }),
      );
      renderInspector({});

      await userEvent.type(await screen.findByRole("combobox", { name: "Adicionar Tag" }), "proj");
      await userEvent.click(await screen.findByRole("option", { name: "#projeto" }));

      await expect.poll(() => appliedPath).toBe(`${NODE_ID}/${NEW_TAG_ID}?`);
    });

    it("does not offer a Tag the Node already has, nor to create it again", async () => {
      serveTags();
      renderInspector({}, tagged);

      await userEvent.type(await screen.findByRole("combobox", { name: "Adicionar Tag" }), "LEITURA");

      expect(screen.queryByRole("option")).not.toBeInTheDocument();
    });

    it("creates a Tag by name and applies it", async () => {
      serveTags([]);
      let created: unknown;
      let applied: string | undefined;
      server.use(
        http.post("/api/tags", async ({ request }) => {
          created = await request.json();
          return HttpResponse.json(
            { changeSetId: "cs", changes: [{ kind: "tag", entityId: NEW_TAG_ID, operation: "created", version: null }], relationsLeftDeleted: [] },
            { status: 201 },
          );
        }),
        http.put("/api/nodes/:nodeId/tags/:tagId", ({ params }) => {
          applied = String(params.tagId);
          return HttpResponse.json(aReceipt());
        }),
      );
      renderInspector({});

      await userEvent.type(await screen.findByRole("combobox", { name: "Adicionar Tag" }), "#Ideias{Enter}");

      await expect.poll(() => created).toEqual({ name: "Ideias" });
      await expect.poll(() => applied).toBe(NEW_TAG_ID);
    });

    it("applies the existing Tag when another one with the same name was created meanwhile", async () => {
      let reads = 0;
      server.use(
        http.get("/api/tags", () => {
          reads += 1;
          return HttpResponse.json({ data: reads === 1 ? [] : [aTag({ id: NEW_TAG_ID, name: "Ideias" })], page: { nextCursor: null } });
        }),
        http.post("/api/tags", () => problem(409, "graph.tag_name_taken")),
      );
      let applied: string | undefined;
      server.use(
        http.put("/api/nodes/:nodeId/tags/:tagId", ({ params }) => {
          applied = String(params.tagId);
          return HttpResponse.json(aReceipt());
        }),
      );
      renderInspector({});

      await userEvent.type(await screen.findByRole("combobox", { name: "Adicionar Tag" }), "ideias");
      await userEvent.click(await screen.findByRole("option", { name: "Criar «ideias»" }));

      await expect.poll(() => applied).toBe(NEW_TAG_ID);
      expect(screen.queryByText("Já existe uma Tag com esse nome.")).not.toBeInTheDocument();
    });

    it("removes a Tag from the Node", async () => {
      serveTags();
      let removed: string | undefined;
      server.use(
        http.delete("/api/nodes/:nodeId/tags/:tagId", ({ params }) => {
          removed = String(params.tagId);
          return HttpResponse.json(aReceipt());
        }),
      );
      renderInspector({}, tagged);

      await userEvent.click(await screen.findByRole("button", { name: "Remover a Tag leitura" }));

      await expect.poll(() => removed).toBe(TAG_ID);
    });

    it("disables the field when the Tags cannot be read", async () => {
      server.use(http.get("/api/tags", () => problem(500, "unexpected")));
      renderInspector({}, tagged);

      expect(await screen.findByText("Tags indisponíveis")).toBeInTheDocument();
      expect(screen.getByRole("combobox", { name: "Adicionar Tag" })).toBeDisabled();
    });

    it("has no accessibility violations with the list open", async () => {
      serveTags();
      const { container } = renderInspector({}, tagged);

      await userEvent.type(await screen.findByRole("combobox", { name: "Adicionar Tag" }), "p");
      await screen.findByRole("option", { name: "#projeto" });

      expect(await axe(container)).toHaveNoViolations();
      expect(await axe(screen.getByRole("listbox", { name: "Tags" }))).toHaveNoViolations();
    });
  });

  describe("collections", () => {
    it("adds the Node to a manual Collection and offers only manual ones", async () => {
      let addedPath: string | undefined;
      server.use(
        http.get("/api/collections", () =>
          HttpResponse.json({
            data: [aCollection(), aCollection({ id: "c2", name: "Livros", criteria: { version: 1, match: "all", criteria: [{ type: "tag", tagId: TAG_ID }] } })],
            page: { nextCursor: null },
          }),
        ),
        http.put("/api/collections/:collectionId/members/:nodeId", ({ params }) => {
          addedPath = `${String(params.collectionId)}/${String(params.nodeId)}`;
          return HttpResponse.json(aReceipt());
        }),
      );
      renderInspector({});

      await userEvent.click(await screen.findByRole("combobox", { name: "Collection manual" }));
      expect(screen.queryByRole("option", { name: "Livros" })).not.toBeInTheDocument();
      await userEvent.click(await screen.findByRole("option", { name: "Quero ler" }));
      await userEvent.click(screen.getByRole("button", { name: "Adicionar" }));

      await expect.poll(() => addedPath).toBe(`${COLLECTION_ID}/${NODE_ID}`);
      expect(await screen.findByText("Adicionado.")).toBeInTheDocument();
    });

    it("points to the Collections page when there is no manual Collection", async () => {
      renderInspector({});

      expect(await screen.findByText("Nenhuma Collection manual.")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Criar em Collections" })).toHaveAttribute("href", "/collections");
    });

    it("offers no Collection control when read only", async () => {
      renderInspector({ readOnly: true });

      await screen.findByRole("region", { name: "Conteúdo" });
      expect(screen.queryByRole("heading", { name: "Collections" })).not.toBeInTheDocument();
    });
  });

  describe("promoting an other property", () => {
    const TRANSLATOR_ID = "01920000-0000-7000-8000-0000000000f1";
    const translator = aDefinition({ id: TRANSLATOR_ID, name: "Tradutor" });
    const typedNode = (overrides: Partial<NodeDetail> = {}) =>
      aNode({
        typeId: TYPE_ID,
        typeName: "Livro",
        otherProperties: [{ propertyDefinitionId: TRANSLATOR_ID, name: "Tradutor", valueKind: "text", value: "Gregory Hays" }],
        ...overrides,
      });

    it("attaches the row's own definition to the Node's Type and says every Node of the Type gets it", async () => {
      let attachedPath: string | undefined;
      server.use(
        http.put("/api/types/:typeId/properties/:propertyDefinitionId", ({ params }) => {
          attachedPath = `${String(params.typeId)}/${String(params.propertyDefinitionId)}`;
          return HttpResponse.json(aReceipt());
        }),
      );
      renderInspector({}, typedNode(), [translator]);

      await userEvent.click(await screen.findByRole("button", { name: "Promover Tradutor" }));
      const dialog = await screen.findByRole("dialog", { name: "Promover Tradutor para o Type Livro?" });
      expect(dialog).toHaveAccessibleDescription(/todos os Nodes desse Type a mostram/);
      await userEvent.click(within(dialog).getByRole("button", { name: "Promover" }));

      await expect.poll(() => attachedPath).toBe(`${TYPE_ID}/${TRANSLATOR_ID}`);
      await expect.poll(() => screen.queryByRole("dialog")).toBeNull();
    });

    it("lists the existing definitions with the same name ignoring case and accents and a kind the value could take", async () => {
      const definitions = [
        aDefinition({ id: TRANSLATOR_ID, name: "Tradutor", valueKind: "date" }),
        aDefinition({ id: "same-name-text", name: "tradútor", valueKind: "text" }),
        aDefinition({ id: "same-name-date-time", name: "TRADUTOR", valueKind: "date_time" }),
        aDefinition({ id: "same-name-number", name: "Tradutor", valueKind: "number" }),
        aDefinition({ id: "same-name-archived", name: "Tradutor", valueKind: "date", isArchived: true }),
        aDefinition({ id: "other-name", name: "Autor", valueKind: "text" }),
      ];
      renderInspector(
        {},
        typedNode({ otherProperties: [{ propertyDefinitionId: TRANSLATOR_ID, name: "Tradutor", valueKind: "date", value: "2026-10-05" }] }),
        definitions,
      );

      await userEvent.click(await screen.findByRole("button", { name: "Promover Tradutor" }));
      const dialog = await screen.findByRole("dialog");
      const listed = within(dialog)
        .getAllByRole("listitem")
        .map((item) => item.textContent);
      expect(listed).toEqual([expect.stringMatching(/^tradútor \(/), expect.stringMatching(/^TRADUTOR \(/)]);
      expect(within(dialog).getByText(/Mover o valor para uma delas ainda não é possível/)).toBeInTheDocument();
      expect(await axe(document.body)).toHaveNoViolations();
    });

    it("is disabled with the reason when the Node has no Type", async () => {
      renderInspector({}, typedNode({ typeId: null, typeName: null }), [translator]);

      const promote = await screen.findByRole("button", { name: "Promover Tradutor" });
      expect(promote).toBeDisabled();
      expect(promote).toHaveAccessibleDescription("Sem Type: escolha um Type para o Node antes de promover.");
    });

    it("is disabled with the reason when the definition is archived", async () => {
      renderInspector({}, typedNode(), [aDefinition({ id: TRANSLATOR_ID, name: "Tradutor", isArchived: true })]);

      const promote = await screen.findByRole("button", { name: "Promover Tradutor" });
      expect(promote).toBeDisabled();
      expect(promote).toHaveAccessibleDescription("Propriedade arquivada: restaure-a em Ontologia para promover.");
    });

    it("is not offered when read only", async () => {
      renderInspector({ readOnly: true }, typedNode(), [translator]);

      expect(await screen.findByText("Gregory Hays")).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: /^Promover/ })).not.toBeInTheDocument();
    });
  });
});
