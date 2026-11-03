/**
 * The texts of the Home (FE-062). The final copy is E17's; these name the structure (DA-317,
 * DA-272, DA-273).
 */

/** The Home's one heading (DA-317). */
export const HOME_TITLE = "Seu grafo";

/** What the Raiz da Conta is called on the Home's graph, in place of its title (DA-272). */
export const HOME_ROOT_LABEL = "Seu grafo";

/** No Node besides the Raiz da Conta (DA-273). */
export const HOME_EMPTY_MESSAGE = "Seu grafo ainda não tem Nodes.";

/** What the empty state's actions do (DA-273). */
export const HOME_EMPTY_HINT = "Crie um Node ou conecte um agente para começar.";

/** There are Nodes, but none is linked to the Raiz da Conta (DA-312). */
export const HOME_UNLINKED_MESSAGE = "Seus Nodes ainda não estão ligados ao seu grafo.";

/** Where the unlinked state leads: the Nodes the root does not reach (DA-312). */
export const HOME_UNLINKED_HINT = "Em Desancorados, conecte cada Node ao seu grafo.";

/** The entry panel's accessible name (DA-271). */
export const HOME_SHORTCUTS_LABEL = "Atalhos";

export const HOME_INBOX_LINK = "Inbox";
export const HOME_RECENT_LINK = "Recent Changes";
export const HOME_AGENT_CHANGES_LINK = "Agent Changes";

/** The way to the Nodes page, where a Node is created without an anchor (DA-313). */
export const HOME_CREATE_NODE_LINK = "Criar Node";

/** The empty state's first action (DA-273); the Node is created on the Nodes page (DA-313). */
export const HOME_CREATE_FIRST_NODE_LINK = "Criar o primeiro Node";

/** The way to connecting an agent, the MVP's central flow (DA-271, DA-273). */
export const HOME_CONNECT_AGENT_LABEL = "Conectar agente";

/**
 * The Desancorados link: with the count once it is read, without a number while it loads or when
 * it fails (DA-312, DA-314). `shown` is the count as counters show it (99+).
 */
export const unanchoredLinkText = (shown: string | undefined) => (shown === undefined ? "Desancorados" : `Desancorados (${shown})`);
