import type { SearchMatch } from "./searchData";

/** The visible texts of the search (FE-062: one place). */

export const SEARCH_TITLE = "Busca";
export const SEARCH_BOX_LABEL = "Buscar no grafo";
export const SEARCH_SUBMIT_LABEL = "Buscar";
export const SEARCH_HINT = "Digite pelo menos 2 caracteres e busque.";
export const SEARCH_LOADING = "Buscando…";
export const SEARCH_ERROR = "Não foi possível buscar agora. Tente novamente em instantes.";
export const SEARCH_DEGRADED_NOTE =
  "A busca por significado está indisponível agora: estes resultados vêm só das palavras.";
export const SEARCH_RESULTS_LABEL = "Resultados";

export const MATCH_LABELS: Readonly<Record<SearchMatch, string>> = {
  text: "Pelas palavras",
  semantic: "Pelo significado",
  both: "Palavras e significado",
};

export function noResultsMessage(q: string): string {
  return `Nenhum Node encontrado para “${q}”.`;
}

export function resultCountMessage(count: number): string {
  return count === 1 ? "1 resultado" : `${count.toLocaleString("pt-BR")} resultados`;
}
