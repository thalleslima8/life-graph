import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { delay, http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { axe } from "vitest-axe";
import type { components } from "@/api/schema.gen";
import { fakeGraphRenderer as fake } from "@/test/fakeGraphRenderer";
import { NODE_ID, OTHER_NODE_ID } from "@/test/graphFixtures";
import { server } from "@/test/msw/server";
import { toVisitorGraphView, toVisitorNodeDetails } from "./visitorApi";
import { VisitorPage } from "./VisitorPage";
import {
  LINK_UNAVAILABLE_MESSAGE,
  LINK_UNAVAILABLE_TITLE,
  REPORT_ABUSE_LABEL,
} from "./visitorMessages";

vi.mock("@/features/graph/graphRenderer", async () => ({
  createSigmaRenderer: (await import("@/test/fakeGraphRenderer"))
    .fakeGraphRenderer.factory,
}));

type Schemas = components["schemas"];

const TOKEN = "k3Y-link-token_0123456789abcdefghijklmnopq";
const RELATION_ID = "01920000-0000-7000-8000-0000000000f1";

function aVisitorGraph(): Schemas["VisitorGraph"] {
  return {
    rootNodeId: NODE_ID,
    publicRef: "shr_abc",
    nodes: [
      { id: NODE_ID, title: "Estoicismo", typeName: null, distance: 0 },
      { id: OTHER_NODE_ID, title: "Sêneca", typeName: "Pessoa", distance: "1" },
    ],
    relations: [
      {
        id: RELATION_ID,
        sourceNodeId: NODE_ID,
        targetNodeId: OTHER_NODE_ID,
        kind: "about",
      },
    ],
  };
}

function aVisitorNode(): Schemas["VisitorNode"] {
  return {
    id: OTHER_NODE_ID,
    title: "Sêneca",
    body: "Cartas a Lucílio",
    typeName: "Pessoa",
    resourceUrl: "https://example.test/cartas",
    properties: [{ name: "Nascimento", valueKind: "number", value: 4 }],
    tags: ["filosofia"],
    relations: [
      {
        id: RELATION_ID,
        sourceNodeId: NODE_ID,
        targetNodeId: OTHER_NODE_ID,
        kind: "about",
      },
    ],
  };
}

/** The visitor's API, recording each request's credentials and Authorization header. */
function visitorApi({
  graph = () => HttpResponse.json(aVisitorGraph()),
  node = () => HttpResponse.json(aVisitorNode()),
}: {
  graph?: () => Response | Promise<Response>;
  node?: () => Response | Promise<Response>;
} = {}) {
  const requests: {
    url: string;
    authorization: string | null;
    cookie: string | null;
    credentials: RequestCredentials;
  }[] = [];
  const record = (request: Request) =>
    requests.push({
      url: new URL(request.url).pathname,
      authorization: request.headers.get("Authorization"),
      cookie: request.headers.get("Cookie"),
      credentials: request.credentials,
    });
  server.use(
    http.get("/api/shared", ({ request }) => {
      record(request);
      return graph();
    }),
    http.get("/api/shared/nodes/:nodeId", ({ request }) => {
      record(request);
      return node();
    }),
  );
  return requests;
}

function openLink(fragment: string) {
  window.history.replaceState(null, "", `/s${fragment}`);
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <VisitorPage />
    </QueryClientProvider>,
  );
}

async function expectUnavailable() {
  expect(
    await screen.findByRole("heading", { name: LINK_UNAVAILABLE_TITLE }),
  ).toBeInTheDocument();
  expect(screen.getByRole("alert")).toHaveTextContent(LINK_UNAVAILABLE_MESSAGE);
}

