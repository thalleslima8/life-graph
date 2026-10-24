import { queryOptions } from "@tanstack/react-query";
import { z } from "zod";
import { api, isApiError, unwrap } from "@/api/client";
import type { components } from "@/api/schema.gen";
import { graphKeys, useGraphWrite, type GraphWriteReceipt } from "@/features/graph/graphData";
import { nameKeyOf } from "@/lib/utils";

/**
 * Tags (DA-160, DA-161): the person's list and one named write hook per action. Every write
 * is a GraphChangeSet of the single pipeline (DA-013) and invalidates the graph's reads.
 */

type Schemas = components["schemas"];
export type TagItem = Schemas["TagItem"];
export type NodeTag = Schemas["NodeTagItem"];

// Mirrors GraphLimits.TagNameMaxLength; the server always checks again (FE-020).
export const TAG_NAME_MAX_LENGTH = 50;
// The API caps a page at 100.
const TAGS_PAGE_SIZE = 100;

/**
 * A Tag name as the server stores it (DA-161): trimmed, without the leading #, inner spaces
 * collapsed; empty, a # elsewhere or more than 50 characters is refused. The server stays the
 * authority, including for "same name ignoring case and accents".
 */
export const tagNameSchema = z
  .string()
  .transform((value) => {
    const trimmed = value.trim();
    const withoutHash = trimmed.startsWith("#") ? trimmed.slice(1) : trimmed;
    return withoutHash.trim().split(/\s+/).filter(Boolean).join(" ");
  })
  .pipe(
    z
      .string()
      .min(1, "Informe um nome.")
      .refine((name) => !name.includes("#"), "Use # só no início.")
      .refine((name) => name.length <= TAG_NAME_MAX_LENGTH, `Use no máximo ${TAG_NAME_MAX_LENGTH} caracteres.`),
  );

/** Every Tag of the person, sorted by name; a personal list, read page by page until the end. */
export const tagsQuery = queryOptions({
  queryKey: graphKeys.tags,
  queryFn: async ({ signal }) => {
    const tags: TagItem[] = [];
    let cursor: string | undefined;
    do {
      const page = unwrap(await api.GET("/api/tags", { params: { query: { limit: TAGS_PAGE_SIZE, cursor } }, signal }));
      tags.push(...page.data);
      cursor = page.page.nextCursor ?? undefined;
    } while (cursor !== undefined);

    return tags.sort((left, right) => left.name.localeCompare(right.name, "pt-BR", { sensitivity: "base" }));
  },
});

/**
 * The name as the server compares it: case and accents folded, so "Livro" and "lívro" are the
 * same Tag (DA-161). Used only to match and suggest; the server decides.
 */
export function tagKeyOf(name: string): string {
  return nameKeyOf(name);
}

/** Another Tag already has this name, ignoring case and accents (DA-161). */
export const isTagNameConflict = (error: unknown): boolean => isApiError(error, "graph.tag_name_taken");

/** The id of the Tag a create made, read from its receipt. */
export function createdTagIdOf(receipt: GraphWriteReceipt): string | undefined {
  return receipt.changes.find((change) => change.kind === "tag" && change.operation === "created")?.entityId;
}

export function useCreateTag() {
  return useGraphWrite(async (name: string) => unwrap(await api.POST("/api/tags", { body: { name } })));
}

export function useRenameTag(id: string) {
  return useGraphWrite(async (name: string) => unwrap(await api.PATCH("/api/tags/{tagId}", { params: { path: { tagId: id } }, body: { name } })));
}

/** Unlinks the Tag from every Node in the same GraphChangeSet, which can be undone (DA-160). */
export function useDeleteTag(id: string) {
  return useGraphWrite(async () => unwrap(await api.DELETE("/api/tags/{tagId}", { params: { path: { tagId: id } } })));
}

/** Idempotent and without a version: the link is not Node state (DA-160). */
export function useTagNode(nodeId: string) {
  return useGraphWrite(async (tagId: string) => unwrap(await api.PUT("/api/nodes/{nodeId}/tags/{tagId}", { params: { path: { nodeId, tagId } } })));
}

export function useUntagNode(nodeId: string) {
  return useGraphWrite(async (tagId: string) =>
    unwrap(await api.DELETE("/api/nodes/{nodeId}/tags/{tagId}", { params: { path: { nodeId, tagId } } })),
  );
}
