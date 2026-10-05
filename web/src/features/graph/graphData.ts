import { infiniteQueryOptions, queryOptions, useMutation, useQueryClient, type MutationFunction } from "@tanstack/react-query";
import { useState } from "react";
import { useSearchParams } from "react-router";
import { api, isApiError, unwrap, type ApiError } from "@/api/client";
import type { components } from "@/api/schema.gen";

/**
 * The graph's server state for the SPA (DA-118): query options per resource, the list filter
 * in the URL (FE-012) and one named mutation hook per action. Every write goes through the
 * single pipeline on the server (DA-013); here it only invalidates what it may have changed.
 */

type Schemas = components["schemas"];
export type NodeSummary = Schemas["NodeSummary"];
export type NodeDetail = Schemas["NodeDetail"];
export type NodePropertyValue = Schemas["NodePropertyValue"];
export type RelationItem = Schemas["RelationItem"];
export type TypeItem = Schemas["TypeItem"];
export type PropertyDefinitionItem = Schemas["PropertyDefinitionItem"];
export type PropertyValueKind = Schemas["PropertyValueKind"];
export type SelectOption = Schemas["SelectOption"];
export type GraphWriteReceipt = Schemas["GraphWriteReceipt"];

/** The Type choice for "every Type", in the list filter. */
export const ANY_TYPE = "any";
/** The Type choice for "no Type" ("Sem Type", DA-018): the list filter and a Node's own Type. */
export const NO_TYPE = "none";

/** `type`: any Type, the Nodes without one, or a Type id. */
export type NodeListFilter = { type: typeof ANY_TYPE | typeof NO_TYPE | string; inbox: boolean };

// Mirror GraphLimits on the server, which always checks again (FE-020).
export const TITLE_MAX_LENGTH = 500;
export const BODY_MAX_LENGTH = 100_000;
export const NAME_MAX_LENGTH = 100;
export const RELATION_KIND_MAX_LENGTH = 100;

// The ontology of a personal graph fits one page; the API caps a page at 100.
const ONTOLOGY_PAGE_SIZE = 100;
const RELATIONS_PAGE_SIZE = 100;

export const graphKeys = {
  all: ["graph"] as const,
  nodes: (filter: NodeListFilter) => ["graph", "nodes", filter] as const,
  node: (id: string) => ["graph", "node", id] as const,
  relations: (id: string) => ["graph", "node", id, "relations"] as const,
  types: ["graph", "types"] as const,
  propertyDefinitions: ["graph", "property-definitions"] as const,
};

/** The feed of GraphChangeSets; every write of this SPA adds one to it. */
export const changeSetsKey = ["changesets"] as const;

/** The version the API sends is an int32, typed loosely by the OpenAPI generator. */
export function versionOf(node: { version: number | string }): number {
  return Number(node.version);
}

export const isVersionConflict = (error: unknown): error is ApiError => isApiError(error, "graph.node_version_conflict");

export const isNotFound = (error: unknown): error is ApiError => isApiError(error) && error.status === 404;

// A missing resource stays missing: retrying a 404 only delays the answer.
function retryUnlessNotFound(failureCount: number, error: unknown): boolean {
  return !isNotFound(error) && failureCount < 1;
}

export const nodesQuery = (filter: NodeListFilter) =>
  infiniteQueryOptions({
    queryKey: graphKeys.nodes(filter),
    queryFn: async ({ pageParam, signal }) =>
      unwrap(
        await api.GET("/api/nodes", {
          params: {
            query: {
              typeId: filter.type === ANY_TYPE || filter.type === NO_TYPE ? undefined : filter.type,
              withoutType: filter.type === NO_TYPE ? true : undefined,
              inInbox: filter.inbox ? true : undefined,
              cursor: pageParam,
            },
          },
          signal,
        }),
      ),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (page) => page.page.nextCursor ?? undefined,
  });

export const nodeQuery = (id: string) =>
  queryOptions({
    queryKey: graphKeys.node(id),
    queryFn: async ({ signal }) => unwrap(await api.GET("/api/nodes/{nodeId}", { params: { path: { nodeId: id } }, signal })),
    retry: retryUnlessNotFound,
  });

export const nodeRelationsQuery = (id: string) =>
  queryOptions({
    queryKey: graphKeys.relations(id),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET("/api/nodes/{nodeId}/relations", {
          params: { path: { nodeId: id }, query: { limit: RELATIONS_PAGE_SIZE } },
          signal,
        }),
      ),
    retry: retryUnlessNotFound,
  });

