import type { AccountExportOversizedData } from "./accountData";

/** The data that took the archive past the cap, as the person knows it (DA-256). */
export const OVERSIZED_DATA_LABELS: Record<AccountExportOversizedData, string> = {
  property_definitions: "as definições de Properties",
  types: "os Types",
  nodes: "os Nodes",
  relations: "as Relations",
  tags: "as Tags",
  collections: "as Collections",
  change_sets: "o histórico de alterações",
  change_proposals: "as propostas de agentes",
  agent_identities: "as conexões de agentes",
  agent_reads: "o registro de leituras de agentes",
  shared_graph_views: "os links compartilhados",
  node_markdown: "a cópia em Markdown dos Nodes",
};

/** Texts of the account page's export section (DA-255). */
export const EXPORT_MESSAGES = {
  intro:
    "Baixe uma cópia de todos os seus dados: Nodes, Relations, Types, Properties, Tags, Collections, Resources, o histórico de alterações e a proveniência, num arquivo .zip com um JSON e um Markdown por Node. O arquivo fica disponível por até 24 horas.",
  none: "Você ainda não pediu um export.",
  pending: "Preparando o export… Você pode sair desta página; ele continua sendo gerado.",
  sizeCap: (oversizedData: AccountExportOversizedData | null | undefined) =>
    `O export passou do tamanho máximo${
      oversizedData ? ` ao incluir ${OVERSIZED_DATA_LABELS[oversizedData]}` : ""
    } e não foi gerado; nada foi cortado. Pedir de novo agora dará o mesmo resultado: reduza esses dados antes de pedir outro export.`,
  internal: "Não foi possível gerar o export. Tente de novo.",
  expired: "O último export expirou e foi apagado. Peça um novo quando precisar.",
  loadError: "Não foi possível carregar o estado do export.",
  startError: "Não foi possível pedir o export. Tente de novo.",
  downloadError: "Não foi possível baixar o export. Confira o estado abaixo e tente de novo.",
} as const;

/** Texts of the Account deletion (DA-082, DA-255). */
export const DELETION_MESSAGES = {
  intro: "Excluir a conta apaga para sempre todos os seus dados, as conexões de agentes e os links compartilhados.",
  irreversible:
    "Esta ação é imediata e não pode ser desfeita: não há período de carência nem como recuperar os dados depois.",
  externalAgents:
    "O que você já enviou a agentes externos, como ChatGPT ou Claude, está fora do controle do Life Graph e não é apagado por esta ação.",
  exportFirst: "Antes de excluir, você pode baixar uma cópia dos seus dados.",
  confirmationHint: (email: string) => `Digite ${email} para confirmar.`,
  wrongPassword: "Senha incorreta.",
  confirmationMismatch: "O texto digitado não é o e-mail da sua conta.",
  done: "Sua conta foi excluída.",
} as const;
