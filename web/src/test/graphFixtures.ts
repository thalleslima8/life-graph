import { http, HttpResponse } from "msw";
import type { components } from "@/api/schema.gen";
import type { NodeDetail, PropertyDefinitionItem, SoftRelationItem, TypeItem } from "@/features/graph/graphData";
import type { ChangeSetFeed, ChangeSetItem } from "@/features/graph/recentChanges";
import type { TagItem } from "@/features/tags/tagsData";
import type { CollectionItem } from "@/features/collections/collectionsData";
import type { ChangeSetDetail, ProposalDetail, ProposalItem, ProposedOperation } from "@/features/agents/agentChanges";

/** Builders of API bodies for the graph screens; each test overrides only what it is about. */

export const NODE_ID = "01920000-0000-7000-8000-000000000001";
export const OTHER_NODE_ID = "01920000-0000-7000-8000-000000000002";
export const TYPE_ID = "01920000-0000-7000-8000-0000000000a1";
export const DEFINITION_ID = "01920000-0000-7000-8000-0000000000b1";
export const CHANGESET_ID = "01920000-0000-7000-8000-0000000000c1";
export const PROPOSAL_ID = "01920000-0000-7000-8000-0000000000d1";
export const AGENT_ID = "01920000-0000-7000-8000-0000000000e1";

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
    isAccountRoot: false,
    inboxEnteredAt: null,
    createdAt: INSTANT,
    updatedAt: INSTANT,
    properties: [],
    otherProperties: [],
    hiddenFromAgents: false,
    tags: [],
    ...overrides,
  };
}

export function aType(overrides: Partial<TypeItem> = {}): TypeItem {
  return { id: TYPE_ID, name: "Livro", properties: [], createdAt: INSTANT, updatedAt: INSTANT, hiddenFromAgents: false, isSystem: false, ...overrides };
}