export const typesQuery = queryOptions({
  queryKey: graphKeys.types,
  queryFn: async ({ signal }) => unwrap(await api.GET("/api/types", { params: { query: { limit: ONTOLOGY_PAGE_SIZE } }, signal })).data,
});

export const propertyDefinitionsQuery = queryOptions({
  queryKey: graphKeys.propertyDefinitions,
  queryFn: async ({ signal }) =>
    unwrap(await api.GET("/api/property-definitions", { params: { query: { limit: ONTOLOGY_PAGE_SIZE } }, signal })).data,
});

/** The list filter, kept in the URL as `?type=none&inbox=1` (FE-012). */
export function useNodeListFilter(): [NodeListFilter, (next: Partial<NodeListFilter>) => void] {
  const [searchParams, setSearchParams] = useSearchParams();
  const filter: NodeListFilter = { type: searchParams.get("type") || ANY_TYPE, inbox: searchParams.get("inbox") === "1" };

  const setFilter = (next: Partial<NodeListFilter>) => {
    const merged = { ...filter, ...next };
    const params = new URLSearchParams();
    if (merged.type !== ANY_TYPE) {
      params.set("type", merged.type);
    }

    if (merged.inbox) {
      params.set("inbox", "1");
    }

    setSearchParams(params, { replace: true });
  };

  return [filter, setFilter];
}

export type EditBaseline = {
  /** The version the edit started from: what a save sends, so a change made meanwhile is refused, never overwritten. */
  baseVersion: number | undefined;
  /** The Node moved past the base while the form held changes. */
  isStale: boolean;
  /** Takes the current Node as the new base and reloads it; the form resets to it. */
  rebase(): void;
};

/**
 * Captures the version when editing starts (DA-118). While the form is pristine the base
 * follows the Node; once the form changes, a refetch never moves it. `baseVersion` changes
 * only when the form may follow the Node, so the form resets to the Node whenever it does.
 */
export function useEditBaseline(node: NodeDetail | undefined, isDirty: boolean): EditBaseline {
  const queryClient = useQueryClient();
  const nodeVersion = node ? versionOf(node) : undefined;
  const [baseVersion, setBaseVersion] = useState(nodeVersion);

  // Adjusting state while rendering: a pristine form takes each version as it arrives.
  if (!isDirty && nodeVersion !== baseVersion) {
    setBaseVersion(nodeVersion);
  }

  const isStale = nodeVersion !== undefined && baseVersion !== undefined && nodeVersion > baseVersion;

  const rebase = () => {
    setBaseVersion(nodeVersion);
    if (node) {
      void queryClient.invalidateQueries({ queryKey: graphKeys.node(node.id) });
    }
  };

  return { baseVersion, isStale, rebase };
}

/**
 * A write of the SPA: afterwards the graph and the feed are read again. Not awaited, so a
 * form can reset to what it saved before the refetch arrives.
 */
function useGraphWrite<TVariables>(mutationFn: MutationFunction<GraphWriteReceipt, TVariables>, onConflict?: () => void) {
  const queryClient = useQueryClient();
  return useMutation<GraphWriteReceipt, ApiError, TVariables>({
    mutationFn,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: graphKeys.all });
      void queryClient.invalidateQueries({ queryKey: changeSetsKey });
    },
    onError: (error) => {
      // The copy was stale: read the Node again, never retry on its own (DA-117).
      if (isVersionConflict(error)) {
        onConflict?.();
      }
    },
  });
}

// Values by Property Definition id; `null` removes one. The OpenAPI document gives this map
// (Dictionary<Guid, JsonElement>) no value schema, so the generated type is narrowed here.
type PropertyValuesInput = Record<string, unknown>;
export type CreateNodeInput = Omit<Schemas["CreateNodeRequest"], "properties"> & { title: string; properties?: PropertyValuesInput };
export type UpdateNodeInput = Omit<Schemas["UpdateNodeRequest"], "version" | "properties"> & {
  version: number;
  properties?: PropertyValuesInput;
};

export function useCreateNode() {
  return useGraphWrite(async (input: CreateNodeInput) =>
    unwrap(await api.POST("/api/nodes", { body: input as Schemas["CreateNodeRequest"] })),
  );
}

export function useUpdateNode(id: string) {
  const queryClient = useQueryClient();
  return useGraphWrite(
    async (input: UpdateNodeInput) =>
      unwrap(await api.PATCH("/api/nodes/{nodeId}", { params: { path: { nodeId: id } }, body: input as Schemas["UpdateNodeRequest"] })),
    () => void queryClient.invalidateQueries({ queryKey: graphKeys.node(id) }),
  );
}

