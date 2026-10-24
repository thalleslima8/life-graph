import { queryOptions } from "@tanstack/react-query";
import { useCallback } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { api, unwrap } from "@/api/client";
import type { components } from "@/api/schema.gen";
import { graphKeys } from "@/features/graph/graphData";

/**
 * The search by words and by meaning (DA-183, DA-184): runs only on submit, never while typing;
 * the words live in the URL (`/search?q=`, FE-012). Its key sits under the graph's, so a write
 * refreshes the results.
 */

type Schemas = components["schemas"];
export type SearchResponse = Schemas["NodeSearchResults"];
export type SearchResult = Schemas["NodeSearchItem"];
export type SearchMatch = SearchResult["match"];

/** Mirrors GraphReads.SearchMinLength; the server checks again (FE-020). */
export const SEARCH_MIN_LENGTH = 2;
/** Mirrors GraphReadLimits.QueryMaxLength. */
export const SEARCH_MAX_LENGTH = 200;
// The most the API returns (GraphReadLimits.SearchMaxCount).
const SEARCH_LIMIT = 25;
const SEARCH_STALE_TIME_MS = 30_000;

export const SEARCH_PATH = "/search";

export function isSearchable(q: string): boolean {
  return q.trim().length >= SEARCH_MIN_LENGTH;
}

export function searchQuery({ q }: { q: string }) {
  const text = q.trim();
  return queryOptions({
    queryKey: [...graphKeys.all, "search", text] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET("/api/search", { params: { query: { q: text, limit: SEARCH_LIMIT } }, signal })),
    enabled: isSearchable(text),
    staleTime: SEARCH_STALE_TIME_MS,
  });
}

/** The search's words from the URL, and the way to search for others: the search page with them. */
export function useSearchParamsState(): { q: string; setSearch: (q: string) => void } {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const setSearch = useCallback(
    (next: string) => {
      void navigate({ pathname: SEARCH_PATH, search: `?${new URLSearchParams({ q: next.trim() }).toString()}` });
    },
    [navigate],
  );
  return { q: params.get("q") ?? "", setSearch };
}
