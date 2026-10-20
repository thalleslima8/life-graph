import { isApiError } from "@/api/client";
import type { PropertyValueKind } from "./graphData";
import type { ChangeSetItem, UndoRefusal } from "./recentChanges";
import type { SchemaRefusal } from "./graphData";
import type { GraphViewMode, LevelOfDetail, LocalGraphEdge } from "./localGraph";

/** The visible texts of the graph screens (FE-062: one place, formatted by locale). */

export const UNEXPECTED_ERROR_MESSAGE = "Não foi possível concluir agora. Tente novamente em instantes.";
export const LOAD_ERROR_MESSAGE = "Não foi possível carregar. Tente novamente em instantes.";
export const VERSION_CONFLICT_MESSAGE = "Este Node foi alterado em outro lugar. Recarregue para ver a versão atual; nada foi sobrescrito.";
export const NO_TYPE_LABEL = "Sem Type";
export const LEGEND_SIZE_TEXT = "Tamanho: quanto mais Relations, maior o Node; quanto mais longe da raiz, menor.";
export const LEGEND_HARD_TEXT = "Hard Relation: linha reta";
export const LEGEND_SOFT_TEXT = "Soft Relation: linha curva e mais fina";
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

const countFormat = new Intl.NumberFormat("pt-BR");

function counted(count: number, singular: string, plural: string): string {
  return `${countFormat.format(count)} ${count === 1 ? singular : plural}`;
}

export const GRAPH_UPDATING_MESSAGE = "Atualizando…";
export const GRAPH_DRAW_ERROR_MESSAGE = "Não foi possível desenhar o grafo; mostrando a lista.";
export const SELECTED_NODE_LABEL = "selecionado";
export const ROOT_HEADING = "Raiz";
export const NO_RELATIONS_MESSAGE = "Este Node ainda não tem Relations.";
export const AGENT_UNREVIEWED_LABEL = "mudança de agente não revisada";
export const LEGEND_AGENT_TEXT = "Anel e texto “mudança de agente não revisada”: tocado por um agente, ainda não revisado";
export const AGENT_NEWS_MESSAGE = "Há novidades de agentes.";
export const AGENT_NEWS_ACTION = "Atualizar";
export const SELECTED_GONE_MESSAGE = "O Node selecionado não existe mais.";
export const SELECTED_AGENT_NOTICE = "Este Node tem uma mudança de agente não revisada.";
export const REVIEW_AGENT_CHANGES_LABEL = "Revisar em Agent Changes";
export const INSPECTOR_CLOSE_LABEL = "Fechar";
export const REROOT_LABEL = "Centralizar aqui";
export const SHOW_IN_GRAPH_LABEL = "Ver no grafo";
export const NODE_BODY_LABEL = "Conteúdo";
export const TYPE_NAME_LOADING_LABEL = "Type (carregando o nome…)";

/** What each level of the Semantic Zoom shows, announced when the level changes (DA-140). */
export const ZOOM_LEVEL_MESSAGES: Record<LevelOfDetail, string> = {
  far: "Zoom afastado: títulos só na raiz e nos vizinhos diretos.",
  middle: "Zoom médio: todos os títulos, com a cor do Type.",
  near: "Zoom próximo: resumo de cada Node e o tipo de cada Relation.",
};

/** The local graph page's own texts (FE-062). */
export const LOCAL_GRAPH_TITLE = "Grafo local";
export const LOADING_MESSAGE = "Carregando…";
export const RETRY_LABEL = "Tentar novamente";
export const CREATE_RELATION_LABEL = "Criar Relation";
export const OPEN_IN_INSPECTOR_LABEL = "Abrir no Inspector";
export const VIEW_MODE_LEGEND = "Visualização";
export const DEPTH_LEGEND = "Profundidade";
export const ZOOM_CONTROLS_LABEL = "Zoom";
export const ZOOM_IN_LABEL = "Aproximar";
export const ZOOM_OUT_LABEL = "Afastar";
export const ZOOM_RESET_LABEL = "Restaurar zoom";
export const LEGEND_TITLE = "Legenda";
export const NODE_NOT_FOUND_TITLE = "Node não encontrado";
export const BACK_TO_NODES_LABEL = "Voltar para os Nodes";

/** The heading of the local graph around a root Node. */
export function localGraphTitle(rootTitle: string): string {
  return `Grafo de ${rootTitle}`;
}

/** The names of the two ways to show the local graph (DA-142). */
export const GRAPH_VIEW_MODE_LABELS: Readonly<Record<GraphViewMode, string>> = { canvas: "Desenho", list: "Lista" };

/** Hard or Soft, in words: the list cannot say it with a line (FE-034). */
export const ASSERTION_LABELS: Readonly<Record<LocalGraphEdge["assertion"], string>> = { hard: "Hard", soft: "Soft" };

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

/** The near level's summary of a Node: its Type and how many Relations it has (DA-139). */
export function nodeSummaryText(typeName: string, degree: number): string {
  return `${typeName}, ${counted(degree, "Relation", "Relations")}`;
}

/** The accessible name of the drawing of the local graph. */
export function localGraphLabel(rootTitle: string, nodeCount: number, edgeCount: number): string {
  return `Grafo local de ${rootTitle}: ${counted(nodeCount, "Node", "Nodes")} e ${counted(edgeCount, "Relation", "Relations")}`;
}

/** What the caps of the local graph left out (DA-138); a Node past the depth is not counted. */
export function omittedMessage(nodeCount: number, edgeCount: number): string {
  const omitted = [
    ...(nodeCount > 0 ? [counted(nodeCount, "Node omitido", "Nodes omitidos")] : []),
    ...(edgeCount > 0 ? [counted(edgeCount, "Relation omitida", "Relations omitidas")] : []),
  ];
  return `${omitted.join(" e ")}. Centralize em outro Node para ver mais.`;
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
