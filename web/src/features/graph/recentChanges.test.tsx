import { focusManager, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, renderHook, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/api/client";
import { aChangeSet, aFeed, CHANGESET_ID, NODE_ID } from "@/test/graphFixtures";
import { csrfTokenHandler, problem } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { graphKeys } from "./graphData";
import { PROBE_INTERVAL_MS, toUndoRefusal, undoChain, useRecentChanges, useUndoChangeSet, type ChangeSetFeed } from "./recentChanges";

function setup() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
  return { queryClient, wrapper };
}

/** The feed answers `history`; the probe (it sends `limit`) answers `probe` and records its `since`. */
function feedApi(history: ChangeSetFeed, probe: () => Response) {
  const probes: (string | null)[] = [];
  server.use(
    http.get("/api/changesets", ({ request }) => {
      const query = new URL(request.url).searchParams;
      if (!query.has("limit")) {
        return HttpResponse.json(history);
      }

      probes.push(query.get("since"));
      return probe();
    }),
  );
  return probes;
}

afterEach(() => focusManager.setFocused(undefined));

describe("useRecentChanges", () => {
  it("probes without since when the feed is empty, and anything found is newer", async () => {
    const probes = feedApi(aFeed([], null), () => HttpResponse.json(aFeed([aChangeSet()])));
    const { queryClient, wrapper } = setup();
    queryClient.setQueryData(graphKeys.node(NODE_ID), {});
    const { result } = renderHook(() => useRecentChanges(), { wrapper });

    await waitFor(() => expect(result.current.hasNewer).toBe(true));

    expect(probes).toEqual([null]);
    expect(result.current.feed.data).toEqual([]);
    // New items stay out of the list but the graph is read again.
    expect(queryClient.getQueryState(graphKeys.node(NODE_ID))?.isInvalidated).toBe(true);
  });

  it("probes after the newest cursor and shows the newer items on request", async () => {
    let history = aFeed([aChangeSet()], "cursor-1");
    let newer = aFeed([aChangeSet({ id: "01920000-0000-7000-8000-0000000000c2" })], "cursor-2");
    const probes: (string | null)[] = [];
    server.use(
      http.get("/api/changesets", ({ request }) => {
        const query = new URL(request.url).searchParams;
        if (!query.has("limit")) {
          return HttpResponse.json(history);
        }

        probes.push(query.get("since"));
        return HttpResponse.json(query.get("since") === "cursor-1" ? newer : aFeed([], query.get("since")));
      }),
    );
    const { wrapper } = setup();
    const { result } = renderHook(() => useRecentChanges(), { wrapper });
    await waitFor(() => expect(result.current.hasNewer).toBe(true));
    expect(probes).toEqual(["cursor-1"]);

    history = aFeed([...newer.data, ...history.data], "cursor-2");
    newer = aFeed([], "cursor-2");
    act(() => result.current.showNewer());

    await waitFor(() => expect(result.current.feed.data).toHaveLength(2));
    await waitFor(() => expect(result.current.hasNewer).toBe(false));
    expect(probes.at(-1)).toBe("cursor-2");
  });

  it("probes again when the window regains focus", async () => {
    const probes = feedApi(aFeed([aChangeSet()], "cursor-1"), () => HttpResponse.json(aFeed([], "cursor-1")));
    const { wrapper } = setup();
    renderHook(() => useRecentChanges(), { wrapper });
    await waitFor(() => expect(probes).toHaveLength(1));

    act(() => {
      focusManager.setFocused(false);
      focusManager.setFocused(true);
    });

    await waitFor(() => expect(probes).toHaveLength(2));
  });

  describe("on a timer", () => {
    // Date is faked too: the Retry-After cooldown is measured with Date.now().
    beforeEach(() => {
      vi.useFakeTimers({ toFake: ["Date", "setTimeout", "clearTimeout", "setInterval", "clearInterval"] });
    });

    afterEach(() => {
      vi.useRealTimers();
    });

    it("probes again every 30 seconds while the tab is visible", async () => {
      const probes = feedApi(aFeed([aChangeSet()], "cursor-1"), () => HttpResponse.json(aFeed([], "cursor-1")));
      const { wrapper } = setup();
      renderHook(() => useRecentChanges(), { wrapper });
      await vi.waitFor(() => expect(probes).toHaveLength(1));

      await act(() => vi.advanceTimersByTimeAsync(PROBE_INTERVAL_MS - 1_000));
      expect(probes).toHaveLength(1);

      await act(() => vi.advanceTimersByTimeAsync(2_000));
      await vi.waitFor(() => expect(probes).toHaveLength(2));
    });

    const RETRY_AFTER_SECONDS = 60;

    function throttledProbe() {
      const probes = feedApi(aFeed([aChangeSet()], "cursor-1"), () =>
        problem(429, "too_many_requests", {}, { "Retry-After": String(RETRY_AFTER_SECONDS) }),
      );
      const { wrapper } = setup();
      renderHook(() => useRecentChanges(), { wrapper });
      return probes;
    }

    const refocus = () =>
      act(() => {
        focusManager.setFocused(false);
        focusManager.setFocused(true);
      });

    it("probes on focus only once the Retry-After delay has passed", async () => {
      const probes = throttledProbe();
      await vi.waitFor(() => expect(probes).toHaveLength(1));

      await act(() => vi.advanceTimersByTimeAsync((RETRY_AFTER_SECONDS - 5) * 1_000));
      refocus();
      await act(() => vi.advanceTimersByTimeAsync(0));
      expect(probes).toHaveLength(1);

      await act(() => vi.advanceTimersByTimeAsync(10_000));
      refocus();
      await vi.waitFor(() => expect(probes).toHaveLength(2));
    });

    it("stretches the timer past the 30-second interval to the Retry-After delay", async () => {
      const probes = throttledProbe();
      await vi.waitFor(() => expect(probes).toHaveLength(1));

      await act(() => vi.advanceTimersByTimeAsync((RETRY_AFTER_SECONDS - 2) * 1_000));
      expect(probes).toHaveLength(1);

      await act(() => vi.advanceTimersByTimeAsync(4_000));
      await vi.waitFor(() => expect(probes).toHaveLength(2));
    });
  });
});

