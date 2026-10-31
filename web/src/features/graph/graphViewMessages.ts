import type { RenderableEdge } from "./graphShape";

/**
 * The texts of the drawing and the list of a graph (FE-062): what both the owner's page and a
 * visitor's page show. Nothing here reads the API, so a visitor's page can use them (DA-222).
 */

const countFormat = new Intl.NumberFormat("pt-BR");

export function counted(count: number, singular: string, plural: string): string {
  return `${countFormat.format(count)} ${count === 1 ? singular : plural}`;
}

export const SELECTED_NODE_LABEL = "selecionado";
export const ROOT_HEADING = "Raiz";
export const REROOT_LABEL = "Centralizar aqui";

/** Hard or Soft, in words: the list cannot say it with a line (FE-034). */
export const ASSERTION_LABELS: Readonly<Record<NonNullable<RenderableEdge["assertion"]>, string>> = { hard: "Hard", soft: "Soft" };

/** The heading of the Nodes at one distance from the root, in the list. */
export function distanceHeading(distance: number): string {
  return distance === 0 ? ROOT_HEADING : `A ${counted(distance, "Relation", "Relations")} da raiz`;
}

/** A Relation in words, from its source to its target. */
export function relationText(sourceTitle: string, kind: string, targetTitle: string): string {
  return `${sourceTitle} → ${kind} → ${targetTitle}`;
}

/** The accessible name of a Node's Relations in the list. */
export function relationsOfLabel(nodeTitle: string): string {
  return `Relations de ${nodeTitle}`;
}

/** The keys of the local graph (DA-142); a visitor's view cannot re-root, and the list has no zoom. */
export function graphKeyboardHelp({ canReroot, canZoom }: { canReroot: boolean; canZoom: boolean }): string {
  return [
    "Teclado: ↑ e ↓ percorrem os Nodes em ordem; → vai a um vizinho mais longe da raiz e ← a um mais perto.",
    "Enter seleciona.",
    ...(canReroot ? ["Shift+Enter centraliza no Node."] : []),
    ...(canZoom ? ["+ e − aproximam e afastam; 0 restaura o zoom."] : []),
    "Esc volta ao Node selecionado ou à raiz.",
  ].join(" ");
}

const scoreFormat = new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 2 });

/** The explanation of an inferred Relation: what an absent field would print is left out, never "undefined". */
export type InferenceExplanation = { method: string; modelId?: string | null; score?: number | string | null };

/** `{method} · score {score}`, plus ` · {model}` for an embedding; only the method when the score is missing. */
export function softRelationExplanationText(explanation: InferenceExplanation): string {
  const score = explanation.score === null || explanation.score === undefined || explanation.score === "" ? Number.NaN : Number(explanation.score);
  if (Number.isNaN(score)) {
    return explanation.method;
  }

  const model = explanation.method === "embedding" && explanation.modelId ? ` · ${explanation.modelId}` : "";
  return `${explanation.method} · score ${scoreFormat.format(score)}${model}`;
}
