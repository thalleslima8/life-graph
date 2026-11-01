import { isApiError } from "@/api/client";
import { isPlanLimitReached, refusedLimitOf, type LimitKind } from "./planData";

/** How each kind is named in the person's language (GLOSSARY.md). */
export const LIMIT_KIND_LABELS: Record<LimitKind, string> = {
  nodes: "Nodes",
  relations: "Relations",
  resources: "Resources",
  collections: "Collections",
  custom_types: "Types próprios",
  custom_properties: "Properties próprias",
  agent_connections: "agentes conectados",
  shared_graph_views: "links compartilhados",
};

/** What stays true whatever the limit (DA-086): said every time, so the person never reads it as a lock. */
export const LIMIT_KEEPS_ACCESS_MESSAGE =
  "Nada foi bloqueado: o que já existe continua visível, editável, exportável e pode ser excluído.";

/** How to free a place: a connection is revoked, never deleted (DA-262); everything else is deleted. */
function freeingTextOf(kind: LimitKind | undefined): string {
  return kind === "agent_connections"
    ? 'Revogue uma conexão que não usa mais em "Agentes conectados" para liberar espaço.'
    : "Exclua o que não usa mais para liberar espaço.";
}

/** The words for a reached limit, with the way out (DA-086): the notice's and every refused creation's. */
export function limitReachedText(kind: LimitKind, max: number | null | undefined, used?: number): string {
  const label = LIMIT_KIND_LABELS[kind];
  const reachedText =
    max === undefined || max === null
      ? `O limite de ${label} do seu plano foi atingido.`
      : `Você usa ${used ?? max} de ${max} ${label} do seu plano: o limite foi atingido.`;
  return `${reachedText} ${LIMIT_KEEPS_ACCESS_MESSAGE} ${freeingTextOf(kind)}`;
}

/** The text for a creation the server refused past the plan's limit; `undefined` for any other error. */
export function refusedLimitText(error: unknown): string | undefined {
  if (!isPlanLimitReached(error)) {
    return undefined;
  }

  const kind = isApiError(error) ? error.details.kind?.[0] : undefined;
  const known = Object.keys(LIMIT_KIND_LABELS).find((entry) => entry === kind) as LimitKind | undefined;
  return known === undefined
    ? `O limite do seu plano foi atingido. ${LIMIT_KEEPS_ACCESS_MESSAGE} ${freeingTextOf(undefined)}`
    : limitReachedText(known, refusedLimitOf(error));
}