export function aDefinition(overrides: Partial<PropertyDefinitionItem> = {}): PropertyDefinitionItem {
  return { id: DEFINITION_ID, name: "Status", valueKind: "text", options: [], createdAt: INSTANT, updatedAt: INSTANT, isSystem: false, typeCount: 0, isArchived: false, ...overrides };
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
    proposalId: null,
    reviewedAt: null,
    agent: null,
    undo: { state: "available", byChangeSetId: null, reason: null },
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

export const TAG_ID = "01920000-0000-7000-8000-0000000000a7";

export function aTag(overrides: Partial<TagItem> = {}): TagItem {
  return { id: TAG_ID, name: "leitura", nodeCount: 0, createdAt: INSTANT, updatedAt: INSTANT, ...overrides };
}

export const COLLECTION_ID = "01920000-0000-7000-8000-0000000000a8";

/** A manual Collection unless criteria are given. */
export function aCollection(overrides: Partial<CollectionItem> = {}): CollectionItem {
  return { id: COLLECTION_ID, name: "Quero ler", criteria: null, pinned: false, needsReview: null, createdAt: INSTANT, updatedAt: INSTANT, ...overrides };
}

/** The ontology reads every graph screen makes, answered empty unless given. */
export function ontologyHandlers({ types = [], definitions = [] }: { types?: TypeItem[]; definitions?: PropertyDefinitionItem[] } = {}) {
  return [
    http.get("/api/types", () => HttpResponse.json({ data: types, page: { nextCursor: null } })),
    http.get("/api/property-definitions", () => HttpResponse.json({ data: definitions, page: { nextCursor: null } })),
  ];
}

export function aProposal(overrides: Partial<ProposalItem> = {}): ProposalItem {
  return {
    id: PROPOSAL_ID,
    agentIdentityId: AGENT_ID,
    agent: { id: AGENT_ID, name: "Claude", revoked: false },
    status: "proposed",
    requiresUser: true,
    reasons: ["sensitive"],
    operationCount: 1,
    summary: { createNode: 0, updateNode: 0, createRelation: 0, removeRelation: 0, deleteNode: 1, captureResource: 0, tagNode: 0, untagNode: 0 },
    source: null,
    createdAt: INSTANT,
    expiresAt: "2026-10-12T12:00:00Z",
    decidedAt: null,
    changeSetId: null,
    isRedacted: false,
    ...overrides,
  };
}

export function anOperation(overrides: Partial<ProposedOperation> = {}): ProposedOperation {
  return {
    index: 0,
    op: "update_node",
    isSensitive: false,
    isRejected: false,
    nodeId: NODE_ID,
    nodeTitle: "Leituras",
    expectedVersion: 1,
    currentVersion: 1,
    ref: null,
    typeId: null,
    suggestedType: null,
    source: null,
    target: null,
    kind: null,
    relationId: null,
    tag: null,
    fields: [{ field: "title", before: "Leituras", after: "Leituras de 2026", overwritesYourValue: true }],
    dependsOn: [],
    ...overrides,
  };
}

export function aProposalDetail(operations: ProposedOperation[] = [anOperation()], overrides: Partial<ProposalDetail> = {}): ProposalDetail {
  return {
    proposal: aProposal({ operationCount: operations.length }),
    agent: { id: AGENT_ID, name: "Claude", revoked: false },
    contentPurged: false,
    operations,
    ...overrides,
  };
}

export function aChangeSetDetail(overrides: Partial<ChangeSetDetail> = {}): ChangeSetDetail {
  return {
    changeSet: aChangeSet({ actorKind: "agent_identity", agentIdentityId: AGENT_ID, channel: "mcp", entries: [{ sequence: 0, entityKind: "node", entityId: NODE_ID, operation: "updated", label: "Leituras de 2026", isPurged: false, cascadeOf: null }] }),
    agent: { id: AGENT_ID, name: "Claude", revoked: false },
    undo: { state: "available", byChangeSetId: null, reason: null },
    entries: [
      {
        sequence: 0,
        entityKind: "node",
        entityId: NODE_ID,
        operation: "updated",
        label: "Leituras de 2026",
        isPurged: false,
        cascadeOf: null,
        fields: [{ field: "title", before: "Leituras", after: "Leituras de 2026", overwritesYourValue: true }],
      },
    ],
    ...overrides,
  };
}

export const RELATION_ID = "01920000-0000-7000-8000-0000000000f1";
export const SOFT_RELATION_ID = "01920000-0000-7000-8000-0000000000f3";

/** A pending Soft Relation of NODE_ID towards OTHER_NODE_ID, inferred by embedding. */
export function aSoftRelation(overrides: Partial<SoftRelationItem> = {}): SoftRelationItem {
  return {
    id: SOFT_RELATION_ID,
    nodeId: OTHER_NODE_ID,
    nodeTitle: "Meditações",
    nodeTypeId: TYPE_ID,
    explanation: { method: "embedding", modelId: "multilingual-e5-small", score: 0.82 },
    ...overrides,
  };
}

/** The local graph as the API sends it: the root and one neighbor, related by one Hard Relation. */
export function aLocalGraph(overrides: Partial<components["schemas"]["LocalGraph"]> = {}): components["schemas"]["LocalGraph"] {
  return {
    rootNodeId: NODE_ID,
    nodes: [
      { id: NODE_ID, title: "Leituras", typeId: null, distance: 0, degree: 1, agentUnreviewed: false },
      { id: OTHER_NODE_ID, title: "Meditações", typeId: TYPE_ID, distance: 1, degree: 1, agentUnreviewed: false },
    ],
    edges: [
      {
        id: RELATION_ID,
        sourceNodeId: NODE_ID,
        targetNodeId: OTHER_NODE_ID,
        kind: "cita",
        assertion: "hard",
        origin: "user",
        strength: 1,
        agentUnreviewed: false,
      },
    ],
    truncated: false,
    omittedNodeCount: 0,
    omittedEdgeCount: 0,
    ...overrides,
  };
}
