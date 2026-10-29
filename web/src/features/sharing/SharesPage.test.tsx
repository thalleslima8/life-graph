import { QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { delay, http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { createQueryClient } from "@/app/queryClient";
import { NODE_ID, OTHER_NODE_ID } from "@/test/graphFixtures";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";
import { SHARED_BADGE_LABEL, SharedBadge } from "./SharedBadge";
import { lastAccessText, SHARE_STATUS_LABELS } from "./sharingMessages";
import {
  sharedNodeIdsOptions,
  sharesKeys,
  type SharedGraphViewSummary,
} from "./sharesData";

const ACTIVE_ID = "01920000-0000-7000-8000-0000000000a1";
const DISABLED_ID = "01920000-0000-7000-8000-0000000000a2";

function aShare(
  overrides: Partial<SharedGraphViewSummary> = {},
): SharedGraphViewSummary {
  return {
    id: ACTIVE_ID,
    rootNodeId: NODE_ID,
    rootTitle: "Estoicismo",
    nodeCount: 12,
    createdAt: "2026-10-20T10:00:00Z",
    lastAccessedOn: "2026-10-28",
    accessDayCount: 4,
    status: "active",
    revokedAt: null,
    ...overrides,
  };
}

/** Serves the list and both revocations, and records what was revoked. */
function sharesApi(
  list: () => Response = () =>
    HttpResponse.json({ data: [aShare()], page: { nextCursor: null } }),
) {
  const state = { revoked: [] as string[], revokedAll: 0 };
  server.use(
    sessionHandler(() => ({ email: "ada@example.test" })),
    csrfTokenHandler().handler,
    http.get("/api/shared-graph-views", list),
    http.post("/api/shared-graph-views/:id/revocation", ({ params }) => {
      state.revoked.push(String(params.id));
      return new HttpResponse(null, { status: 204 });
    }),
    http.post("/api/shared-graph-views/revocation", () => {
      state.revokedAll += 1;
      return new HttpResponse(null, { status: 204 });
    }),
  );
  return state;
}

describe("the shared links page", () => {
  it("lists each link with its status in words and its last access as a UTC day", async () => {
    sharesApi(() =>
      HttpResponse.json({
        data: [
          aShare(),
          aShare({
            id: DISABLED_ID,
            rootTitle: "Saúde",
            status: "disabled_by_platform",
            lastAccessedOn: null,
          }),
          aShare({
            id: "01920000-0000-7000-8000-0000000000a3",
            rootNodeId: null,
            rootTitle: null,
            status: "revoked",
            revokedAt: "2026-10-27T09:00:00Z",
          }),
        ],
        page: { nextCursor: null },
      }),
    );
    const { container } = renderRoute("/shares");

    const active = await screen.findByRole("listitem", { name: "Estoicismo" });
    const disabled = screen.getByRole("listitem", { name: "Saúde" });
    const revoked = screen.getByRole("listitem", {
      name: "Node raiz indisponível",
    });

    expect(
      within(active).getByText(SHARE_STATUS_LABELS.active),
    ).toBeInTheDocument();
    expect(within(active).getByText("28/10/2026 (UTC)")).toBeInTheDocument();
    expect(
      within(disabled).getByText("Desativado pela plataforma"),
    ).toBeInTheDocument();
    expect(within(disabled).getByText("Nunca acessado")).toBeInTheDocument();
    expect(
      within(revoked).getByText(SHARE_STATUS_LABELS.revoked),
    ).toBeInTheDocument();
    expect(
      within(revoked).queryByRole("button", { name: /Revogar/ }),
    ).not.toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();
  });

  it("shows loading, then the empty state", async () => {
    sharesApi(() =>
      HttpResponse.json({ data: [], page: { nextCursor: null } }),
    );
    renderRoute("/shares");

    expect(await screen.findByText(/Nenhum link ainda/)).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Revogar todos" }),
    ).not.toBeInTheDocument();
  });

  it("shows the error with a retry", async () => {
    let failures = 1;
    sharesApi(() =>
      failures-- > 0
        ? problem(500, "internal")
        : HttpResponse.json({ data: [aShare()], page: { nextCursor: null } }),
    );
    const user = userEvent.setup();
    renderRoute("/shares");

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Não foi possível carregar os links.",
    );
    await user.click(screen.getByRole("button", { name: "Tentar de novo" }));
    expect(
      await screen.findByRole("listitem", { name: "Estoicismo" }),
    ).toBeInTheDocument();
  });

  it("revokes one link only after its confirmation and reads the badge's Nodes again", async () => {
    const api = sharesApi();
    const user = userEvent.setup();
    const { queryClient } = renderRoute("/shares");
    await queryClient.prefetchQuery(sharedNodeIdsOptions());
    const row = await screen.findByRole("listitem", { name: "Estoicismo" });

    await user.click(
      within(row).getByRole("button", { name: "Revogar: Estoicismo" }),
    );
    const dialog = await screen.findByRole("dialog", {
      name: "Revogar este link?",
    });
    expect(dialog).toHaveTextContent("Não dá para desfazer");
    expect(await axe(dialog)).toHaveNoViolations();
    expect(api.revoked).toEqual([]);
    await user.click(within(dialog).getByRole("button", { name: "Revogar" }));

    await waitFor(() => expect(api.revoked).toEqual([ACTIVE_ID]));
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
    expect(
      queryClient.getQueryState(sharesKeys.sharedNodeIds)?.isInvalidated,
    ).toBe(true);
  });

  it("revokes every link behind its own confirmation", async () => {
    const api = sharesApi(() =>
      HttpResponse.json({
        data: [aShare(), aShare({ id: DISABLED_ID, rootTitle: "Saúde" })],
        page: { nextCursor: null },
      }),
    );
    const user = userEvent.setup();
    renderRoute("/shares");

    await user.click(
      await screen.findByRole("button", { name: "Revogar todos" }),
    );
    const dialog = await screen.findByRole("dialog", {
      name: "Revogar todos os links?",
    });
    expect(dialog).toHaveTextContent(
      "Os 2 links que ainda não foram revogados deixam de abrir na hora",
    );
    expect(await axe(dialog)).toHaveNoViolations();
    await user.click(
      within(dialog).getByRole("button", { name: "Revogar todos" }),
    );

    await waitFor(() => expect(api.revokedAll).toBe(1));
    expect(api.revoked).toEqual([]);
    expect(await screen.findByRole("status")).toHaveTextContent(
      "Todos os links foram revogados.",
    );
  });

  it("keeps the dialog open with the error when revoking fails", async () => {
    sharesApi();
    server.use(
      http.post("/api/shared-graph-views/:id/revocation", () =>
        problem(500, "internal"),
      ),
    );
    const user = userEvent.setup();
    renderRoute("/shares");

    await user.click(
      await screen.findByRole("button", { name: "Revogar: Estoicismo" }),
    );
    const dialog = await screen.findByRole("dialog", {
      name: "Revogar este link?",
    });
    await user.click(within(dialog).getByRole("button", { name: "Revogar" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "Não foi possível revogar.",
    );
  });
});

describe("lastAccessText", () => {
  it("says the UTC day, or that nobody visited", () => {
    expect(lastAccessText("2026-01-02")).toBe("02/01/2026 (UTC)");
    expect(lastAccessText(null)).toBe("Nunca acessado");
  });
});

describe("the shared badge", () => {
  function renderBadge(nodeId: string) {
    return render(
      <QueryClientProvider client={createQueryClient()}>
        <SharedBadge nodeId={nodeId} />
      </QueryClientProvider>,
    );
  }

  it("marks a Node in a link, in words", async () => {
    server.use(
      http.get("/api/shared-graph-views/node-ids", () =>
        HttpResponse.json({ data: [NODE_ID], page: { nextCursor: null } }),
      ),
    );

    renderBadge(NODE_ID);

    expect(await screen.findByText(SHARED_BADGE_LABEL)).toBeInTheDocument();
  });

  it("renders nothing for a Node in no link, while loading and on error", async () => {
    let answer: () => Promise<Response> = async () =>
      HttpResponse.json({ data: [NODE_ID], page: { nextCursor: null } });
    server.use(http.get("/api/shared-graph-views/node-ids", () => answer()));
    renderBadge(OTHER_NODE_ID);
    await waitFor(() =>
      expect(screen.queryByText(SHARED_BADGE_LABEL)).not.toBeInTheDocument(),
    );

    answer = async () => {
      await delay("infinite");
      return HttpResponse.json({});
    };
    const loading = renderBadge(NODE_ID);
    expect(loading.container).toBeEmptyDOMElement();
    loading.unmount();

    answer = async () => problem(500, "internal");
    const failed = renderBadge(NODE_ID);
    await waitFor(() => expect(failed.container).toBeEmptyDOMElement());
  });
});
