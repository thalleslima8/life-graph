import type { AgentRef, ChangeSetDetail, FieldChange, ProposalItem, ProposalRefusal, ProposalStatus, ProposedOperation } from "./agentChanges";

/** The visible texts of Agent Changes and the GraphChangeSet screen (FE-062: one place). */

export const PROPOSAL_STATUS_LABELS: Readonly<Record<ProposalStatus, string>> = {
  proposed: "Aguardando você",
  applied: "Aplicada",
  rejected: "Recusada",
  expired: "Expirada",
  withdrawn: "Retirada",
};

const REASON_LABELS: Readonly<Record<ProposalItem["reasons"][number], string>> = {
  suggested: "O agente sugeriu",
  approval_required: "Você exige aprovação deste agente",
  sensitive: "Ação sensível",
  type_change: "Troca de Type de um Node seu",
  bulk: "Muitas alterações de uma vez",
  window: "Muitas alterações em pouco tempo",
};

export function reasonLabels(reasons: ProposalItem["reasons"]): string {
  return reasons.map((reason) => REASON_LABELS[reason]).join(" · ");
}

export const REMOVED_AGENT_LABEL = "Agente removido";

/** Why a GraphChangeSet can no longer be undone, before the person clicks (DA-135). */
export const UNAVAILABLE_UNDO_LABELS: Readonly<Record<NonNullable<ChangeSetDetail["undo"]["reason"]>, string>> = {
  window_closed: "Não dá mais para desfazer: o prazo terminou e o conteúdo foi removido de vez.",
  content_purged: "Não dá mais para desfazer: parte do conteúdo foi removida por purge.",
};

/** The agent as the person knows it; the name is the person's own label, never trusted for anything else. */
export function agentLabel(agent: AgentRef | null | undefined): string {
  if (!agent?.name) {
    return REMOVED_AGENT_LABEL;
  }

  return agent.revoked ? `${agent.name} (revogado)` : agent.name;
}

const quoted = (text: string | null | undefined, fallback: string) => (text ? `«${text}»` : fallback);

function endpointLabel(endpoint: ProposedOperation["source"]): string {
  if (!endpoint) {
    return "(desconhecido)";
  }

  if (endpoint.ref) {
    return `o novo Node «${endpoint.ref}»`;
  }

  return quoted(endpoint.title, "um Node indisponível");
}

export function operationTitle(operation: ProposedOperation): string {
  switch (operation.op) {
    case "create_node": {
      const title = operation.fields.find((field) => field.field === "title")?.after;
      return `Criar Node ${quoted(typeof title === "string" ? title : null, "sem título")}`;
    }
    case "update_node":
      return `Alterar Node ${quoted(operation.nodeTitle, "indisponível")}`;
    case "create_relation":
      return `Relacionar ${endpointLabel(operation.source)} → ${endpointLabel(operation.target)}${operation.kind ? ` (${operation.kind})` : ""}`;
    case "remove_relation":
      return "Remover uma Relation";
    case "delete_node":
      return `Apagar Node ${quoted(operation.nodeTitle, "indisponível")}`;
    case "capture_resource": {
      const title = operation.fields.find((field) => field.field === "title")?.after;
      const capture = `Capturar Resource ${quoted(typeof title === "string" ? title : null, "sem título")}`;
      return operation.target ? `${capture} → ${endpointLabel(operation.target)}${operation.kind ? ` (${operation.kind})` : ""}` : capture;
    }
    case "tag_node":
      return `Aplicar Tag ${quoted(operation.tag, "sem nome")} em Node ${quoted(operation.nodeTitle, "indisponível")}`;
    case "untag_node":
      return `Remover Tag ${quoted(operation.tag, "sem nome")} de Node ${quoted(operation.nodeTitle, "indisponível")}`;
  }
}

const FIELD_LABELS: Readonly<Record<string, string>> = {
  title: "Título",
  body: "Corpo",
  typeId: "Type",
  hiddenFromAgents: "Oculto para agentes",
  inInbox: "Na Inbox",
  suggestedType: "Type sugerido",
  kind: "Tipo da Relation",
  url: "Link",
  name: "Nome",
};

export function fieldLabel(field: string): string {
  if (field.startsWith("properties.")) {
    return "Propriedade";
  }

  return FIELD_LABELS[field] ?? field;
}

export function fieldValue(value: FieldChange["before"]): string {
  if (value === null || value === undefined || value === "") {
    return "(vazio)";
  }

  if (typeof value === "string") {
    return value;
  }

  if (typeof value === "boolean") {
    return value ? "Sim" : "Não";
  }

  return JSON.stringify(value);
}

const operationList = (operations: number[]) => operations.map((index) => index + 1).join(", ");

export function refusalMessage(refusal: ProposalRefusal): string {
  switch (refusal.kind) {
    case "not_pending":
      return "Esta proposta já foi decidida ou expirou. Nada foi aplicado.";
    case "not_found":
      return "Esta proposta não está mais disponível.";
    case "version_conflict":
      return refusal.operations.length > 0
        ? `Nada foi aplicado: o Node da operação ${operationList(refusal.operations)} mudou depois que o agente o leu.`
        : "Nada foi aplicado: um Node mudou depois que o agente o leu.";
    case "dependency":
      return `Nada foi aplicado: a operação ${operationList(refusal.operations)} usa um Node novo que ficou de fora. Marque também a operação de que ela depende.`;
  }
}
