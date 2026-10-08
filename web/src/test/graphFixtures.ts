import { http, HttpResponse } from "msw";
import type { NodeDetail, PropertyDefinitionItem, TypeItem } from "@/features/graph/graphData";
import type { ChangeSetFeed, ChangeSetItem } from "@/features/graph/recentChanges";

/** Builders of API bodies for the graph screens; each test overrides only what it is about. */

export const NODE_ID = "01920000-0000-7000-8000-000000000001";
export const OTHER_NODE_ID = "01920000-0000-7000-8000-000000000002";
export const TYPE_ID = "01920000-0000-7000-8000-0000000000a1";
export const DEFINITION_ID = "01920000-0000-7000-8000-0000000000b1";
export const CHANGESET_ID = "01920000-0000-7000-8000-0000000000c1";

const INSTANT = "2026-10-05T12:00:00Z";

export function aNode(overrides: Partial<NodeDetail> = {}): NodeDetail {
  return {
    id: NODE_ID,
    title: "Leituras",
    body: "",
    typeId: null,
    typeName: null,
    version: 1,
    inInbox: false,
    inboxEnteredAt: null,
    createdAt: INSTANT,
    updatedAt: INSTANT,
    properties: [],
    otherProperties: [],
    hiddenFromAgents: false,
    ...overrides,
  };
}

export function aType(overrides: Partial<TypeItem> = {}): TypeItem {
  return { id: TYPE_ID, name: "Livro", properties: [], createdAt: INSTANT, updatedAt: INSTANT, hiddenFromAgents: false, ...overrides };
}

export function aDefinition(overrides: Partial<PropertyDefinitionItem> = {}): PropertyDefinitionItem {
  return { id: DEFINITION_ID, name: "Status", valueKind: "text", options: [], createdAt: INSTANT, updatedAt: INSTANT, ...overrides };
}

export function aChangeSet(overrides: Partial<ChangeSetItem> = {}): ChangeSetItem {
  return {
    id: CHANGESET_ID,
    status: "applied",
    actorKind: "human",
    agentIdentityId: null,
    channel: "ui",
    source: null,
    createdAt: INSTANT,
    revertsChangeSetId: null,
    revertedByChangeSetId: null,
    entries: [{ sequence: 0, entityKind: "node", entityId: NODE_ID, operation: "created", label: "Leituras", isPurged: false, cascadeOf: null }],
    ...overrides,
  };
}

export function aFeed(items: ChangeSetItem[], latestCursor: string | null = items.length > 0 ? `cursor-${items[0]?.id}` : null): ChangeSetFeed {
  return { data: items, page: { nextCursor: null }, latestCursor };
}

export function aReceipt(changeSetId = CHANGESET_ID, changes: { kind: "node"; entityId: string; version: number }[] = []) {
  return { changeSetId, changes: changes.map((change) => ({ ...change, operation: "updated" as const })), relationsLeftDeleted: [] };
}

/** The ontology reads every graph screen makes, answered empty unless given. */
export function ontologyHandlers({ types = [], definitions = [] }: { types?: TypeItem[]; definitions?: PropertyDefinitionItem[] } = {}) {
  return [
    http.get("/api/types", () => HttpResponse.json({ data: types, page: { nextCursor: null } })),
    http.get("/api/property-definitions", () => HttpResponse.json({ data: definitions, page: { nextCursor: null } })),
  ];
}
