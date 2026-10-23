import type { CollectionReviewItem } from "./collectionsData";

/** Why a saved criterion needs review, by the reason the server sends (DA-171). */
const REVIEW_REASONS: Readonly<Record<string, string>> = {
  type_missing: "o Type não existe mais",
  tag_missing: "a Tag não existe mais",
  property_missing: "a propriedade não existe mais",
  node_missing: "o Node não está mais disponível",
  operator_not_valid_for_kind: "a comparação não vale mais para o tipo da propriedade",
  option_missing: "a opção não existe mais",
  value_not_valid_for_kind: "o valor não vale mais para o tipo da propriedade",
};

export const NEEDS_REVIEW_LABEL = "Precisa de revisão";
export const ALL_COLLECTIONS_LABEL = "Todas as Collections";
export const OUTSIDE_NAV_NOTE = "fora do menu";
export const NAV_FULL_HINT = "O menu já mostra 8 Collections fixadas; esta fica fora dele.";
export const MANUAL_EMPTY_MESSAGE = "Adicione Nodes pelo Inspector.";
export const DYNAMIC_EMPTY_MESSAGE = "Nenhum Node atende aos critérios.";

/** "Critério 2: a Tag não existe mais" — numbered from 1, as the editor shows them. */
export function reviewText(item: CollectionReviewItem): string {
  const reason = REVIEW_REASONS[item.reason] ?? "não pode ser avaliado";
  return `Critério ${Number(item.criterionIndex) + 1}: ${reason}.`;
}
