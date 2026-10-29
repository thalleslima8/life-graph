import {
  infiniteQueryOptions,
  queryOptions,
  useMutation,
  useQueryClient,
} from "@tanstack/react-query";
import { useEffect } from "react";
import { z } from "zod";
import { api, isApiError, unwrap } from "@/api/client";
import type { components } from "@/api/schema.gen";
import { retryUnlessNotFound } from "@/features/graph/graphData";

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

// Mirror SharingLimits (DA-081, DA-220); the server always checks again (FE-020).
export const MAX_NODES_PER_SHARE = 25;
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
  sharedNodeIds: ["shares", "shared-node-ids"] as const,
};

// 3 links × 25 Nodes (DA-081): one page of the server's largest size holds every shared Node.
const SHARED_NODE_IDS_PAGE_SIZE = 75;

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
 * The Nodes a share from this root may hold (at most 25) and the properties the person may show.
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

export const createShareSchema = z.object({
  rootNodeId: z.string().min(1),
  maxDepth: z.union([z.literal(1), z.literal(2)]),
  nodeIds: z
    .array(z.string())
    .min(1, "Mantenha ao menos o Node raiz.")
    .max(MAX_NODES_PER_SHARE, `Use no máximo ${MAX_NODES_PER_SHARE} Nodes.`),
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

export type CreateShareFormInput = z.input<typeof createShareSchema>;
export type CreateShareInput = z.output<typeof createShareSchema>;

/**
 * Creates the share. The result keeps only the link, and only while the flow is mounted: the
 * mutation is never cached (`gcTime: 0`) and is reset when the component leaves.
 */
export function useCreateShare() {
  const queryClient = useQueryClient();
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

/** The Nodes in a link that still opens or may open again: the source of the "compartilhado" badge. */
export const sharedNodeIdsOptions = () =>
  queryOptions({
    queryKey: sharesKeys.sharedNodeIds,
    queryFn: async ({ signal }) =>
      new Set(
        unwrap(
          await api.GET("/api/shared-graph-views/node-ids", {
            params: { query: { limit: SHARED_NODE_IDS_PAGE_SIZE } },
            signal,
          }),
        ).data,
      ),
  });

/** Revokes one link, at once and for good (DA-214). */
export function useRevokeShare() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (sharedGraphViewId: string): Promise<void> => {
      unwrap(
        await api.POST(
          "/api/shared-graph-views/{sharedGraphViewId}/revocation",
          { params: { path: { sharedGraphViewId } } },
        ),
      );
    },
    onSettled: () =>
      void queryClient.invalidateQueries({ queryKey: sharesKeys.all }),
  });
}

/** Revokes every link of the Account that is not revoked yet. */
export function useRevokeAllShares() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (): Promise<void> => {
      unwrap(await api.POST("/api/shared-graph-views/revocation"));
    },
    onSettled: () =>
      void queryClient.invalidateQueries({ queryKey: sharesKeys.all }),
  });
}
