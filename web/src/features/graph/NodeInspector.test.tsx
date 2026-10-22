import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { MemoryRouter } from "react-router";
import { describe, expect, it } from "vitest";
import { aDefinition, aNode, aType, NODE_ID, ontologyHandlers, OTHER_NODE_ID, RELATION_ID } from "@/test/graphFixtures";
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
});
