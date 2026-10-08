import { describe, expect, it } from "vitest";
import { ApiError } from "@/api/client";
import { AGENT_NAME_MAX_LENGTH, agentNameSchema, isAgentIdentityGone, SCOPE_LABELS } from "./agentIdentities";

describe("agent identities", () => {
  // DA-125: the same words as the issuer's consent page (AuthorizationEndpoints.ScopeDescriptions, pinned by AuthorizationRulesTests).
  it("labels each scope with the consent page's exact text", () => {
    expect(SCOPE_LABELS).toEqual({
      "lifegraph.read": "Ler o seu grafo: buscar e consultar Nodes, Relations e o contexto que você permitir.",
      "lifegraph.write": "Criar e alterar Nodes e Relations no seu grafo.",
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
});
