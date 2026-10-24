import { useId, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { SEARCH_MAX_LENGTH, useSearchParamsState } from "./searchData";
import { SEARCH_BOX_LABEL, SEARCH_SUBMIT_LABEL } from "./searchMessages";

/**
 * The header's search: submitting opens the search page with the words; typing alone searches nothing (DA-184).
 * The box shows the words of the current `?q=`, and follows them when the address changes.
 */
export function SearchBox() {
  const { q, setSearch } = useSearchParamsState();
  const inputId = useId();

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const words = String(new FormData(event.currentTarget).get("q") ?? "").trim();
    if (words.length > 0) {
      setSearch(words);
    }
  }

  return (
    <form role="search" aria-label={SEARCH_BOX_LABEL} onSubmit={submit} className="flex items-center gap-2">
      <label htmlFor={inputId} className="sr-only">
        {SEARCH_BOX_LABEL}
      </label>
      <Input key={q} defaultValue={q} id={inputId} name="q" type="search" maxLength={SEARCH_MAX_LENGTH} className="h-8 w-48" />
      <Button type="submit" variant="secondary" size="sm">
        {SEARCH_SUBMIT_LABEL}
      </Button>
    </form>
  );
}