describe("the visitor's page", () => {
  beforeEach(() => fake.reset());

  it("takes the token out of the address and sends it only in the Authorization header, without credentials", async () => {
    const requests = visitorApi();

    openLink(`#${TOKEN}`);

    expect(window.location.hash).toBe("");
    expect(window.location.pathname).toBe("/s");
    expect(
      await screen.findByRole("heading", { level: 1, name: "Estoicismo" }),
    ).toBeInTheDocument();
    expect(requests).toEqual([
      {
        url: "/api/shared",
        authorization: `Bearer ${TOKEN}`,
        cookie: null,
        credentials: "omit",
      },
    ]);
  });

  it("shows the loading state until the shared graph arrives", async () => {
    let release: () => void = () => undefined;
    const arrived = new Promise<void>((resolve) => {
      release = resolve;
    });
    visitorApi({
      graph: async () => {
        await arrived;
        return HttpResponse.json(aVisitorGraph());
      },
    });

    openLink(`#${TOKEN}`);

    expect(await screen.findByRole("status")).toHaveTextContent("Carregando…");
    await act(async () => release());
    expect(
      await screen.findByRole("heading", { level: 1, name: "Estoicismo" }),
    ).toBeInTheDocument();
  });

  it("draws the shared graph read-only, rooted at the share's root", async () => {
    visitorApi();

    openLink(`#${TOKEN}`);

    await screen.findByRole("heading", { level: 1, name: "Estoicismo" });
    const drawn = fake.recorded.mounts.at(-1)?.view.graph;
    expect(drawn?.rootId).toBe(NODE_ID);
    expect(drawn?.nodes.map((node) => node.id)).toEqual([
      NODE_ID,
      OTHER_NODE_ID,
    ]);
    expect(drawn?.edges.map((edge) => edge.id)).toEqual([RELATION_ID]);
    expect(
      screen.queryByRole("button", { name: /raiz/i }),
    ).not.toBeInTheDocument();
  });

  it("lists the members, opens one in the Inspector and closes it, with no accessibility violation", async () => {
    const requests = visitorApi();
    const user = userEvent.setup();
    const { container } = openLink(`#${TOKEN}`);

    await user.click(await screen.findByRole("radio", { name: "Lista" }));
    await user.click(screen.getByRole("button", { name: "Sêneca" }));

    const inspector = await screen.findByRole("region", { name: "Inspector" });
    expect(
      await within(inspector).findByRole("heading", {
        level: 2,
        name: "Sêneca",
      }),
    ).toHaveFocus();
    expect(inspector).toHaveTextContent("Cartas a Lucílio");
    expect(
      within(inspector).getByRole("link", {
        name: "https://example.test/cartas",
      }),
    ).toHaveAttribute("rel", "noopener noreferrer");
    expect(inspector).toHaveTextContent("Nascimento");
    expect(inspector).toHaveTextContent("filosofia");
    expect(inspector).toHaveTextContent("Estoicismo → about → Sêneca");
    expect(within(inspector).queryByRole("textbox")).not.toBeInTheDocument();
    expect(requests.at(-1)).toEqual({
      url: `/api/shared/nodes/${OTHER_NODE_ID}`,
      authorization: `Bearer ${TOKEN}`,
      cookie: null,
      credentials: "omit",
    });
    expect(await axe(container)).toHaveNoViolations();

    await user.click(within(inspector).getByRole("button", { name: "Fechar" }));
    expect(
      screen.queryByRole("region", { name: "Inspector" }),
    ).not.toBeInTheDocument();
  });

  it("falls back to the list when the drawing cannot start", async () => {
    visitorApi();
    fake.recorded.failMount = true;

    openLink(`#${TOKEN}`);

    expect(
      await screen.findByText(
        "Não foi possível desenhar o grafo; mostrando a lista.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sêneca" })).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Desenho" })).toBeDisabled();
  });

  it("shows the one neutral state, without asking the API, when the link has no token", async () => {
    const requests = visitorApi();
    const { container } = openLink("");

    await expectUnavailable();
    expect(requests).toEqual([]);
    expect(await axe(container)).toHaveNoViolations();
  });

  it.each([
    ["not found", () => HttpResponse.json({ status: 404 }, { status: 404 })],
    [
      "too many requests",
      () => HttpResponse.json({ status: 429 }, { status: 429 }),
    ],
    [
      "a server error",
      () => HttpResponse.json({ status: 500 }, { status: 500 }),
    ],
    ["a network failure", () => HttpResponse.error()],
    [
      "a share with no live member",
      () => HttpResponse.json({ ...aVisitorGraph(), nodes: [], relations: [] }),
    ],
  ])("shows the same neutral state for %s", async (_case, answer) => {
    visitorApi({ graph: answer });

    openLink(`#${TOKEN}`);

    await expectUnavailable();
  });

  it("shows the neutral state in the Inspector when a member is no longer available", async () => {
    visitorApi({
      node: () => HttpResponse.json({ status: 404 }, { status: 404 }),
    });
    const user = userEvent.setup();
    openLink(`#${TOKEN}`);

    await user.click(await screen.findByRole("radio", { name: "Lista" }));
    await user.click(screen.getByRole("button", { name: "Sêneca" }));

    const inspector = await screen.findByRole("region", { name: "Inspector" });
    expect(await within(inspector).findByRole("alert")).toHaveTextContent(
      LINK_UNAVAILABLE_MESSAGE,
    );
  });
});

