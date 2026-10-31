import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { axe } from "vitest-axe";
import { aNode, NODE_ID, OTHER_NODE_ID } from "@/test/graphFixtures";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";
import {
  LEGAL_NOTICE_ACK_LABEL,
  LEAVE_WARNING,
  LIMIT_REACHED_MESSAGE,
  NOTHING_ELIGIBLE_MESSAGE,
  ROOT_NOT_SHAREABLE_MESSAGE,
  TAGS_WARNING,
} from "./NewSharePage";
import {
  sharedNodeIdsOptions,
  sharesKeys,
  type SharedGraphViewProposal,
} from "./sharesData";

const PROPERTY_ID = "01920000-0000-7000-8000-0000000000b9";
const SHARE_URL = "https://share.localhost/s#token-shown-once";

function aProposal(
  overrides: Partial<SharedGraphViewProposal> = {},
): SharedGraphViewProposal {
  return {
    rootNodeId: NODE_ID,
    maxDepth: 1,
    nodes: [
      { id: NODE_ID, title: "Estoicismo", typeName: null, distance: 0 },
      {
        id: OTHER_NODE_ID,
        title: "Meditações",
        typeName: "Livro",
        distance: 1,
      },
    ],
    eligibleCount: 2,
    truncated: false,
    properties: [{ id: PROPERTY_ID, name: "Tradutor" }],
    ...overrides,
  };
}

/** Serves the proposal and the creation, and records what was asked. */
function sharingApi({
  proposal = () => HttpResponse.json(aProposal()),
  create,
}: { proposal?: () => Response; create?: () => Response } = {}) {
  const state = { created: [] as unknown[], depths: [] as (string | null)[] };
  server.use(
    sessionHandler(() => ({ email: "ada@example.test" })),
    csrfTokenHandler().handler,
    http.get("/api/shared-graph-views/proposal", ({ request }) => {
      state.depths.push(new URL(request.url).searchParams.get("maxDepth"));
      return proposal();
    }),
    http.post("/api/shared-graph-views", async ({ request }) => {
      state.created.push(await request.json());
      return create
        ? create()
        : HttpResponse.json(
            { id: "01920000-0000-7000-8000-0000000000e9", shareUrl: SHARE_URL },
            { status: 201 },
          );
    }),
    http.get("/api/nodes/:nodeId", () =>
      HttpResponse.json(aNode({ title: "Estoicismo" })),
    ),
    http.get("/api/nodes/:nodeId/relations", () =>
      HttpResponse.json({ data: [], page: { nextCursor: null } }),
    ),
    http.get("/api/types", () =>
      HttpResponse.json({ data: [], page: { nextCursor: null } }),
    ),
    http.get("/api/property-definitions", () =>
      HttpResponse.json({ data: [], page: { nextCursor: null } }),
    ),
  );
  return state;
}

async function toNotice(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole("button", { name: "Continuar" }));
  return screen.findByRole("heading", { name: "2. Aviso" });
}

