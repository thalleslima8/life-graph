import { QueryClientProvider } from "@tanstack/react-query";
import { render, renderHook, screen, waitFor } from "@testing-library/react";
import { delay, http, HttpResponse } from "msw";
import type { ReactNode } from "react";
import { describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { createQueryClient } from "@/app/queryClient";
import { NODE_ID, OTHER_NODE_ID } from "@/test/graphFixtures";
import { problem } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import {
  SHARED_BADGE_LABEL,
  SHARED_BADGE_UNKNOWN_LABEL,
  SharedBadge,
} from "./SharedBadge";
import {
  SHARED_NODE_IDS_CHUNK_SIZE,
  sharedBadgeStateOf,
  sharedNodeIdsChunks,
  useSharedNodeIds,
} from "./sharesData";

function wrapper({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={createQueryClient()}>
      {children}
    </QueryClientProvider>
  );
}

function BadgeOf({ nodeId }: { nodeId: string }) {
  const shared = useSharedNodeIds([nodeId]);
  return <SharedBadge state={sharedBadgeStateOf(nodeId, shared)} />;
}

function answering(sharedIds: string[]) {
  const asked: string[][] = [];
  server.use(
    http.get("/api/shared-graph-views/node-ids", ({ request }) => {
      const ids = new URL(request.url).searchParams.getAll("ids");
      asked.push(ids);
      return HttpResponse.json({
        data: ids.filter((id) => sharedIds.includes(id)),
      });
    }),
  );
  return asked;
}

const idsOf = (count: number) =>
  Array.from(
    { length: count },
    (_, index) =>
      `01a10492-43b8-7176-9da5-${index.toString().padStart(12, "0")}`,
  );

describe("the shared badge", () => {
  it("says shared or unknown in words and shows nothing for a Node in no link", async () => {
    const shared = render(<SharedBadge state="shared" />);
    expect(screen.getByText(SHARED_BADGE_LABEL)).toBeInTheDocument();
    expect(await axe(shared.container)).toHaveNoViolations();
    shared.unmount();

    const unknown = render(<SharedBadge state="unknown" />);
    expect(screen.getByText(SHARED_BADGE_UNKNOWN_LABEL)).toBeInTheDocument();
    expect(await axe(unknown.container)).toHaveNoViolations();
    unknown.unmount();

    const notShared = render(<SharedBadge state="not-shared" />);
    expect(notShared.container).toBeEmptyDOMElement();
  });

  it("marks a Node in a link and nothing for one the answer leaves out", async () => {
    answering([NODE_ID]);

    render(<BadgeOf nodeId={NODE_ID} />, { wrapper });
    const other = render(<BadgeOf nodeId={OTHER_NODE_ID} />, { wrapper });

    expect(await screen.findByText(SHARED_BADGE_LABEL)).toBeInTheDocument();
    await waitFor(() => expect(other.container).toBeEmptyDOMElement());
  });

  it("is unknown, never not shared, while loading and on error", async () => {
    server.use(
      http.get("/api/shared-graph-views/node-ids", async () => {
        await delay("infinite");
        return HttpResponse.json({ data: [] });
      }),
    );
    const loading = render(<BadgeOf nodeId={NODE_ID} />, { wrapper });
    expect(screen.getByText(SHARED_BADGE_UNKNOWN_LABEL)).toBeInTheDocument();
    loading.unmount();

    server.use(
      http.get("/api/shared-graph-views/node-ids", () =>
        problem(500, "internal"),
      ),
    );
    render(<BadgeOf nodeId={NODE_ID} />, { wrapper });
    await waitFor(() =>
      expect(
        screen.getByText(SHARED_BADGE_UNKNOWN_LABEL),
      ).toBeInTheDocument(),
    );
  });
});

describe("the shared Nodes of the screen", () => {
  it("asks in blocks of 75 distinct ids, never more", async () => {
    const visible = idsOf(160);
    const asked = answering([visible[0] ?? "", visible[159] ?? ""]);

    const { result } = renderHook(
      () => useSharedNodeIds([...visible, visible[0] ?? ""]),
      { wrapper },
    );

    await waitFor(() => expect(result.current.isPending).toBe(false));
    expect(asked.map((ids) => ids.length).sort()).toEqual([10, 75, 75]);
    expect([...result.current.ids].sort()).toEqual([visible[0], visible[159]]);
    expect(sharedNodeIdsChunks(visible).every((chunk) => chunk.length <= SHARED_NODE_IDS_CHUNK_SIZE)).toBe(true);
  });

  it("makes no request when no Node is visible", async () => {
    const asked = answering([]);

    const { result } = renderHook(() => useSharedNodeIds([]), { wrapper });

    expect(result.current.isPending).toBe(false);
    expect(result.current.ids.size).toBe(0);
    await delay(50);
    expect(asked).toEqual([]);
  });

  it("keeps the same set across renders while the answer does not change", async () => {
    answering([NODE_ID]);

    const { result, rerender } = renderHook(
      ({ ids }: { ids: string[] }) => useSharedNodeIds(ids),
      { wrapper, initialProps: { ids: [NODE_ID, OTHER_NODE_ID] } },
    );
    await waitFor(() => expect(result.current.ids.has(NODE_ID)).toBe(true));
    const first = result.current.ids;

    rerender({ ids: [OTHER_NODE_ID, NODE_ID] });

    expect(result.current.ids).toBe(first);
  });
});
