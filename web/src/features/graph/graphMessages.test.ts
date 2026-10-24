import { describe, expect, it } from "vitest";

import { ApiError } from "@/api/client";
import { entryDescription, provenanceOf, schemaRefusalMessage, softRelationCountText, softRelationExplanationText, writeErrorMessage } from "./graphMessages";
import type { ChangeSetItem } from "./recentChanges";

function changeSet(
  fields: Pick<ChangeSetItem, "actorKind" | "channel"> & Partial<ChangeSetItem>,
): ChangeSetItem {
  return { agentIdentityId: null, source: null, ...fields } as ChangeSetItem;
}

describe("provenanceOf", () => {
  it("names the metadata fetch as the system, on its own channel", () => {
    expect(
      provenanceOf(changeSet({ actorKind: "system", channel: "system" })),
    ).toBe("Busca de metadados · Sistema");
  });

  it("names the user and an agent as before", () => {
    expect(provenanceOf(changeSet({ actorKind: "human", channel: "ui" }))).toBe(
      "Você · Interface",
    );
    expect(
      provenanceOf(
        changeSet({
          actorKind: "agent_identity",
          channel: "mcp",
          agentIdentityId: "a1",
          source: "chat",
        }),
      ),
    ).toBe("Agente a1 · MCP · fonte: chat");
  });
});

describe("entryDescription", () => {
  const entry = (fields: Partial<ChangeSetItem["entries"][number]>): ChangeSetItem["entries"][number] => ({
    sequence: 0,
    entityKind: "node",
    entityId: "e1",
    operation: "created",
    label: null,
    isPurged: false,
    cascadeOf: null,
    ...fields,
  });

  it("names a Tag and a Tag applied to a Node", () => {
    expect(entryDescription(entry({ entityKind: "tag", label: "leitura" }))).toBe("Criou Tag «leitura»");
    expect(entryDescription(entry({ entityKind: "node_tag", operation: "deleted" }))).toBe("Apagou Tag em Node «(sem rótulo)»");
  });

  it("names a Collection and a Node added to one", () => {
    expect(entryDescription(entry({ entityKind: "collection", label: "Quero ler" }))).toBe("Criou Collection «Quero ler»");
    expect(entryDescription(entry({ entityKind: "collection_member", label: "Duna" }))).toBe("Criou Node em Collection «Duna»");
  });

  it("names an ignored Soft Relation", () => {
    expect(entryDescription(entry({ entityKind: "soft_dismissal" }))).toBe("Criou Soft Relation ignorada «(sem rótulo)»");
  });
});

describe("property editor refusals", () => {
  it.each([
    ["graph.property_kind_change_blocked", 422, "Crie uma nova propriedade."],
    ["graph.property_rewrite_too_large", 422, "Nodes demais guardam valores nesta propriedade"],
    ["graph.property_usage_changed", 409, "Confira a nova contagem e confirme de novo."],
  ])("explains %s", (code, status, expected) => {
    expect(writeErrorMessage(new ApiError(status, code, "Refused"))).toContain(expected);
  });

  it("explains that an option Nodes chose cannot be removed, and points to archiving it", () => {
    expect(schemaRefusalMessage({ kind: "has_values" })).toContain("arquive-a");
  });
});

describe("Soft Relation texts", () => {
  it("explains an embedding inference with its score and model", () => {
    expect(softRelationExplanationText({ method: "embedding", modelId: "multilingual-e5-small", score: 0.8234 })).toBe(
      "embedding · score 0,82 · multilingual-e5-small",
    );
  });

  it("leaves the model out of any other method, and the score out when it is missing", () => {
    expect(softRelationExplanationText({ method: "coocorrência", modelId: "multilingual-e5-small", score: "0.5" })).toBe("coocorrência · score 0,5");
    expect(softRelationExplanationText({ method: "embedding", modelId: "multilingual-e5-small", score: null })).toBe("embedding");
    expect(softRelationExplanationText({ method: "embedding" })).toBe("embedding");
    expect(softRelationExplanationText({ method: "embedding", modelId: null, score: 0.9 })).toBe("embedding · score 0,9");
  });

  it("counts Soft Relations in the glossary's words", () => {
    expect([softRelationCountText(1), softRelationCountText(3)]).toEqual(["1 Soft Relation", "3 Soft Relations"]);
  });
});
