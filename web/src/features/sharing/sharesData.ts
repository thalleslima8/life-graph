import {
  infiniteQueryOptions,
  keepPreviousData,
  queryOptions,
  useMutation,
  useQueries,
  useQueryClient,
} from "@tanstack/react-query";
import { useEffect, useMemo } from "react";
import { z } from "zod";
import { api, isApiError, unwrap } from "@/api/client";
import type { components } from "@/api/schema.gen";
import { useInvalidatePlan } from "@/features/account/planData";
import {
  retryUnlessNotFound,
  SHARED_NODE_IDS_KEY,
} from "@/features/graph/graphData";

/**
 * SharedGraphViews, the owner's side (DA-075, DA-078, DA-222): the Nodes proposed from a root,
 * the creation, the list of links, revoking one or all, and the Nodes behind the "compartilhado"
 * badge. A share is not a graph write, so it does not go through the pipeline (DA-214). The
 * link the creation answers is shown once and lives only in the mutation's result in memory:
 * never in the query cache, router state, storage or logs (DA-079).
 */

type Schemas = components["schemas"];
export type SharedGraphViewProposal = Schemas["SharedGraphViewProposal"];
export type ProposedSharedNode = Schemas["ProposedSharedNode"];
export type ShareableProperty = Schemas["ShareableProperty"];
export type SharedGraphViewSummary = Schemas["SharedGraphViewSummary"];
export type SharedGraphViewStatus = SharedGraphViewSummary["status"];

export const SHARE_DEPTHS = [1, 2] as const;
export type ShareDepth = (typeof SHARE_DEPTHS)[number];
export const DEFAULT_SHARE_DEPTH: ShareDepth = 1;

export const sharingErrorCodes = {
  rootNotShareable: "sharing.root_not_shareable",
  limitReached: "sharing.limit_reached",
  nodeNotEligible: "sharing.node_not_eligible",
  propertyNotEligible: "sharing.property_not_eligible",
  legalNoticeNotAccepted: "sharing.legal_notice_not_accepted",
} as const;

export const isRootNotShareable = (error: unknown) =>
  isApiError(error, sharingErrorCodes.rootNotShareable);
export const isShareLimitReached = (error: unknown) =>
  isApiError(error, sharingErrorCodes.limitReached);

export const sharesKeys = {
  all: ["shares"] as const,
  proposal: (rootNodeId: string, depth: ShareDepth) =>
    ["shares", "proposal", rootNodeId, depth] as const,
  list: ["shares", "list"] as const,
  sharedNodeIds: SHARED_NODE_IDS_KEY,
  sharedNodeIdsAmong: (nodeIds: readonly string[]) =>
    [...SHARED_NODE_IDS_KEY, nodeIds] as const,
};

/** The most Node ids one badge request asks about; the server refuses more (DA-299). */
export const SHARED_NODE_IDS_CHUNK_SIZE = 75;

// Members are fixed at creation (ADR 0012): a revoke in another tab converges by age and focus.
const SHARED_NODE_IDS_STALE_TIME_MS = 30_000;

/** The depth a `?depth=` asks for, or the default when it is absent or not one of 1 and 2. */
export function shareDepthOf(value: string | null): ShareDepth {
  const depth = Number(value);
  return (
    SHARE_DEPTHS.find((allowed) => allowed === depth) ?? DEFAULT_SHARE_DEPTH
  );
}

/** Where the creation flow starts for a Node (DA-222). */
export function newSharePath(rootNodeId: string): string {
  return `/shares/new?${new URLSearchParams({ rootNodeId }).toString()}`;
}

/**
 * The Nodes a share from this root may hold (at most the plan's `maxNodes`) and the properties the person may show.
 * Read once per root and depth: the list the person reviews does not change under them, and the
 * server checks every id again at creation (BE-021).
 */
export const shareProposalOptions = (rootNodeId: string, depth: ShareDepth) =>
  queryOptions({
    queryKey: sharesKeys.proposal(rootNodeId, depth),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET("/api/shared-graph-views/proposal", {
          params: { query: { rootNodeId, maxDepth: depth } },
          signal,
        }),
      ),
    retry: retryUnlessNotFound,
    refetchOnWindowFocus: false,
  });

const shareNodeIdsSchema = (maxNodes: number | undefined) => {
  const atLeastTheRoot = z
    .array(z.string())
    .min(1, "Mantenha ao menos o Node raiz.");
  return maxNodes === undefined
    ? atLeastTheRoot
    : atLeastTheRoot.max(maxNodes, `Use no máximo ${maxNodes} Nodes.`);
};

/**
 * The creation form, with the plan's Nodes per share as the proposal carries it (E13). Without
 * it the client sets no cap, never a default one: the server always checks (FE-020, DA-266).
 */
export const createShareSchema = (maxNodes: number | undefined) =>
  z.object({
    rootNodeId: z.string().min(1),
    maxDepth: z.union([z.literal(1), z.literal(2)]),
    nodeIds: shareNodeIdsSchema(maxNodes),
    propertyIds: z.array(z.string()),
    includeTags: z.boolean(),
    // Starts unchecked: the person acknowledges the notice explicitly, and the server checks again.
    legalNoticeAccepted: z
      .boolean()
      .refine(
        (accepted) => accepted,
        "Confirme que leu o aviso para criar o link.",
      ),
  });

export type CreateShareFormInput = z.input<ReturnType<typeof createShareSchema>>;
export type CreateShareInput = z.output<ReturnType<typeof createShareSchema>>;

/**
 * Creates the share. The result keeps only the link, and only while the flow is mounted: the
 * mutation is never cached (`gcTime: 0`) and is reset when the component leaves.
 */
