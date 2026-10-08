import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { aNode, aReceipt, aType, NODE_ID, ontologyHandlers, TYPE_ID } from "@/test/graphFixtures";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";

const typed = aNode({ id: "01920000-0000-7000-8000-000000000003", title: "Duna", typeId: TYPE_ID });
const untyped = aNode({ title: "Ideia solta", inInbox: true });

/** Answers the list by its filter and records each query. */
function nodesApi() {
  const queries: string[] = [];
  let inbox = [untyped];
  server.use(
    sessionHandler(() => ({ email: "ada@example.test" })),
    csrfTokenHandler().handler,
    ...ontologyHandlers({ types: [aType()] }),
    http.get("/api/nodes", ({ request }) => {
      const query = new URL(request.url).searchParams;
      queries.push(query.toString());
      const data = query.get("inInbox") === "true" ? inbox : query.get("withoutType") === "true" ? [untyped] : [typed, untyped];
      return HttpResponse.json({ data, page: { nextCursor: null } });
    }),
    http.post("/api/nodes/:nodeId/archival", () => {
      inbox = [];
      return HttpResponse.json(aReceipt());
    }),
  );
  return queries;
}

describe("node list", () => {
  beforeEach(() => {
    window.history.replaceState(null, "", "/");
  });

  it("lists the Nodes with their Type, and 'Sem Type' for the others", async () => {
    nodesApi();
    renderRoute("/nodes");

    const list = await screen.findByRole("list", { name: "Nodes" });
    expect(within(list).getByRole("link", { name: "Duna" })).toHaveAttribute("href", `/nodes/${typed.id}`);
    expect(await within(list).findByText("Livro")).toBeInTheDocument();
    expect(within(list).getByText("Sem Type")).toBeInTheDocument();
  });

  it("says a Type is loading or unavailable instead of calling the Node 'Sem Type'", async () => {
    nodesApi();
    let release: () => void = () => undefined;
    const loaded = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get("/api/types", async () => {
        await loaded;
        return problem(500, "unexpected_error");
      }),
    );
    renderRoute("/nodes");

    const list = await screen.findByRole("list", { name: "Nodes" });
    expect(within(list).getByText("Carregando Type…")).toBeInTheDocument();
    expect(within(list).getByText("Sem Type")).toBeInTheDocument();
    expect(screen.getByText("Carregando Types…")).toBeInTheDocument();

    release();
    expect(await within(list).findByText("Type indisponível")).toBeInTheDocument();
    expect(within(list).getByText("Sem Type")).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent("Não foi possível carregar os Types.");
  });

  it("filters the Nodes without a Type, keeping the filter in the URL", async () => {
    const queries = nodesApi();
    const { router } = renderRoute("/nodes");
    await screen.findByRole("list", { name: "Nodes" });

    await userEvent.click(screen.getByRole("combobox", { name: "Type" }));
    await userEvent.click(await screen.findByRole("option", { name: "Sem Type" }));

    // "Ideia solta" is in both lists: wait for the filtered one to replace the first.
    await waitFor(() => expect(queries.at(-1)).toBe("withoutType=true"));
    expect(await screen.findByRole("link", { name: "Ideia solta" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Duna" })).not.toBeInTheDocument();
    expect(router.state.location.search).toBe("?type=none");
  });

  it("shows the Inbox and archives a Node out of it", async () => {
    nodesApi();
    renderRoute("/nodes?inbox=1");

    expect(await screen.findByRole("heading", { level: 1, name: "Inbox" })).toBeInTheDocument();
    await userEvent.click(await screen.findByRole("button", { name: "Arquivar Ideia solta" }));

    expect(await screen.findByText("A Inbox está vazia.")).toBeInTheDocument();
  });

  it("creates a Node and opens it in the Inspector", async () => {
    nodesApi();
    const created: unknown[] = [];
    server.use(
      http.post("/api/nodes", async ({ request }) => {
        created.push(await request.json());
        return HttpResponse.json({ changeSetId: null, changes: [{ kind: "node", entityId: NODE_ID, operation: "created", version: 1 }] }, { status: 201 });
      }),
      http.get("/api/nodes/:nodeId", () => HttpResponse.json(aNode({ title: "Nova ideia" }))),
      http.get("/api/nodes/:nodeId/relations", () => HttpResponse.json({ data: [], page: { nextCursor: null } })),
    );
    const { router } = renderRoute("/nodes?inbox=1");

    await userEvent.type(await screen.findByLabelText("Capturar na Inbox"), "Nova ideia");
    await userEvent.click(screen.getByRole("button", { name: "Criar" }));

    expect(await screen.findByRole("heading", { level: 1, name: "Nova ideia" })).toBeInTheDocument();
    expect(created).toEqual([{ title: "Nova ideia", inInbox: true }]);
    expect(router.state.location.pathname).toBe(`/nodes/${NODE_ID}`);
  });

  it("has no accessibility violations", async () => {
    nodesApi();
    const { container } = renderRoute("/nodes");
    await screen.findByRole("list", { name: "Nodes" });

    expect(await axe(container)).toHaveNoViolations();
  });
});
