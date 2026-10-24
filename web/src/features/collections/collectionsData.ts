import { infiniteQueryOptions, queryOptions, useQuery } from "@tanstack/react-query";
import { z } from "zod";
import { api, unwrap } from "@/api/client";
import type { components } from "@/api/schema.gen";
import {
  graphKeys,
  NAME_MAX_LENGTH,
  RELATION_KIND_MAX_LENGTH,
  retryUnlessNotFound,
  useGraphWrite,
  type GraphWriteReceipt,
} from "@/features/graph/graphData";

/**
 * Collections (DA-163, DA-164): saved queries over the graph, never owners of the data
 * (DA-063). A manual one lists the Nodes the person added; a dynamic one is evaluated on the
 * server at every read. Every write is a GraphChangeSet of the single pipeline (DA-013).
 */

type Schemas = components["schemas"];
export type CollectionItem = Schemas["CollectionItem"];
export type CollectionReviewItem = Schemas["CollectionReviewItem"];

// Mirrors GraphLimits.CollectionCriteriaMaxCount; the server always checks again (FE-020).
export const CRITERIA_MAX_COUNT = 20;
// The API caps a page at 100.
const COLLECTIONS_PAGE_SIZE = 100;
/** How many pinned Collections the menu shows before "Todas as Collections" (DA-173). */
export const NAV_PINNED_LIMIT = 8;

const idSchema = z.string().min(1, "Escolha um item.");

/** One criterion (DA-164); criteria hold ids, never names, so a rename keeps them working. */
export const criterionSchema = z.discriminatedUnion("type", [
  z.object({ type: z.literal("type"), typeId: idSchema }),
  z.object({ type: z.literal("tag"), tagId: idSchema }),
  z.object({
    type: z.literal("property"),
    propertyId: idSchema,
    op: z.enum(["equals", "contains", "is_empty"]),
    value: z.unknown().optional(),
  }),
  z.object({
    type: z.literal("relation"),
    nodeId: idSchema,
    direction: z.literal("either"),
    // Relation kinds are free strings, compared as stored (Adjustment 5): trimmed, empty is absent.
    relationKind: z
      .string()
      .trim()
      .max(RELATION_KIND_MAX_LENGTH, `Use no máximo ${RELATION_KIND_MAX_LENGTH} caracteres.`)
      .transform((kind) => (kind ? kind : undefined))
      .optional(),
  }),
]);

export type Criterion = z.input<typeof criterionSchema>;

export const collectionCriteriaSchema = z.object({
  version: z.literal(1),
  match: z.enum(["all", "any"]),
  criteria: z
    .array(criterionSchema)
    .min(1, "Adicione ao menos um critério.")
    .max(CRITERIA_MAX_COUNT, `Use no máximo ${CRITERIA_MAX_COUNT} critérios.`),
});

export type CollectionCriteria = z.input<typeof collectionCriteriaSchema>;

/** The form of a Collection: no criteria is a manual one (the kind is fixed after creation). */
export const collectionFormSchema = z.object({
  name: z.string().trim().min(1, "Informe um nome.").max(NAME_MAX_LENGTH, `Use no máximo ${NAME_MAX_LENGTH} caracteres.`),
  pinned: z.boolean(),
  criteria: collectionCriteriaSchema.optional(),
});

export type CollectionFormInput = z.input<typeof collectionFormSchema>;
export type CollectionInput = z.output<typeof collectionFormSchema>;

/** Manual when it has no criteria (FE-010: derived, not stored twice). */
export function isManual(collection: Pick<CollectionItem, "criteria">): boolean {
  return collection.criteria == null;
}

/** The criteria as the form edits them, from the wire's nullable members. */
export function criteriaFromWire(criteria: Schemas["CollectionCriteria"]): CollectionCriteria {
  return {
    version: 1,
    match: criteria.match,
    criteria: criteria.criteria.map((criterion): Criterion => {
      switch (criterion.type) {
        case "type":
          return { type: "type", typeId: criterion.typeId ?? "" };
        case "tag":
          return { type: "tag", tagId: criterion.tagId ?? "" };
        case "property":
          return { type: "property", propertyId: criterion.propertyId ?? "", op: criterion.op ?? "equals", value: criterion.value };
        case "relation":
          return {
            type: "relation",
            nodeId: criterion.nodeId ?? "",
            direction: "either",
            relationKind: criterion.relationKind ?? undefined,
          };
      }
    }),
  };
}