export function useDeleteNode(id: string) {
  const queryClient = useQueryClient();
  return useGraphWrite(
    async (version: number) =>
      unwrap(await api.DELETE("/api/nodes/{nodeId}", { params: { path: { nodeId: id }, query: { version } } })),
    () => void queryClient.invalidateQueries({ queryKey: graphKeys.node(id) }),
  );
}

/** Leaves the Inbox (DA-019); needs no version (DA-117). */
export function useArchiveNode(id: string) {
  return useGraphWrite(async () => unwrap(await api.POST("/api/nodes/{nodeId}/archival", { params: { path: { nodeId: id } } })));
}

export type CreateRelationInput = { sourceNodeId: string; targetNodeId: string; kind: string };

export function useCreateRelation() {
  return useGraphWrite(async (input: CreateRelationInput) => unwrap(await api.POST("/api/relations", { body: input })));
}

export function useDeleteRelation() {
  return useGraphWrite(async (relationId: string) =>
    unwrap(await api.DELETE("/api/relations/{relationId}", { params: { path: { relationId } } })),
  );
}

export type CreateTypeInput = { name: string; propertyDefinitionIds?: string[] };

export function useCreateType() {
  return useGraphWrite(async (input: CreateTypeInput) => unwrap(await api.POST("/api/types", { body: input })));
}

export function useUpdateType(id: string) {
  return useGraphWrite(async (input: { name: string }) =>
    unwrap(await api.PATCH("/api/types/{typeId}", { params: { path: { typeId: id } }, body: input })),
  );
}

export function useDeleteType(id: string) {
  return useGraphWrite(async () => unwrap(await api.DELETE("/api/types/{typeId}", { params: { path: { typeId: id } } })));
}

export function useAttachPropertyToType(typeId: string) {
  return useGraphWrite(async (propertyDefinitionId: string) =>
    unwrap(await api.PUT("/api/types/{typeId}/properties/{propertyDefinitionId}", { params: { path: { typeId, propertyDefinitionId } } })),
  );
}

export function useDetachPropertyFromType(typeId: string) {
  return useGraphWrite(async (propertyDefinitionId: string) =>
    unwrap(await api.DELETE("/api/types/{typeId}/properties/{propertyDefinitionId}", { params: { path: { typeId, propertyDefinitionId } } })),
  );
}

/** `options`: the full list; an option without `id` is new, an existing one keeps its id. */
export type PropertyDefinitionInput = {
  name: string;
  valueKind: PropertyValueKind;
  options?: { id?: string; label: string }[];
};

export function useCreatePropertyDefinition() {
  return useGraphWrite(async (input: PropertyDefinitionInput) => unwrap(await api.POST("/api/property-definitions", { body: input })));
}

export function useUpdatePropertyDefinition(id: string) {
  return useGraphWrite(async (input: Partial<PropertyDefinitionInput>) =>
    unwrap(
      await api.PATCH("/api/property-definitions/{propertyDefinitionId}", { params: { path: { propertyDefinitionId: id } }, body: input }),
    ),
  );
}

export function useDeletePropertyDefinition(id: string) {
  return useGraphWrite(async () =>
    unwrap(await api.DELETE("/api/property-definitions/{propertyDefinitionId}", { params: { path: { propertyDefinitionId: id } } })),
  );
}

/** Why a change to the ontology was refused (DA-023, DA-115, DA-117). */
export type SchemaRefusal =
  | { kind: "in_use"; entity: "type" | "property"; tombstoneCount?: number; lastPurgeAt?: string }
  | { kind: "has_values" };

const IN_USE_CODES: Readonly<Record<string, "type" | "property">> = {
  "graph.type_in_use": "type",
  "graph.type_in_use_by_deleted_nodes": "type",
  "graph.property_in_use": "property",
  "graph.property_in_use_by_deleted_nodes": "property",
};

/** The refusal, or null for any other error. The count and date are optional: a body may lack them. */
export function toSchemaRefusal(error: unknown): SchemaRefusal | null {
  if (!isApiError(error) || error.code === undefined) {
    return null;
  }

  if (error.code === "graph.property_has_values") {
    return { kind: "has_values" };
  }

  const entity = IN_USE_CODES[error.code];
  if (entity === undefined) {
    return null;
  }

  const count = Number(error.details.deletedNodeCount?.[0]);
  const lastPurgeAt = error.details.lastPurgeAt?.[0];
  return {
    kind: "in_use",
    entity,
    ...(Number.isInteger(count) && count >= 0 ? { tombstoneCount: count } : {}),
    ...(lastPurgeAt ? { lastPurgeAt } : {}),
  };
}