export function useCreateShare() {
  const queryClient = useQueryClient();
  const invalidatePlan = useInvalidatePlan();
  const mutation = useMutation({
    mutationFn: async (
      input: CreateShareInput,
    ): Promise<{ shareUrl: string }> => {
      const created = unwrap(
        await api.POST("/api/shared-graph-views", { body: input }),
      );
      return { shareUrl: created.shareUrl };
    },
    gcTime: 0,
    // The new link joins the list and the badge; the proposal stays as the person reviewed it.
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: sharesKeys.list });
      void queryClient.invalidateQueries({
        queryKey: sharesKeys.sharedNodeIds,
      });
    },
    // Created or refused at the limit, the plan's count of links is read again (DA-266).
    onSettled: () => void invalidatePlan(),
  });
  const { reset } = mutation;
  useEffect(() => reset, [reset]);
  return mutation;
}

/** The person's links, newest first, revoked ones included (DA-078). */
export const sharesListOptions = () =>
  infiniteQueryOptions({
    queryKey: sharesKeys.list,
    queryFn: async ({ pageParam, signal }) =>
      unwrap(
        await api.GET("/api/shared-graph-views", {
          params: { query: { cursor: pageParam } },
          signal,
        }),
      ),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (page) => page.page.nextCursor ?? undefined,
    select: (data): SharedGraphViewSummary[] =>
      data.pages.flatMap((page) => page.data),
  });

/** Which of these Nodes are in a link that still opens or may open again (one request, ≤ 75 ids). */
export const sharedNodeIdsOptions = (nodeIds: readonly string[]) =>
  queryOptions({
    queryKey: sharesKeys.sharedNodeIdsAmong(nodeIds),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET("/api/shared-graph-views/node-ids", {
          params: { query: { ids: [...nodeIds] } },
          signal,
        }),
      ).data,
    staleTime: SHARED_NODE_IDS_STALE_TIME_MS,
    refetchOnWindowFocus: true,
    placeholderData: keepPreviousData,
  });

export type SharedNodeIds = {
  /** The shared Nodes among the asked ones; the previous set while a new one loads. */
  ids: ReadonlySet<string>;
  /** Some block has no answer of its own yet. */
  isPending: boolean;
  isError: boolean;
  refetch: () => void;
};

/** The asked ids, distinct and sorted, in blocks the server accepts. */
export function sharedNodeIdsChunks(nodeIds: readonly string[]): string[][] {
  const distinct = [...new Set(nodeIds)].sort();
  const chunks: string[][] = [];
  for (let start = 0; start < distinct.length; start += SHARED_NODE_IDS_CHUNK_SIZE) {
    chunks.push(distinct.slice(start, start + SHARED_NODE_IDS_CHUNK_SIZE));
  }

  return chunks;
}

/**
 * The "compartilhado" badge's source for the Nodes on screen (DA-265, DA-299): one query per
 * block of 75 ids, none when nothing is visible. `ids` keeps its identity while the answers do
 * not change.
 */
export function useSharedNodeIds(
  visibleNodeIds: readonly string[],
): SharedNodeIds {
  const askedKey = sharedNodeIdsChunks(visibleNodeIds)
    .map((chunk) => chunk.join(","))
    .join("|");
  const chunks = useMemo(
    () =>
      askedKey === ""
        ? []
        : askedKey.split("|").map((chunk) => chunk.split(",")),
    [askedKey],
  );
  const results = useQueries({
    queries: chunks.map((chunk) => sharedNodeIdsOptions(chunk)),
  });
  const answeredKey = results
    .flatMap((result) => result.data ?? [])
    .sort()
    .join(",");
  const ids = useMemo(
    () => new Set(answeredKey === "" ? [] : answeredKey.split(",")),
    [answeredKey],
  );

  return {
    ids,
    isPending: results.some(
      (result) => result.isPending || result.isPlaceholderData,
    ),
    isError: results.some((result) => result.isError),
    refetch: () => {
      for (const result of results) {
        void result.refetch();
      }
    },
  };
}

export type SharedBadgeState = "shared" | "not-shared" | "unknown";

/** A Node the answer has not covered yet, or failed to, is "unknown", never "not-shared" (DA-299). */
export function sharedBadgeStateOf(
  nodeId: string,
  shared: SharedNodeIds,
): SharedBadgeState {
  if (shared.ids.has(nodeId)) {
    return "shared";
  }

  return shared.isPending || shared.isError ? "unknown" : "not-shared";
}

/** Revokes one link, at once and for good (DA-214). */
export function useRevokeShare() {
  const queryClient = useQueryClient();
  const invalidatePlan = useInvalidatePlan();
  return useMutation({
    mutationFn: async (sharedGraphViewId: string): Promise<void> => {
      unwrap(
        await api.POST(
          "/api/shared-graph-views/{sharedGraphViewId}/revocation",
          { params: { path: { sharedGraphViewId } } },
        ),
      );
    },
    // A revoked link frees its place in the plan (DA-220) and leaves the badge.
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: sharesKeys.all });
      void invalidatePlan();
    },
  });
}

/** Revokes every link of the Account that is not revoked yet. */
export function useRevokeAllShares() {
  const queryClient = useQueryClient();
  const invalidatePlan = useInvalidatePlan();
  return useMutation({
    mutationFn: async (): Promise<void> => {
      unwrap(await api.POST("/api/shared-graph-views/revocation"));
    },
    // A revoked link frees its place in the plan (DA-220) and leaves the badge.
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: sharesKeys.all });
      void invalidatePlan();
    },
  });
}
