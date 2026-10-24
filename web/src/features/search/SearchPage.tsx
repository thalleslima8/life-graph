import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router";
import { NO_TYPE_LABEL } from "@/features/graph/graphMessages";
import { isSearchable, searchQuery, useSearchParamsState, type SearchResult } from "./searchData";
import {
  MATCH_LABELS,
  noResultsMessage,
  resultCountMessage,
  SEARCH_DEGRADED_NOTE,
  SEARCH_ERROR,
  SEARCH_HINT,
  SEARCH_LOADING,
  SEARCH_RESULTS_LABEL,
  SEARCH_TITLE,
} from "./searchMessages";

/** The results of a search by words and by meaning (DA-183); the words come from `?q=`. */
export function SearchPage() {
  const { q } = useSearchParamsState();
  const words = q.trim();
  const searchable = isSearchable(words);
  const results = useQuery(searchQuery({ q: words }));

  return (
    <section aria-labelledby="search-title" className="space-y-4">
      <h1 id="search-title" className="text-2xl font-semibold">
        {SEARCH_TITLE}
      </h1>
      {!searchable && <p className="text-sm text-muted-foreground">{SEARCH_HINT}</p>}
      {searchable && results.isPending && <p role="status">{SEARCH_LOADING}</p>}
      {searchable && results.isError && (
        <p role="alert" className="text-sm text-destructive">
          {SEARCH_ERROR}
        </p>
      )}
      {searchable && results.data?.degraded && (
        <p role="status" className="text-sm text-muted-foreground">
          {SEARCH_DEGRADED_NOTE}
        </p>
      )}
      <p aria-live="polite" className="text-sm text-muted-foreground">
        {searchable && results.data ? resultCountMessage(results.data.items.length) : ""}
      </p>
      {searchable &&
        results.data &&
        (results.data.items.length === 0 ? (
          <p>{noResultsMessage(words)}</p>
        ) : (
          <ul aria-label={SEARCH_RESULTS_LABEL} className="space-y-3">
            {results.data.items.map((result) => (
              <SearchResultItem key={result.id} result={result} />
            ))}
          </ul>
        ))}
    </section>
  );
}

/** One Node found: its title links to the Inspector; how it was found is said in words (FE-034). */
function SearchResultItem({ result }: { result: SearchResult }) {
  return (
    <li className="rounded-md border p-3">
      <div className="flex flex-wrap items-baseline gap-2">
        <Link to={`/nodes/${result.id}`} className="font-medium underline-offset-4 hover:underline">
          {result.title}
        </Link>
        <span className="text-sm text-muted-foreground">{result.typeName ?? NO_TYPE_LABEL}</span>
        <span className="rounded border px-1.5 text-xs">{MATCH_LABELS[result.match]}</span>
      </div>
      {result.snippet && <p className="mt-1 line-clamp-2 text-sm text-muted-foreground">{result.snippet}</p>}
    </li>
  );
}
