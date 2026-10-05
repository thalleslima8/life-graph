import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { aDefinition, aNode, aReceipt, aType, DEFINITION_ID, NODE_ID, OTHER_NODE_ID, ontologyHandlers, TYPE_ID } from "@/test/graphFixtures";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";
import type { NodeDetail, RelationItem } from "./graphData";
import { graphKeys } from "./graphData";

const OPTIONS = [
  { id: "01920000-0000-7000-8000-0000000000e1", label: "Lendo" },
  { id: "01920000-0000-7000-8000-0000000000e2", label: "Lido" },
];
const PAGES_ID = "01920000-0000-7000-8000-0000000000b2";
const RELATION_ID = "01920000-0000-7000-8000-0000000000f1";

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

/** Records each write the inspector sends, by method and path, with its body. */
function recordWrites() {
  const sent: { method: string; path: string; body: unknown }[] = [];
  const record = async ({ request }: { request: Request }) => {
    const text = await request.text();
    sent.push({ method: request.method, path: new URL(request.url).pathname, body: text ? JSON.parse(text) : null });
    return HttpResponse.json(aReceipt());
  };
  server.use(http.post("/api/relations", record), http.delete("/api/relations/:relationId", record), http.patch("/api/nodes/:nodeId", record));
  return sent;
}

const book = aNode({
  typeId: TYPE_ID,
  typeName: "Livro",
  properties: [{ propertyDefinitionId: DEFINITION_ID, name: "Status", valueKind: "select", value: OPTIONS[0]?.id ?? null }],
  otherProperties: [{ propertyDefinitionId: PAGES_ID, name: "Páginas", valueKind: "number", value: 412 }],
});

/** One Node served from `current`, which a test may replace to simulate a change made elsewhere. */
function inspectorApi(initial: NodeDetail | null) {
  const state = { current: initial, patches: [] as unknown[], deletes: [] as string[] };
  server.use(
    sessionHandler(() => ({ email: "ada@example.test" })),
    csrfTokenHandler().handler,
    ...ontologyHandlers({
      types: [aType()],
      definitions: [aDefinition({ valueKind: "select", options: OPTIONS }), aDefinition({ id: PAGES_ID, name: "Páginas", valueKind: "number" })],
    }),
    http.get("/api/nodes", () => HttpResponse.json({ data: [], page: { nextCursor: null } })),
    http.get("/api/nodes/:nodeId", () => (state.current ? HttpResponse.json(state.current) : problem(404, "graph.node_not_found"))),
    http.get("/api/nodes/:nodeId/relations", () => HttpResponse.json({ data: [], page: { nextCursor: null } })),
    http.delete("/api/nodes/:nodeId", ({ request }) => {
      state.deletes.push(new URL(request.url).search);
      state.current = null;
      return HttpResponse.json(aReceipt());
    }),
  );
  return state;
}

