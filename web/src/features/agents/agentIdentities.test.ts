import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, renderHook } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { createElement, type ReactNode } from "react";
import { describe, expect, it } from "vitest";
import { ApiError } from "@/api/client";
import { csrfTokenHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { agentChangeKeys } from "./agentChanges";
import { AGENT_NAME_MAX_LENGTH, agentIdentityKeys, agentNameSchema, isAgentIdentityGone, SCOPE_LABELS, useRevokeAgentIdentity } from "./agentIdentities";

const AGENT_ID = "01920000-0000-7000-8000-0000000000a1";

describe("agent identities", () => {
  // DA-125: the same words as the issuer's consent page (AuthorizationEndpoints.ScopeDescriptions, pinned by AuthorizationRulesTests).
  it("labels each scope with the consent page's exact text", () => {
    expect(SCOPE_LABELS).toEqual({
      "lifegraph.read": "Ler o seu grafo: buscar e consultar Nodes, Relations e o contexto que você permitir.",
      "lifegraph.write": "Criar e alterar Nodes e Relations no seu grafo. Pode propor exclusões, que só você aprova.",
    });
  });

  it("has no label for offline access nor for scopes that do not exist yet", () => {
    for (const scope of ["offline_access", "lifegraph.delete", "openid"]) {
      expect(SCOPE_LABELS[scope]).toBeUndefined();
    }
  });

  it("trims the name and keeps it between 1 and 100 characters", () => {
    expect(agentNameSchema.parse("  Pesquisa  ")).toBe("Pesquisa");
    expect(agentNameSchema.safeParse("   ").success).toBe(false);
    expect(agentNameSchema.safeParse("x".repeat(AGENT_NAME_MAX_LENGTH)).success).toBe(true);
    expect(agentNameSchema.safeParse("x".repeat(AGENT_NAME_MAX_LENGTH + 1)).success).toBe(false);
  });

  it("reads only the connection's own 404 as gone", () => {
    expect(isAgentIdentityGone(new ApiError(404, "accounts.agent_identity_not_found", "gone"))).toBe(true);
    expect(isAgentIdentityGone(new ApiError(404, "not_found", "other"))).toBe(false);
    expect(isAgentIdentityGone(new ApiError(422, "accounts.agent_identity_not_found", "odd"))).toBe(false);
    expect(isAgentIdentityGone(new Error("network"))).toBe(false);
  });

  // DA-135: revoking withdraws the agent's pending proposals, so Agent Changes is read again too.
  it("reads the connections and the agents' changes again after revoking", async () => {
    server.use(
      csrfTokenHandler().handler,
      http.delete("/api/agent-identities/:id", () => new HttpResponse(null, { status: 204 })),
    );
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const agentChangesKey = agentChangeKeys.proposals({ unreviewed: false });
    queryClient.setQueryData(agentChangesKey, { pages: [], pageParams: [] });
    queryClient.setQueryData(agentIdentityKeys.list, { pages: [], pageParams: [] });
    const wrapper = ({ children }: { children: ReactNode }) => createElement(QueryClientProvider, { client: queryClient }, children);
    const { result } = renderHook(() => useRevokeAgentIdentity(AGENT_ID), { wrapper });

    await act(() => result.current.mutateAsync());

    expect(queryClient.getQueryState(agentChangesKey)?.isInvalidated).toBe(true);
    expect(queryClient.getQueryState(agentIdentityKeys.list)?.isInvalidated).toBe(true);
  });
});