describe("useUndoChangeSet", () => {
  it("names the Relations that did not come back by the labels of the original ChangeSet", async () => {
    const kept = "01920000-0000-7000-8000-0000000000d1";
    const purged = "01920000-0000-7000-8000-0000000000d2";
    server.use(
      csrfTokenHandler().handler,
      http.post("/api/changesets/:id/undo", () =>
        HttpResponse.json({ changeSetId: "01920000-0000-7000-8000-0000000000c9", changes: [], relationsLeftDeleted: [kept, purged] }),
      ),
    );
    const deletion = aChangeSet({
      entries: [
        { sequence: 0, entityKind: "node", entityId: NODE_ID, operation: "deleted", label: "Leituras", isPurged: false, cascadeOf: null },
        { sequence: 1, entityKind: "relation", entityId: kept, operation: "deleted", label: "cita", isPurged: false, cascadeOf: 0 },
        { sequence: 2, entityKind: "relation", entityId: purged, operation: "deleted", label: null, isPurged: true, cascadeOf: 0 },
      ],
    });
    const { wrapper } = setup();
    const { result } = renderHook(() => useUndoChangeSet(), { wrapper });

    act(() => result.current.mutate(deletion));

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.relationsLeftDeleted).toEqual([
      { id: kept, label: "cita" },
      { id: purged, label: null },
    ]);
  });
});

describe("toUndoRefusal", () => {
  const refusal = (status: number, code: string, details: Record<string, string[]> = {}) =>
    toUndoRefusal(new ApiError(status, code, code, { details }));

  it("names the conflicting entries by sequence", () => {
    expect(refusal(409, "graph.undo_conflict", { "entries[2]": ["x"], "entries[0]": ["y"] })).toEqual({ kind: "conflict", entrySequences: [0, 2] });
  });

  it("reads a conflict without details as one with no entries", () => {
    expect(refusal(409, "graph.undo_conflict")).toEqual({ kind: "conflict", entrySequences: [] });
  });

  it.each([
    ["graph.type_in_use", { kind: "in_use", entity: "type" }],
    ["graph.type_in_use_by_deleted_nodes", { kind: "in_use", entity: "type" }],
    ["graph.property_in_use", { kind: "in_use", entity: "property" }],
    ["graph.property_in_use_by_deleted_nodes", { kind: "in_use", entity: "property" }],
    ["graph.changeset_already_reverted", { kind: "already_reverted" }],
    ["graph.undo_window_closed", { kind: "window_closed" }],
    ["graph.changeset_content_purged", { kind: "content_purged" }],
    ["graph.changeset_not_found", { kind: "not_found" }],
  ])("maps %s", (code, expected) => {
    expect(refusal(422, code)).toEqual(expected);
  });

  it("is null for any other error", () => {
    expect(refusal(500, "unexpected_error")).toBeNull();
  });
});

describe("undoChain", () => {
  const undoId = "01920000-0000-7000-8000-0000000000c2";

  it("takes Reverted from the item and links the other end only when it is loaded", () => {
    const original = aChangeSet({ status: "reverted", revertedByChangeSetId: undoId });
    const undo = aChangeSet({ id: undoId, revertsChangeSetId: CHANGESET_ID });

    expect(undoChain(original, [original])).toEqual({ isReverted: true, reverts: undefined, revertedBy: undefined });
    expect(undoChain(original, [undo, original])).toEqual({ isReverted: true, reverts: undefined, revertedBy: undo });
    expect(undoChain(undo, [undo, original])).toEqual({ isReverted: false, reverts: original, revertedBy: undefined });
  });
});