describe("creating a shared link", () => {
  it("reviews the proposal, requires the notice and sends only what the person kept", async () => {
    const api = sharingApi();
    const user = userEvent.setup();
    const { container } = renderRoute(`/shares/new?rootNodeId=${NODE_ID}`);

    const root = await screen.findByRole("checkbox", { name: /Estoicismo/ });
    expect(root).toBeChecked();
    expect(root).toBeDisabled();
    expect(screen.getByText(/2 de 2 elegíveis/)).toBeInTheDocument();
    expect(
      screen.getByRole("checkbox", { name: "Tradutor" }),
    ).not.toBeChecked();
    expect(
      screen.getByRole("checkbox", { name: "Mostrar as Tags" }),
    ).not.toBeChecked();
    expect(screen.queryByText(TAGS_WARNING)).not.toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();

    await user.click(screen.getByRole("checkbox", { name: /Meditações/ }));
    await user.click(screen.getByRole("checkbox", { name: "Tradutor" }));
    await user.click(screen.getByRole("checkbox", { name: "Mostrar as Tags" }));
    expect(screen.getByText(TAGS_WARNING)).toBeInTheDocument();

    expect(await toNotice(user)).toHaveFocus();
    await user.click(screen.getByRole("button", { name: "Criar link" }));
    expect(
      await screen.findByText("Confirme que leu o aviso para criar o link."),
    ).toBeInTheDocument();
    expect(api.created).toEqual([]);

    await user.click(
      screen.getByRole("checkbox", { name: LEGAL_NOTICE_ACK_LABEL }),
    );
    await user.click(screen.getByRole("button", { name: "Criar link" }));

    expect(
      await screen.findByRole("heading", { name: "3. Seu link" }),
    ).toHaveFocus();
    expect(api.created).toEqual([
      {
        rootNodeId: NODE_ID,
        maxDepth: 1,
        nodeIds: [NODE_ID],
        propertyIds: [PROPERTY_ID],
        includeTags: true,
        legalNoticeAccepted: true,
      },
    ]);
  });

  it("shows the link once with a copy button and forgets it when the person leaves", async () => {
    sharingApi();
    const user = userEvent.setup();
    // After setup, which installs its own clipboard.
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", {
      value: { writeText },
      configurable: true,
    });
    const { container, router, queryClient } = renderRoute(
      `/shares/new?rootNodeId=${NODE_ID}`,
    );
    await queryClient.prefetchQuery(sharedNodeIdsOptions());

    await toNotice(user);
    await user.click(
      screen.getByRole("checkbox", { name: LEGAL_NOTICE_ACK_LABEL }),
    );
    await user.click(screen.getByRole("button", { name: "Criar link" }));

    expect(
      await screen.findByRole("textbox", { name: "Link compartilhado" }),
    ).toHaveValue(SHARE_URL);
    expect(screen.getByText(LEAVE_WARNING)).toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();
    await user.click(screen.getByRole("button", { name: "Copiar link" }));
    expect(writeText).toHaveBeenCalledWith(SHARE_URL);
    expect(await screen.findByText("Link copiado.")).toBeInTheDocument();
    // The new link's Nodes now carry the badge (DA-222).
    expect(
      queryClient.getQueryState(sharesKeys.sharedNodeIds)?.isInvalidated,
    ).toBe(true);
    // Only the mutation holds the link, never the query cache (DA-079).
    expect(
      JSON.stringify(
        queryClient
          .getQueryCache()
          .getAll()
          .map((query) => query.state.data),
      ),
    ).not.toContain(SHARE_URL);

    await user.click(screen.getByRole("button", { name: "Já copiei" }));

    await waitFor(() =>
      expect(router.state.location.pathname).toBe(`/nodes/${NODE_ID}`),
    );
    await waitFor(() =>
      expect(
        JSON.stringify(
          queryClient
            .getMutationCache()
            .getAll()
            .map((mutation) => mutation.state.data),
        ),
      ).not.toContain(SHARE_URL),
    );
    expect(JSON.stringify(router.state)).not.toContain(SHARE_URL);
  });

  it("reads the proposal again at the depth the person picks", async () => {
    const api = sharingApi();
    const user = userEvent.setup();
    const { router } = renderRoute(`/shares/new?rootNodeId=${NODE_ID}`);

    await user.click(
      await screen.findByRole("radio", {
        name: "Até 2 Relations de distância",
      }),
    );

    await waitFor(() => expect(api.depths).toEqual(["1", "2"]));
    expect(router.state.location.search).toContain("depth=2");
    expect(
      await screen.findByRole("radio", {
        name: "Até 2 Relations de distância",
      }),
    ).toBeChecked();
  });

  it("says how many eligible Nodes the 25 left out", async () => {
    sharingApi({
      proposal: () =>
        HttpResponse.json(aProposal({ eligibleCount: 31, truncated: true })),
    });
    renderRoute(`/shares/new?rootNodeId=${NODE_ID}`);

    expect(
      await screen.findByText(
        /2 de 31 elegíveis: um link tem no máximo 25 Nodes/,
      ),
    ).toBeInTheDocument();
  });

  it("blocks the creation when nothing is eligible", async () => {
    sharingApi({
      proposal: () =>
        HttpResponse.json(
          aProposal({ nodes: [], eligibleCount: 0, properties: [] }),
        ),
    });
    renderRoute(`/shares/new?rootNodeId=${NODE_ID}`);

    expect(await screen.findByRole("alert")).toHaveTextContent(
      NOTHING_ELIGIBLE_MESSAGE,
    );
    expect(
      screen.queryByRole("button", { name: "Continuar" }),
    ).not.toBeInTheDocument();
  });

  it("explains that the Raiz da Conta is never shared", async () => {
    sharingApi({ proposal: () => problem(422, "sharing.root_not_shareable") });
    renderRoute(`/shares/new?rootNodeId=${NODE_ID}`);

    expect(await screen.findByRole("alert")).toHaveTextContent(
      ROOT_NOT_SHAREABLE_MESSAGE,
    );
    expect(
      screen.queryByRole("button", { name: "Continuar" }),
    ).not.toBeInTheDocument();
  });

  it("says the Node was not found on a 404", async () => {
    sharingApi({ proposal: () => problem(404, "graph.node_not_found") });
    renderRoute(`/shares/new?rootNodeId=${NODE_ID}`);

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Este Node não foi encontrado.",
    );
  });

  it("offers to try again when the proposal fails to load", async () => {
    let failing = true;
    sharingApi({
      proposal: () =>
        failing ? problem(500, "unexpected") : HttpResponse.json(aProposal()),
    });
    const user = userEvent.setup();
    renderRoute(`/shares/new?rootNodeId=${NODE_ID}`);

    const retry = await screen.findByRole("button", {
      name: "Tentar novamente",
    });
    failing = false;
    await user.click(retry);

    expect(
      await screen.findByRole("button", { name: "Continuar" }),
    ).toBeInTheDocument();
  });

  it("explains the three-link limit and keeps the creation blocked", async () => {
    sharingApi({ create: () => problem(422, "sharing.limit_reached") });
    const user = userEvent.setup();
    renderRoute(`/shares/new?rootNodeId=${NODE_ID}`);

    await toNotice(user);
    await user.click(
      screen.getByRole("checkbox", { name: LEGAL_NOTICE_ACK_LABEL }),
    );
    await user.click(screen.getByRole("button", { name: "Criar link" }));

    expect(await screen.findByText(LIMIT_REACHED_MESSAGE)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Criar link" })).toBeDisabled();
  });

  it("asks for a root when none is given", async () => {
    sharingApi();
    renderRoute("/shares/new");

    expect(
      await screen.findByText(
        "Escolha no Inspector o Node que será a raiz do link.",
      ),
    ).toBeInTheDocument();
  });
});
