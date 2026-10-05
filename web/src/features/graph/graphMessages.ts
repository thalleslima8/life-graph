import { isApiError } from "@/api/client";
import type { PropertyValueKind } from "./graphData";
import type { ChangeSetItem, UndoRefusal } from "./recentChanges";
import type { SchemaRefusal } from "./graphData";

/** The visible texts of the graph screens (FE-062: one place, formatted by locale). */

export const UNEXPECTED_ERROR_MESSAGE = "Não foi possível concluir agora. Tente novamente em instantes.";
export const LOAD_ERROR_MESSAGE = "Não foi possível carregar. Tente novamente em instantes.";
export const VERSION_CONFLICT_MESSAGE = "Este Node foi alterado em outro lugar. Recarregue para ver a versão atual; nada foi sobrescrito.";
export const NO_TYPE_LABEL = "Sem Type";
export const TYPE_LOADING_LABEL = "Carregando Type…";
export const TYPE_UNAVAILABLE_LABEL = "Type indisponível";
export const TYPES_LOAD_ERROR_MESSAGE = "Não foi possível carregar os Types.";
export const STALE_NODE_MESSAGE = "Este Node mudou desde que você começou a editar.";

export const VALUE_KIND_LABELS: Readonly<Record<PropertyValueKind, string>> = {
  text: "Texto",
  number: "Número",
  boolean: "Sim/Não",
  date: "Data",
  date_time: "Data e hora",
  url: "URL",
  select: "Seleção",
  multi_select: "Seleção múltipla",
};

const WRITE_ERRORS: Readonly<Record<string, string>> = {
  "graph.type_name_taken": "Já existe um Type com esse nome.",
  "graph.property_name_taken": "Já existe uma propriedade com esse nome.",
  "graph.node_not_found": "Este Node não está mais disponível.",
  "graph.relation_not_found": "Esta Relation não está mais disponível.",
  "graph.type_not_found": "Este Type não está mais disponível.",
  "graph.property_definition_not_found": "Esta propriedade não está mais disponível.",
  "graph.write_conflict": "Outra alteração chegou ao mesmo tempo. Tente novamente.",
  payload_too_large: "O conteúdo é grande demais para salvar de uma vez.",
};

const dateTimeFormat = new Intl.DateTimeFormat("pt-BR", { dateStyle: "medium", timeStyle: "short" });
const dateFormat = new Intl.DateTimeFormat("pt-BR", { dateStyle: "long" });

export function formatDateTime(isoInstant: string): string {
  return dateTimeFormat.format(new Date(isoInstant));
}

/** The message for a failed write no field explains. */
export function writeErrorMessage(error: unknown): string {
  if (!isApiError(error)) {
    return UNEXPECTED_ERROR_MESSAGE;
  }

  if (error.code === "too_many_requests") {
    return error.retryAfterSeconds === undefined
      ? "Muitas requisições. Aguarde um pouco e tente novamente."
      : `Muitas requisições. Tente novamente em ${error.retryAfterSeconds} s.`;
  }

  if (error.code === "graph.node_version_conflict") {
    return VERSION_CONFLICT_MESSAGE;
  }

  if (error.code === "validation_failed") {
    const first = Object.values(error.fieldErrors).flat()[0];
    return first ? `Revise os dados: ${first}` : "Revise os dados e tente novamente.";
  }

  return (error.code && WRITE_ERRORS[error.code]) || UNEXPECTED_ERROR_MESSAGE;
}

export function schemaRefusalMessage(refusal: SchemaRefusal): string {
  if (refusal.kind === "has_values") {
    return "Há Nodes com valores nesta propriedade, apagados incluídos: o tipo de valor não muda e uma opção escolhida não sai.";
  }

  const subject = refusal.entity === "type" ? "Este Type" : "Esta propriedade";
  if (refusal.tombstoneCount === undefined) {
    return `${subject} ainda está em uso por Nodes. Tire o uso antes de apagar.`;
  }

  const nodes = refusal.tombstoneCount === 1 ? "1 Node apagado ainda usa" : `${refusal.tombstoneCount} Nodes apagados ainda usam`;
  const purge = refusal.lastPurgeAt ? ` O último será removido de vez em ${dateFormat.format(new Date(refusal.lastPurgeAt))}.` : "";
  return `${subject} não pode ser apagado agora: ${nodes}, e eles ainda podem ser restaurados.${purge}`;
}

export function undoRefusalMessage(refusal: UndoRefusal, changeSet: ChangeSetItem): string {
  switch (refusal.kind) {
    case "conflict": {
      const labels = refusal.entrySequences
        .map((sequence) => changeSet.entries.find((entry) => Number(entry.sequence) === sequence))
        .map((entry) => entry?.label)
        .filter((label): label is string => Boolean(label));
      return labels.length > 0
        ? `Não dá para desfazer: ${labels.join(", ")} mudou depois desta alteração. Nada foi sobrescrito.`
        : "Não dá para desfazer: algo que esta alteração tocou mudou depois. Nada foi sobrescrito.";
    }
    case "in_use":
      return refusal.entity === "type"
        ? "Não dá para desfazer: o Type ainda é usado por Nodes fora desta alteração."
        : "Não dá para desfazer: a propriedade ainda é usada por Nodes fora desta alteração.";
    case "already_reverted":
      return "Esta alteração já foi desfeita.";
    case "window_closed":
      return "O prazo para desfazer terminou: o conteúdo foi removido de vez.";
    case "content_purged":
      return "Não dá para desfazer: parte do conteúdo desta alteração já foi removida de vez.";
    case "not_found":
      return "Esta alteração não está mais disponível.";
  }
}

const CHANNEL_LABELS: Readonly<Record<ChangeSetItem["channel"], string>> = { ui: "Interface", api: "API", mcp: "MCP", import: "Importação" };

/** Who, by which channel and from which source (Provenance). An agent shows by id until E5 names it. */
export function provenanceOf(changeSet: ChangeSetItem): string {
  const who = changeSet.actorKind === "human" ? "Você" : `Agente ${changeSet.agentIdentityId ?? "desconhecido"}`;
  const source = changeSet.source ? ` · fonte: ${changeSet.source}` : "";
  return `${who} · ${CHANNEL_LABELS[changeSet.channel]}${source}`;
}

const OPERATION_LABELS: Readonly<Record<ChangeSetItem["entries"][number]["operation"], string>> = {
  created: "Criou",
  updated: "Alterou",
  deleted: "Apagou",
  restored: "Restaurou",
};

const ENTITY_LABELS: Readonly<Record<ChangeSetItem["entries"][number]["entityKind"], string>> = {
  node: "Node",
  relation: "Relation",
  type: "Type",
  property_definition: "Propriedade",
};

export function entryDescription(entry: ChangeSetItem["entries"][number]): string {
  const label = entry.label ?? (entry.isPurged ? "(conteúdo removido)" : "(sem rótulo)");
  return `${OPERATION_LABELS[entry.operation]} ${ENTITY_LABELS[entry.entityKind]} «${label}»`;
}
