import { infiniteQueryOptions, queryOptions, useMutation, useQueryClient, type UseMutationResult } from "@tanstack/react-query";
import { useCallback, useMemo } from "react";
import { useSearchParams } from "react-router";
import { z } from "zod";
import { api, isApiError, unwrap, type ApiError } from "@/api/client";
import type { components } from "@/api/schema.gen";
import { changeSetsKey, graphKeys, type GraphWriteReceipt } from "@/features/graph/graphData";
import type { ChangeSetItem } from "@/features/graph/recentChanges";

/**
 * Agent Changes (v2 §65) and the GraphChangeSet screen (v2 §66), DA-135. Same shape as the
 * graph's modules (DA-118): query options that take their filters as arguments (no Router
 * inside them), keys and one named mutation hook per action. Two lists, not one merged feed:
 * the agents' proposals, and the agents' GraphChangeSets; an applied proposal shows in the
 * second. Only the person decides; the server checks everything again.
 */

type Schemas = components["schemas"];
export type ProposalStatus = Schemas["ProposalStatus"];
export type ProposalItem = Schemas["ProposalItem"];
export type ProposalDetail = Schemas["ProposalDetail"];
export type ProposedOperation = Schemas["ProposedOperationItem"];
export type ChangeSetDetail = Schemas["ChangeSetDetail"];
export type ChangeEntryDetail = Schemas["ChangeEntryDetail"];
export type FieldChange = Schemas["FieldChange"];
export type AgentRef = Schemas["AgentRef"];
export type Receipt = GraphWriteReceipt;

export const PROPOSAL_STATUSES = ["proposed", "applied", "rejected", "expired", "withdrawn"] as const satisfies readonly ProposalStatus[];

export type FeedFilters = { agentIdentityId?: string; unreviewed: boolean; status?: ProposalStatus };

// Each parameter falls back on its own: one bad value never resets the others (FE-012).
const feedFiltersSchema = z.object({
  agentIdentityId: z.uuid().optional().catch(undefined),
  unreviewed: z
    .enum(["1"])
    .optional()
    .catch(undefined)
    .transform((value) => value === "1"),
  status: z.enum(PROPOSAL_STATUSES).optional().catch(undefined),
});

function readFilters(params: URLSearchParams): FeedFilters {
  const parsed = feedFiltersSchema.parse({
    agentIdentityId: params.get("agentIdentityId") ?? undefined,
    unreviewed: params.get("unreviewed") ?? undefined,
    status: params.get("status") ?? undefined,
  });
  return {
    unreviewed: parsed.unreviewed,
    ...(parsed.agentIdentityId === undefined ? {} : { agentIdentityId: parsed.agentIdentityId }),
    ...(parsed.status === undefined ? {} : { status: parsed.status }),
  };
}

/** The filters of Agent Changes, kept in the URL (FE-012); an invalid value reads as the default. */
export function useFeedFilters(): [FeedFilters, (next: Partial<FeedFilters>) => void] {
  const [params, setParams] = useSearchParams();
  const filters = useMemo(() => readFilters(params), [params]);
  const setFilters = useCallback(
    (next: Partial<FeedFilters>) => {
      setParams((current) => {
        const merged = { ...readFilters(current), ...next };
        const written = new URLSearchParams();
        if (merged.agentIdentityId) {
          written.set("agentIdentityId", merged.agentIdentityId);
        }

        if (merged.unreviewed) {
          written.set("unreviewed", "1");
        }

        if (merged.status) {
          written.set("status", merged.status);
        }

        return written;
      });
    },
    [setParams],
  );
  return [filters, setFilters];
}

// Under the ChangeSets' key: every graph write and Undo already invalidates it, and the diff of
// a pending proposal compares with the graph as it is now.
const agentChangesRoot = [...changeSetsKey, "agent-changes"] as const;

export const agentChangeKeys = {
  all: agentChangesRoot,
  proposals: (filters: FeedFilters) => [...agentChangesRoot, "proposals", filters] as const,
  proposal: (id: string) => [...agentChangesRoot, "proposal", id] as const,
  changeSets: (filters: FeedFilters) => [...agentChangesRoot, "changesets", filters] as const,
  changeSet: (id: string) => [...agentChangesRoot, "changeset", id] as const,
};

/** The agents' proposals, newest first; only the agent and status filters apply to them. */
export function proposalsQuery(filters: FeedFilters) {
  return infiniteQueryOptions({
    queryKey: agentChangeKeys.proposals(filters),
    queryFn: async ({ pageParam, signal }) =>
      unwrap(
        await api.GET("/api/proposals", {
          params: { query: { status: filters.status, agentIdentityId: filters.agentIdentityId, cursor: pageParam } },
          signal,
        }),
      ),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (page) => page.page.nextCursor ?? undefined,
    select: (data): ProposalItem[] => data.pages.flatMap((page) => page.data),
  });
}

/** The agents' GraphChangeSets, newest first, by agent and "não revisado". */
export function agentChangeSetsQuery(filters: FeedFilters) {
  return infiniteQueryOptions({
    queryKey: agentChangeKeys.changeSets(filters),
    queryFn: async ({ pageParam, signal }) =>
      unwrap(
        await api.GET("/api/changesets", {
          params: {
            query: {
              agentsOnly: true,
              agentIdentityId: filters.agentIdentityId,
              unreviewed: filters.unreviewed || undefined,
              before: pageParam,
            },
          },
          signal,
        }),
      ),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (page) => page.page.nextCursor ?? undefined,
    select: (data): ChangeSetItem[] => data.pages.flatMap((page) => page.data),
  });
}