function toRequest(input: CollectionInput): Schemas["CollectionRequest"] {
  return { name: input.name, pinned: input.pinned, criteria: input.criteria ? (input.criteria as Schemas["CollectionCriteria"]) : null };
}

/** Every Collection of the person, in creation order; a personal list, read page by page until the end. */
export const collectionsQuery = queryOptions({
  queryKey: graphKeys.collections,
  queryFn: async ({ signal }) => {
    const collections: CollectionItem[] = [];
    let cursor: string | undefined;
    do {
      const page = unwrap(await api.GET("/api/collections", { params: { query: { limit: COLLECTIONS_PAGE_SIZE, cursor } }, signal }));
      collections.push(...page.data);
      cursor = page.page.nextCursor ?? undefined;
    } while (cursor !== undefined);

    return collections;
  },
});

export const collectionQuery = (id: string) =>
  queryOptions({
    queryKey: graphKeys.collection(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET("/api/collections/{collectionId}", { params: { path: { collectionId: id } }, signal })),
    retry: retryUnlessNotFound,
  });

/**
 * The members, newest first, page by page ("Carregar mais"). Read again on focus and on every
 * probe that finds a change, so a Node an agent just made shows up without a reload (DA-024).
 */
export const collectionMembersQuery = (id: string) =>
  infiniteQueryOptions({
    queryKey: graphKeys.collectionMembers(id),
    queryFn: async ({ pageParam, signal }) =>
      unwrap(
        await api.GET("/api/collections/{collectionId}/members", {
          params: { path: { collectionId: id }, query: { cursor: pageParam } },
          signal,
        }),
      ),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (page) => page.page.nextCursor ?? undefined,
    retry: retryUnlessNotFound,
    staleTime: 0,
    refetchOnWindowFocus: true,
  });

/** The id of the Collection a create made, read from its receipt. */
export function createdCollectionIdOf(receipt: GraphWriteReceipt): string | undefined {
  return receipt.changes.find((change) => change.kind === "collection" && change.operation === "created")?.entityId;
}

export function useCreateCollection() {
  return useGraphWrite(async (input: CollectionInput) => unwrap(await api.POST("/api/collections", { body: toRequest(input) })));
}

/** Replaces the Collection whole: name, criteria and pinned (API-020). */
export function useUpdateCollection(id: string) {
  return useGraphWrite(async (input: CollectionInput) =>
    unwrap(await api.PUT("/api/collections/{collectionId}", { params: { path: { collectionId: id } }, body: toRequest(input) })),
  );
}

export function useDeleteCollection(id: string) {
  return useGraphWrite(async () => unwrap(await api.DELETE("/api/collections/{collectionId}", { params: { path: { collectionId: id } } })));
}

/** Manual Collections only; the server answers graph.collection_not_manual otherwise. */
export function useAddToCollection(id: string) {
  return useGraphWrite(async (nodeId: string) =>
    unwrap(await api.PUT("/api/collections/{collectionId}/members/{nodeId}", { params: { path: { collectionId: id, nodeId } } })),
  );
}

export function useRemoveFromCollection(id: string) {
  return useGraphWrite(async (nodeId: string) =>
    unwrap(await api.DELETE("/api/collections/{collectionId}/members/{nodeId}", { params: { path: { collectionId: id, nodeId } } })),
  );
}

/** The input that keeps a Collection as it is but for `pinned` (DA-173). */
export function withPinned(collection: CollectionItem, pinned: boolean): CollectionInput {
  return { name: collection.name, pinned, criteria: collection.criteria ? criteriaFromWire(collection.criteria) : undefined };
}

export const collectionPath = (id: string) => `/collections/${id}`;

/** The pinned Collections the menu shows: the first 8 by creation (DA-173). */
export function useCollectionNav(): { items: { to: string; label: string; needsReview: boolean }[]; isPending: boolean; isError: boolean } {
  const collections = useQuery(collectionsQuery);
  const items = (collections.data ?? [])
    .filter((collection) => collection.pinned)
    .slice(0, NAV_PINNED_LIMIT)
    .map((collection) => ({
      to: collectionPath(collection.id),
      label: collection.name,
      needsReview: (collection.needsReview?.length ?? 0) > 0,
    }));
  return { items, isPending: collections.isPending, isError: collections.isError };
}

/** Whether this Collection is pinned but past the 8 the menu shows (DA-173). */
export function isOutsideNav(collections: CollectionItem[], id: string): boolean {
  const pinned = collections.filter((collection) => collection.pinned);
  const position = pinned.findIndex((collection) => collection.id === id);
  return position >= NAV_PINNED_LIMIT;
}
