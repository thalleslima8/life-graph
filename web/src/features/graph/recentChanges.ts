import {
  infiniteQueryOptions,
  useInfiniteQuery,
  useMutation,
  useQuery,
  useQueryClient,
  type Query,
  type UseInfiniteQueryResult,
} from "@tanstack/react-query";
import { useEffect } from "react";
import { api, isApiError, unwrap, type ApiError } from "@/api/client";
import type { components } from "@/api/schema.gen";
import { changeSetsKey, graphKeys, type GraphWriteReceipt } from "./graphData";

/**
 * Recent Changes (DA-024, DA-118): the feed of GraphChangeSets with older pages by `before`,
 * a probe by `since` that only says something new exists, and Undo.
 */

export type ChangeSetItem = components["schemas"]["ChangeSetItem"];
export type ChangeSetFeed = components["schemas"]["ChangeSetFeed"];

/** While the tab is visible; the API's read budget is sized for it (DA-116). */
export const PROBE_INTERVAL_MS = 30_000;
const PROBE_PAGE_SIZE = 20;
const MILLISECONDS_PER_SECOND = 1_000;

const feedKey = [...changeSetsKey, "feed"] as const;

export const changeSetsQuery = infiniteQueryOptions({
  queryKey: feedKey,
  queryFn: async ({ pageParam, signal }): Promise<ChangeSetFeed> =>
    unwrap(await api.GET("/api/changesets", { params: { query: { before: pageParam } }, signal })),
  initialPageParam: undefined as string | undefined,
  getNextPageParam: (page) => page.page.nextCursor ?? undefined,
});

// After a 429 the probe waits what Retry-After asked, on the timer and on focus alike.
function cooldownLeftMs(query: { state: Pick<Query["state"], "error" | "errorUpdatedAt"> }): number {
  const { error, errorUpdatedAt } = query.state;
  if (!isApiError(error) || error.retryAfterSeconds === undefined) {
    return 0;
  }

  return Math.max(0, errorUpdatedAt + error.retryAfterSeconds * MILLISECONDS_PER_SECOND - Date.now());
}

export function useRecentChanges(): {
  feed: UseInfiniteQueryResult<ChangeSetItem[]>;
  hasNewer: boolean;
  showNewer(): void;
} {
  const queryClient = useQueryClient();
  const feed = useInfiniteQuery({ ...changeSetsQuery, select: (data) => data.pages.flatMap((page) => page.data) });
  // A second observer of the same cache: the newest cursor the person has seen.
  const latestCursor = useInfiniteQuery({ ...changeSetsQuery, select: (data) => data.pages[0]?.latestCursor ?? null });

  // Without a cursor (empty feed) the probe reads without `since`: anything it finds is new.
  const cursor = latestCursor.data ?? null;
  const probe = useQuery({
    queryKey: [...changeSetsKey, "probe", cursor] as const,
    queryFn: async ({ signal }): Promise<ChangeSetFeed> =>
      unwrap(await api.GET("/api/changesets", { params: { query: { since: cursor ?? undefined, limit: PROBE_PAGE_SIZE } }, signal })),
    enabled: latestCursor.isSuccess,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: (query) => cooldownLeftMs(query) === 0,
    refetchInterval: (query) => Math.max(PROBE_INTERVAL_MS, cooldownLeftMs(query)),
    refetchIntervalInBackground: false,
  });

  const hasNewer = (probe.data?.data.length ?? 0) > 0;
  const newestProbed = probe.data?.latestCursor ?? null;

  // New items stay out of the list (focus and scroll stay put) but the graph is read again.
  useEffect(() => {
    if (hasNewer) {
      void queryClient.invalidateQueries({ queryKey: graphKeys.all });
    }
  }, [hasNewer, newestProbed, queryClient]);

  const showNewer = () => {
    void queryClient.resetQueries({ queryKey: feedKey });
  };

  return { feed, hasNewer, showNewer };
}

export type UndoResult = {
  receipt: GraphWriteReceipt;
  /** Relations of a restored Node whose other end is gone (DA-115), named by the original ChangeSet. */
  relationsLeftDeleted: { id: string; label: string | null }[];
};

export function useUndoChangeSet() {
  const queryClient = useQueryClient();
  return useMutation<UndoResult, ApiError, ChangeSetItem>({
    mutationFn: async (changeSet) => {
      const receipt = unwrap(await api.POST("/api/changesets/{changeSetId}/undo", { params: { path: { changeSetId: changeSet.id } } }));
      const relationsLeftDeleted = (receipt.relationsLeftDeleted ?? []).map((id) => ({
        id,
        label: changeSet.entries.find((entry) => entry.entityKind === "relation" && entry.entityId === id)?.label ?? null,
      }));
      return { receipt, relationsLeftDeleted };
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: graphKeys.all });
      void queryClient.invalidateQueries({ queryKey: changeSetsKey });
    },
  });
}

export type UndoRefusal =
  | { kind: "conflict"; entrySequences: number[] }
  | { kind: "in_use"; entity: "type" | "property" }
  | { kind: "already_reverted" }
  | { kind: "window_closed" }
  | { kind: "content_purged" }
  | { kind: "not_found" };

const UNDO_REFUSALS: Readonly<Record<string, UndoRefusal>> = {
  "graph.type_in_use": { kind: "in_use", entity: "type" },
  "graph.type_in_use_by_deleted_nodes": { kind: "in_use", entity: "type" },
  "graph.property_in_use": { kind: "in_use", entity: "property" },
  "graph.property_in_use_by_deleted_nodes": { kind: "in_use", entity: "property" },
  "graph.changeset_already_reverted": { kind: "already_reverted" },
  "graph.undo_window_closed": { kind: "window_closed" },
  "graph.changeset_content_purged": { kind: "content_purged" },
  "graph.changeset_not_found": { kind: "not_found" },
};

const CONFLICTING_ENTRY = /^entries\[(\d+)\]$/;

/** Why an Undo was refused, or null for any other error. A conflict without details names no entry. */
export function toUndoRefusal(error: unknown): UndoRefusal | null {
  if (!isApiError(error) || error.code === undefined) {
    return null;
  }

  if (error.code === "graph.undo_conflict") {
    const entrySequences = Object.keys(error.details)
      .map((key) => CONFLICTING_ENTRY.exec(key)?.[1])
      .filter((sequence) => sequence !== undefined)
      .map(Number)
      .sort((left, right) => left - right);
    return { kind: "conflict", entrySequences };
  }

  return UNDO_REFUSALS[error.code] ?? null;
}

/** Reverted comes from the item itself; the other end of the chain only when it is loaded. */
export function undoChain(
  changeSet: ChangeSetItem,
  loaded: ChangeSetItem[],
): { isReverted: boolean; reverts?: ChangeSetItem; revertedBy?: ChangeSetItem } {
  const reverts = changeSet.revertsChangeSetId ? loaded.find((item) => item.id === changeSet.revertsChangeSetId) : undefined;
  const revertedBy = changeSet.revertedByChangeSetId ? loaded.find((item) => item.id === changeSet.revertedByChangeSetId) : undefined;
  return { isReverted: changeSet.revertedByChangeSetId !== null, reverts, revertedBy };
}