export function proposalQuery(id: string) {
  return queryOptions({
    queryKey: agentChangeKeys.proposal(id),
    queryFn: async ({ signal }): Promise<ProposalDetail> =>
      unwrap(await api.GET("/api/proposals/{proposalId}", { params: { path: { proposalId: id } }, signal })),
  });
}

export function changeSetQuery(id: string) {
  return queryOptions({
    queryKey: agentChangeKeys.changeSet(id),
    queryFn: async ({ signal }): Promise<ChangeSetDetail> =>
      unwrap(await api.GET("/api/changesets/{changeSetId}", { params: { path: { changeSetId: id } }, signal })),
  });
}

/** Approves every operation (no argument) or only those listed; the rest are rejected. */
// eslint-disable-next-line @typescript-eslint/no-invalid-void-type -- TanStack's "no variables" is void: mutate() approves everything (DA-135)
export function useApproveProposal(id: string): UseMutationResult<Receipt, ApiError, { operations?: number[] } | void> {
  const queryClient = useQueryClient();
  // eslint-disable-next-line @typescript-eslint/no-invalid-void-type -- TanStack's "no variables" is void: mutate() approves everything (DA-135)
  return useMutation<Receipt, ApiError, { operations?: number[] } | void>({
    mutationFn: async (approval) =>
      unwrap(
        await api.POST("/api/proposals/{proposalId}/approval", {
          params: { path: { proposalId: id } },
          body: { operations: approval?.operations ?? null },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: graphKeys.all });
      void queryClient.invalidateQueries({ queryKey: changeSetsKey });
    },
    onError: (error) => {
      // Decided elsewhere, or a Node changed: read it again so the person sees it as it is now.
      if (toProposalRefusal(error) !== null) {
        void queryClient.invalidateQueries({ queryKey: agentChangeKeys.all });
      }
    },
  });
}

export function useRejectProposal(id: string): UseMutationResult<void, ApiError, void> {
  const queryClient = useQueryClient();
  // eslint-disable-next-line @typescript-eslint/no-invalid-void-type -- TanStack's "no data, no variables" is void
  return useMutation<void, ApiError, void>({
    mutationFn: async () => {
      unwrap(await api.POST("/api/proposals/{proposalId}/rejection", { params: { path: { proposalId: id } } }));
    },
    onSettled: () => void queryClient.invalidateQueries({ queryKey: agentChangeKeys.all }),
  });
}

/** Takes an agent's GraphChangeSet out of "não revisado". */
export function useMarkReviewed(changeSetId: string): UseMutationResult<void, ApiError, void> {
  const queryClient = useQueryClient();
  // eslint-disable-next-line @typescript-eslint/no-invalid-void-type -- TanStack's "no data, no variables" is void
  return useMutation<void, ApiError, void>({
    mutationFn: async () => {
      unwrap(await api.POST("/api/changesets/{changeSetId}/review", { params: { path: { changeSetId } } }));
    },
    onSettled: () => void queryClient.invalidateQueries({ queryKey: changeSetsKey }),
  });
}

export type ProposalRefusal =
  | { kind: "not_pending" }
  | { kind: "version_conflict"; operations: number[] }
  | { kind: "dependency"; operations: number[] }
  | { kind: "anchor_required"; operations: number[] }
  | { kind: "root_missing" }
  | { kind: "not_found" };

const OPERATION_DETAIL = /^operations\[(\d+)\]/;

function operationsNamedIn(error: ApiError): number[] {
  const indexes = Object.keys(error.details)
    .map((key) => OPERATION_DETAIL.exec(key)?.[1])
    .filter((index) => index !== undefined)
    .map(Number);
  return [...new Set(indexes)].sort((left, right) => left - right);
}

/** Why an approval was refused, or null for any other error; details `operations[n]` name the operations, like {@link toUndoRefusal}. */
export function toProposalRefusal(error: unknown): ProposalRefusal | null {
  if (!isApiError(error) || error.code === undefined) {
    return null;
  }

  switch (error.code) {
    case "agents.proposal_not_pending":
      return { kind: "not_pending" };
    case "agents.proposal_not_found":
      return { kind: "not_found" };
    case "graph.node_version_conflict":
      return { kind: "version_conflict", operations: operationsNamedIn(error) };
    case "agents.operation_dependency":
      return { kind: "dependency", operations: operationsNamedIn(error) };
    // The approval checks the anchors again (DA-199, DA-212): one may have come loose since the proposal.
    case "graph.anchor_required":
      return { kind: "anchor_required", operations: operationsNamedIn(error) };
    case "graph.root_missing":
      return { kind: "root_missing" };
    default:
      return null;
  }
}

/**
 * The body of an approval: `null` when there is nothing to send (no operation, or none
 * chosen), `undefined` when every operation is chosen, else the chosen indexes in order. An
 * empty list is never sent.
 */
export function toApproval(selected: ReadonlySet<number>, total: number): number[] | undefined | null {
  if (total === 0 || selected.size === 0) {
    return null;
  }

  if (selected.size === total) {
    return undefined;
  }

  return [...selected].sort((left, right) => left - right);
}
