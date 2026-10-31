import { infiniteQueryOptions, queryOptions, useQuery } from "@tanstack/react-query";
import { api, isApiError, unwrap } from "@/api/client";
import { graphKeys, retryUnlessNotFound } from "./graphData";

/**
 * The Raiz da Conta and the Unanchored View in the SPA (DA-197, DA-206, DA-208). The root's id
 * never changes, so it is read once, outside the graph's keys; the Unanchored list and its count
 * are graph reads, refreshed by every write of the SPA (useGraphWrite). Connecting a Node is the
 * same create-Relation write as anywhere else (DA-013).
 */

/** The page of the Nodes the root does not reach. */
export const UNANCHORED_PATH = "/unanchored";

/** The kind of the Relation "Conectar a…" creates, from the Node to the target (DA-193). */
export const CONNECT_KIND_DEFAULT = "related_to";

const UNANCHORED_PAGE_SIZE = 50;

/** The Account has no root yet: it is created at the next sign-in (DA-201, DA-202). */
export const isRootMissing = (error: unknown): boolean => isApiError(error, "graph.root_missing");

export const accountRootQuery = queryOptions({
  queryKey: ["account-root"] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET("/api/account-root", { signal })),
  staleTime: Infinity,
  // A missing root stays missing until the next sign-in: retrying only delays the answer.
  retry: (failureCount, error) => !isRootMissing(error) && retryUnlessNotFound(failureCount, error),
});

export function useAccountRoot() {
  const root = useQuery(accountRootQuery);
  return {
    rootId: root.data?.id,
    isLoading: root.isPending,
    isError: root.isError,
    isRootMissing: isRootMissing(root.error),
    refetch: root.refetch,
  };
}

const unanchoredKey = [...graphKeys.all, "unanchored"] as const;

export const unanchoredListOptions = () =>
  infiniteQueryOptions({
    queryKey: [...unanchoredKey, "list"] as const,
    queryFn: async ({ pageParam, signal }) =>
      unwrap(
        await api.GET("/api/nodes", {
          params: { query: { unanchored: true, cursor: pageParam, limit: UNANCHORED_PAGE_SIZE } },
          signal,
        }),
      ),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (page) => page.page.nextCursor ?? undefined,
  });

/** The Inbox counter: the total of a one-item page, no count endpoint of its own (DA-206). */
export const unanchoredCountOptions = () =>
  queryOptions({
    queryKey: [...unanchoredKey, "count"] as const,
    queryFn: async ({ signal }) => unwrap(await api.GET("/api/nodes", { params: { query: { unanchored: true, limit: 1 } }, signal })),
    select: (page) => page.page.totalCount ?? 0,
  });
