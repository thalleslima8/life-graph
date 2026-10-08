import { infiniteQueryOptions, useMutation, useQueryClient } from "@tanstack/react-query";
import { z } from "zod";
import { api, isApiError, unwrap, type ApiError } from "@/api/client";
import type { components } from "@/api/schema.gen";

/**
 * "Agentes conectados" (DA-030, DA-125): the person's agent connections, listed by cursor,
 * renamed and revoked. Same shape as the graph's modules (DA-118): query options, keys and
 * one named mutation hook per action. The client's declared name is display only.
 */

type Schemas = components["schemas"];
export type AgentIdentityStatus = Schemas["AgentIdentityStatus"];

export type AgentIdentityView = {
  id: string;
  name: string;
  /** Declared by the client; never verified (DA-030). */
  clientName: string;
  clientId: string;
  scopes: string[];
  status: AgentIdentityStatus;
  connectedAt: string;
  lastUsedAt: string | null;
};

// Mirrors AgentIdentity.NameMaxLength on the server, which always checks again (FE-020).
export const AGENT_NAME_MAX_LENGTH = 100;

export const agentNameSchema = z
  .string()
  .trim()
  .min(1, "Informe um nome.")
  .max(AGENT_NAME_MAX_LENGTH, `Use no máximo ${AGENT_NAME_MAX_LENGTH} caracteres.`);

/** The consent page's words for each scope (DA-122); a scope missing here is never shown. */
export const SCOPE_LABELS: Readonly<Record<string, string>> = {
  "lifegraph.read": "Ler o seu grafo: buscar e consultar Nodes, Relations e o contexto que você permitir.",
  "lifegraph.write": "Criar e alterar Nodes e Relations no seu grafo.",
};

export const agentIdentityKeys = {
  all: ["agent-identities"] as const,
  list: ["agent-identities", "list"] as const,
};

export const agentIdentitiesQuery = infiniteQueryOptions({
  queryKey: agentIdentityKeys.list,
  queryFn: async ({ pageParam, signal }) =>
    unwrap(await api.GET("/api/agent-identities", { params: { query: { cursor: pageParam } }, signal })),
  initialPageParam: undefined as string | undefined,
  getNextPageParam: (page) => page.page.nextCursor ?? undefined,
  select: (data): AgentIdentityView[] => data.pages.flatMap((page) => page.data.map(toView)),
});

/** The connection is gone: revoked elsewhere, or never this Account's. */
export const isAgentIdentityGone = (error: unknown): error is ApiError =>
  isApiError(error, "accounts.agent_identity_not_found") && error.status === 404;

// A refusal stays a refusal: retrying a 400, 404 or 422 only delays the answer.
const refusalStatuses = new Set([400, 404, 422]);

function retryUnlessRefused(failureCount: number, error: unknown): boolean {
  return !(isApiError(error) && refusalStatuses.has(error.status)) && failureCount < 1;
}

export function useRenameAgentIdentity(id: string) {
  const queryClient = useQueryClient();
  return useMutation<AgentIdentityView, ApiError, { name: string }>({
    mutationFn: async ({ name }) =>
      toView(unwrap(await api.PATCH("/api/agent-identities/{agentIdentityId}", { params: { path: { agentIdentityId: id } }, body: { name } }))),
    retry: retryUnlessRefused,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: agentIdentityKeys.all }),
    onError: (error) => {
      if (isAgentIdentityGone(error)) {
        void queryClient.invalidateQueries({ queryKey: agentIdentityKeys.all });
      }
    },
  });
}

export function useRevokeAgentIdentity(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (): Promise<void> => {
      unwrap(await api.DELETE("/api/agent-identities/{agentIdentityId}", { params: { path: { agentIdentityId: id } } }));
    },
    retry: retryUnlessRefused,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: agentIdentityKeys.all }),
    onError: (error) => {
      if (isAgentIdentityGone(error)) {
        void queryClient.invalidateQueries({ queryKey: agentIdentityKeys.all });
      }
    },
  });
}

function toView(item: Schemas["AgentIdentityView"]): AgentIdentityView {
  return {
    id: item.id,
    name: item.name,
    clientName: item.clientName,
    clientId: item.clientId,
    scopes: item.scopes,
    status: item.status,
    connectedAt: item.connectedAt,
    lastUsedAt: item.lastUsedAt ?? null,
  };
}