describe("the visitor's mapping of the API", () => {
  it("keeps only the fields the share allows and drops anything else the answer carries", () => {
    const root = {
      id: NODE_ID,
      title: "Estoicismo",
      typeName: null,
      distance: 0,
      degree: 7,
      hiddenFromAgents: true,
    };
    const relation = {
      id: RELATION_ID,
      sourceNodeId: NODE_ID,
      targetNodeId: OTHER_NODE_ID,
      kind: "about",
      origin: "soft",
    };
    const graph: Schemas["VisitorGraph"] = Object.assign(aVisitorGraph(), {
      ownerEmail: "ada@example.test",
      nodes: [root],
      relations: [relation],
    });
    const node: Schemas["VisitorNode"] = Object.assign(aVisitorNode(), {
      provenance: { channel: "mcp" },
      relations: [relation],
    });

    expect(toVisitorGraphView(graph)).toEqual({
      publicRef: "shr_abc",
      graph: {
        rootId: NODE_ID,
        nodes: [
          { id: NODE_ID, title: "Estoicismo", typeName: null, distance: 0 },
        ],
        edges: [
          {
            id: RELATION_ID,
            sourceNodeId: NODE_ID,
            targetNodeId: OTHER_NODE_ID,
            kind: "about",
          },
        ],
      },
    });
    expect(toVisitorNodeDetails(node)).toEqual({
      id: OTHER_NODE_ID,
      title: "Sêneca",
      body: "Cartas a Lucílio",
      typeName: "Pessoa",
      resourceUrl: "https://example.test/cartas",
      properties: [{ name: "Nascimento", value: "4" }],
      tags: ["filosofia"],
      relations: [
        {
          id: RELATION_ID,
          sourceNodeId: NODE_ID,
          kind: "about",
          targetNodeId: OTHER_NODE_ID,
        },
      ],
    });
  });

  it("keeps a Resource link only when it is http or https", () => {
    const linkOf = (resourceUrl: string | null) =>
      toVisitorNodeDetails({ ...aVisitorNode(), resourceUrl }).resourceUrl;

    expect(linkOf("https://example.test/a")).toBe("https://example.test/a");
    expect(linkOf("http://example.test/b")).toBe("http://example.test/b");
    expect(linkOf("javascript:alert(1)")).toBeNull();
    expect(linkOf("not a link")).toBeNull();
    expect(linkOf(null)).toBeNull();
  });

  it("writes Property values as plain text and places a member no shown Relation reaches after the farthest", () => {
    const values = [
      { name: "Lido", valueKind: "boolean", value: true },
      { name: "Temas", valueKind: "multi_select", value: ["ética", "lógica"] },
      { name: "Nota", valueKind: "text", value: "<b>negrito</b>" },
    ] satisfies Schemas["VisitorProperty"][];
    const graph = {
      ...aVisitorGraph(),
      nodes: [
        ...aVisitorGraph().nodes,
        {
          id: "01920000-0000-7000-8000-000000000003",
          title: "Solto",
          typeName: null,
          distance: null,
        },
      ],
    };

    expect(
      toVisitorNodeDetails({ ...aVisitorNode(), properties: values })
        .properties,
    ).toEqual([
      { name: "Lido", value: "Sim" },
      { name: "Temas", value: "ética, lógica" },
      { name: "Nota", value: "<b>negrito</b>" },
    ]);
    expect(
      toVisitorGraphView(graph).graph.nodes.map((member) => member.distance),
    ).toEqual([0, 1, 2]);
  });
});

describe("Denunciar esta página", () => {
  const ADDRESS = "abuse@lifegraph.test";
  let meta: HTMLMetaElement;
  beforeEach(() => {
    fake.reset();
    // As the Host serves visitor.html (DA-218).
    meta = document.createElement("meta");
    meta.name = "lifegraph-abuse-report";
    meta.content = ADDRESS;
    document.head.append(meta);
  });
  afterEach(() => meta.remove());

  function reportHref() {
    return (
      screen
        .getByRole("link", { name: REPORT_ABUSE_LABEL })
        .getAttribute("href") ?? ""
    );
  }

  it("names the shown link by its public reference and never by its token", async () => {
    visitorApi();
    const { container } = openLink(`#${TOKEN}`);

    expect(
      await screen.findByRole("heading", { name: "Estoicismo" }),
    ).toBeInTheDocument();
    const href = reportHref();
    expect(href.startsWith(`mailto:${ADDRESS}?subject=`)).toBe(true);
    expect(decodeURIComponent(href)).toContain("shr_abc");
    expect(href).not.toContain(TOKEN);
    expect(await axe(container)).toHaveNoViolations();
  });

  it("is there while loading, for a link that does not open and without a token", async () => {
    visitorApi({
      graph: async () => {
        await delay("infinite");
        return HttpResponse.json({});
      },
    });
    const loading = openLink(`#${TOKEN}`);
    expect(reportHref()).not.toContain(TOKEN);
    loading.unmount();

    visitorApi({ graph: () => new HttpResponse(null, { status: 404 }) });
    const unavailable = openLink(`#${TOKEN}`);
    await expectUnavailable();
    expect(reportHref()).not.toContain(TOKEN);
    unavailable.unmount();

    openLink("");
    await expectUnavailable();
    expect(reportHref().startsWith(`mailto:${ADDRESS}`)).toBe(true);
  });
});