describe("node inspector", () => {
  it("shows the Node, its Type's properties with their options, and Outras propriedades", async () => {
    inspectorApi(book);
    renderRoute(`/nodes/${NODE_ID}`);

    expect(await screen.findByRole("heading", { level: 1, name: "Leituras" })).toBeInTheDocument();
    expect(await screen.findByRole("combobox", { name: "Status" })).toHaveTextContent("Lendo");
    expect(screen.getByRole("heading", { level: 2, name: "Outras propriedades" })).toBeInTheDocument();
    expect(screen.getByText("412")).toBeInTheDocument();
  });

  it("saves only what changed, with the version the edit started from", async () => {
    const state = inspectorApi(book);
    server.use(
      http.patch("/api/nodes/:nodeId", async ({ request }) => {
        state.patches.push(await request.json());
        return HttpResponse.json(aReceipt());
      }),
    );
    renderRoute(`/nodes/${NODE_ID}`);

    await userEvent.click(await screen.findByRole("combobox", { name: "Status" }));
    await userEvent.click(await screen.findByRole("option", { name: "Lido" }));
    await userEvent.click(screen.getByRole("button", { name: "Salvar" }));

    await screen.findByRole("button", { name: "Salvar" });
    expect(state.patches).toEqual([{ version: 1, properties: { [DEFINITION_ID]: OPTIONS[1]?.id } }]);
  });

  it("warns when the Node changed elsewhere during the edit, and the save is refused, not overwritten", async () => {
    const state = inspectorApi(book);
    server.use(
      http.patch("/api/nodes/:nodeId", async ({ request }) => {
        state.patches.push(await request.json());
        return problem(409, "graph.node_version_conflict", { details: { version: ["2"] } });
      }),
    );
    const { queryClient } = renderRoute(`/nodes/${NODE_ID}`);
    await userEvent.type(await screen.findByLabelText("Título"), " de outubro");

    // An agent edits the Node; the refetch brings version 2 into the changed form.
    state.current = { ...book, title: "Leituras do agente", version: 2 };
    await queryClient.invalidateQueries({ queryKey: graphKeys.node(NODE_ID) });

    expect(await screen.findByText("Este Node mudou desde que você começou a editar.")).toBeInTheDocument();
    expect(screen.getByLabelText("Título")).toHaveValue("Leituras de outubro");

    await userEvent.click(screen.getByRole("button", { name: "Salvar" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Este Node foi alterado em outro lugar.");
    expect(state.patches).toEqual([{ version: 1, title: "Leituras de outubro" }]);

    await userEvent.click(screen.getByRole("button", { name: /Recarregar/ }));
    expect(await screen.findByLabelText("Título")).toHaveValue("Leituras do agente");
  });

  it("says a Node deleted after it was shown is gone, with a way to undo", async () => {
    const state = inspectorApi(book);
    renderRoute(`/nodes/${NODE_ID}`);

    await userEvent.click(await screen.findByRole("button", { name: "Apagar" }));
    await userEvent.click(screen.getByRole("button", { name: "Confirmar exclusão" }));

    expect(await screen.findByRole("heading", { level: 1, name: "Node indisponível" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Desfazer em Recent Changes" })).toHaveAttribute("href", "/changes");
    expect(state.deletes).toEqual(["?version=1"]);
  });

  it("says a Node opened by a link that does not exist is not found", async () => {
    inspectorApi(null);
    renderRoute(`/nodes/${NODE_ID}`);

    expect(await screen.findByRole("heading", { level: 1, name: "Node não encontrado" })).toBeInTheDocument();
  });

  it("changes the Node's Type, sending only the new Type", async () => {
    inspectorApi(aNode());
    const sent = recordWrites();
    renderRoute(`/nodes/${NODE_ID}`);

    await userEvent.click(await screen.findByRole("combobox", { name: "Type" }));
    await userEvent.click(await screen.findByRole("option", { name: "Livro" }));
    expect(screen.getByText(/os valores que o novo Type não tem ficam em Outras propriedades/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Salvar" }));

    await screen.findByRole("button", { name: "Salvar" });
    expect(sent).toEqual([{ method: "PATCH", path: `/api/nodes/${NODE_ID}`, body: { version: 1, type: { id: TYPE_ID } } }]);
  });

  it("relates the Node to another one, loading more candidates on request", async () => {
    inspectorApi(aNode());
    const sent = recordWrites();
    server.use(
      http.get("/api/nodes", ({ request }) =>
        new URL(request.url).searchParams.get("cursor") === "page-2"
          ? HttpResponse.json({ data: [aNode({ id: OTHER_NODE_ID, title: "Meditações" })], page: { nextCursor: null } })
          : HttpResponse.json({ data: [aNode()], page: { nextCursor: "page-2" } }),
      ),
    );
    renderRoute(`/nodes/${NODE_ID}`);

    await userEvent.click(await screen.findByRole("button", { name: "Carregar mais Nodes" }));
    await screen.findByText("Nenhuma Relation.");
    await userEvent.type(screen.getByLabelText("Tipo da Relation"), "cita");
    await userEvent.click(screen.getByRole("combobox", { name: "Com o Node" }));
    await userEvent.click(await screen.findByRole("option", { name: "Meditações" }));
    await userEvent.click(screen.getByRole("button", { name: "Relacionar" }));

    await waitFor(() =>
      expect(sent).toEqual([{ method: "POST", path: "/api/relations", body: { sourceNodeId: NODE_ID, targetNodeId: OTHER_NODE_ID, kind: "cita" } }]),
    );
    expect(screen.queryByRole("button", { name: "Carregar mais Nodes" })).not.toBeInTheDocument();
  });

  it("says when the Nodes to relate to could not load, and retries", async () => {
    inspectorApi(aNode());
    let fail = true;
    server.use(
      http.get("/api/nodes", () =>
        fail ? problem(500, "unexpected_error") : HttpResponse.json({ data: [aNode({ id: OTHER_NODE_ID, title: "Meditações" })], page: { nextCursor: null } }),
      ),
    );
    renderRoute(`/nodes/${NODE_ID}`);

    expect(await screen.findByText("Carregando Nodes…")).toBeInTheDocument();
    const form = await screen.findByRole("form", { name: "Nova Relation" });
    expect(await within(form).findByRole("alert")).toHaveTextContent("Não foi possível carregar.");
    expect(within(form).getByRole("button", { name: "Relacionar" })).toBeDisabled();

    fail = false;
    await userEvent.click(within(form).getByRole("button", { name: "Tentar de novo" }));
    await waitFor(() => expect(within(form).getByRole("button", { name: "Relacionar" })).toBeEnabled());
  });

  it("removes a Relation", async () => {
    inspectorApi(aNode());
    const sent = recordWrites();
    server.use(http.get("/api/nodes/:nodeId/relations", () => HttpResponse.json({ data: [citation], page: { nextCursor: null } })));
    renderRoute(`/nodes/${NODE_ID}`);

    await userEvent.click(await screen.findByRole("button", { name: "Remover Relation cita com Meditações" }));

    await waitFor(() => expect(sent).toEqual([{ method: "DELETE", path: `/api/relations/${RELATION_ID}`, body: null }]));
  });

  it("builds no form until the properties load, so a stored option never shows as removed", async () => {
    inspectorApi(book);
    let release: () => void = () => undefined;
    const loaded = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get("/api/property-definitions", async () => {
        await loaded;
        return HttpResponse.json({ data: [aDefinition({ valueKind: "select", options: OPTIONS })], page: { nextCursor: null } });
      }),
    );
    renderRoute(`/nodes/${NODE_ID}`);

    expect(await screen.findByText("Carregando as propriedades…")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Salvar" })).not.toBeInTheDocument();
    expect(screen.queryByText("(opção removida)")).not.toBeInTheDocument();

    release();
    expect(await screen.findByRole("combobox", { name: "Status" })).toHaveTextContent("Lendo");
  });

  it("says when the properties could not load, and retries", async () => {
    inspectorApi(book);
    let fail = true;
    server.use(
      http.get("/api/property-definitions", () =>
        fail
          ? problem(500, "unexpected_error")
          : HttpResponse.json({ data: [aDefinition({ valueKind: "select", options: OPTIONS })], page: { nextCursor: null } }),
      ),
    );
    renderRoute(`/nodes/${NODE_ID}`);

    expect(await screen.findByText("Não foi possível carregar. Tente novamente em instantes.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Salvar" })).not.toBeInTheDocument();

    fail = false;
    await userEvent.click(screen.getByRole("button", { name: "Tentar de novo" }));
    expect(await screen.findByRole("combobox", { name: "Status" })).toHaveTextContent("Lendo");
  });

  it("has no accessibility violations", async () => {
    inspectorApi(book);
    const { container } = renderRoute(`/nodes/${NODE_ID}`);
    await screen.findByRole("combobox", { name: "Status" });

    expect(await axe(container)).toHaveNoViolations();
  });
});
