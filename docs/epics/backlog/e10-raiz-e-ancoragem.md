# E10 — Raiz da Conta e ancoragem de Nodes

- **Ordem:** 10
- **Depende de:** E2, E5, E6, E9
- **Última revisão:** 2026-10-23

## Contexto

Num teste do MCP, o usuário pediu a um agente para cadastrar os 13 ADRs do Life
Graph. O agente criou os 13 Nodes sem nenhuma Relation. Eles ficaram soltos: não
há como chegar a eles navegando pelo grafo. O certo seria o agente achar (ou
criar) o Node "Life Graph" e ligar os ADRs a ele. Faltava também uma âncora
global, do tipo "Raiz da Conta → Life Graph → ADRs".

O MCP já tinha `create_relation`, mas a descrição de `create_node` não pede nada
sobre relacionar o Node novo, e o servidor não manda instruções. Não existia
conceito de raiz: o `RootNode` da spec (§47) é só do SharedGraphView.

O épico entra antes do SharedGraphView (agora E11), do Export (E12) e dos
Entitlements (E13), que precisam conhecer a Raiz da Conta. Por isso o backlog foi
renumerado (E10–E14 → E11–E15).

## Decisões

- **DA-188 — Ancoragem sem invariante rígida no backend** (consenso, aceito pelo
  usuário).
  - O backend não proíbe Nodes desancorados. Uma regra absoluta quebraria a
    Inbox, a captura rápida, o Undo de um GraphChangeSet, o Purge de um Node que
    era a única ponte e a remoção da última Relation.
  - A regra vale por principal: uma AgentIdentity sempre ancora (DA-192); o
    usuário pode criar Nodes soltos, que ficam visíveis na view Desancorados
    (DA-197).
  - Por quê: o sistema não consegue tornar órfãos impossíveis sem bloquear
    fluxos que já existem. Consegue torná-los raros e sempre encontráveis.
- **DA-189 — Raiz da Conta: um Node de sistema por Account** (consenso,
  glossário).
  - Node de um Type de sistema (como `Resource`), um por Account, garantido por
    índice único parcial. É criado no provisionamento da Account, pelo pipeline
    único (DA-013), com Origin `system`.
  - Pode ser renomeado. Não pode ser excluído nem purgado sozinho: a tentativa
    responde erro `graph.*` de validação (422), nunca NotFound, porque o Node
    existe e é do próprio usuário. Some só no Purge da Account.
  - Não pode ser oculto para agentes (o flag fica bloqueado nele e no Type).
    Para restringir um agente, o caminho são os scopes.
  - Nunca entra implicitamente num SharedGraphView: o `RootNode` de um share
    continua uma escolha explícita (§47).
  - Não conta nos limites do Free, fica fora das sugestões semânticas
    (embeddings, Soft Relations) e entra no Export.
  - Por quê: dá um ponto de partida estável para a navegação humana e para o
    `get_context` dos agentes, sem impor árvore.
- **DA-190 — Nome padrão neutro** (usuário). A raiz nasce com um nome neutro
  ("Meu Life Graph"), não com o nome real do dono. A pessoa renomeia quando
  quiser. Por quê: todo agente conectado vê a raiz, e o nome real é dado pessoal.
- **DA-191 — "Ancorado" = alcançável a partir da raiz por Hard Relations,
  ignorando a direção** (consenso, glossário: Desancorado).
  - Qualquer kind de Relation serve. Não há árvore nem pai único: um Node pode
    estar em vários contextos.
  - A direção não conta: inverter uma Relation não pode desancorar uma
    subárvore.
  - Soft Relations não contam: são inferências que o usuário pode ignorar.
  - Grau > 0 não basta: dois Nodes ligados só entre si continuam desancorados.
  - É um estado derivado, calculado na consulta (CTE recursiva) e não gravado.
    Precisa de teste de desempenho, porque o alcance é global e o teto de 1 000
    Nodes do grafo local não vale para ele.
- **DA-192 — O agente sempre ancora, e a falta de âncora é recusada**
  (consenso após contra-argumentação).
  - `create_node` e `apply_changes` ganham `anchor: { node_id, kind? }`. O Node e
    a Relation de âncora entram no mesmo GraphChangeSet, atomicamente.
  - Sem âncora, a chamada é recusada com erro `graph.*` de validação que diz como
    corrigir: "passe `anchor`", com o id da raiz.
  - Ancorar na raiz é permitido, como escolha explícita do agente, nunca como
    padrão silencioso.
  - Num lote, basta que cada Node novo fique alcançável a partir de alguma âncora
    dentro do próprio lote (um Node ancorado e os outros ligados a ele). Se algum
    ficar solto, o lote inteiro é recusado e o erro aponta quais.
  - O alvo de `anchor` passa pelo filtro central: alvo de outra Account ou oculto
    para agentes responde NotFound.
  - A regra mora no caso de uso compartilhado, não no adapter MCP nem no domínio.
  - Por quê: um padrão silencioso para a raiz reproduziria o problema relatado
    (13 ADRs pendurados direto na raiz) e faria "ancorado" deixar de significar
    algo. Um erro com o caminho de correção custa uma nova tentativa
    determinística.
- **DA-193 — Kind padrão da âncora: `related_to`** (consenso após
  contra-argumentação).
  - Quando `anchor.kind` é omitido, a Relation nasce com kind `related_to`, o
    mesmo já usado pelo Accept de Soft Relations (DA-177). A direção é fixa:
    Node novo → alvo.
  - `part_of` e qualquer outro kind continuam permitidos quando escolhidos.
  - Por quê: o padrão vale para qualquer âncora, e `part_of` afirmaria uma
    hierarquia falsa ao ancorar, por exemplo, uma Pessoa ou um Evento.
- **DA-194 — Orientação do agente no MCP** (consenso).
  - O servidor MCP passa a mandar `instructions`: buscar o assunto antes de
    criar, ancorar sob o Node mais específico e criar o Node do assunto quando
    ele não existir.
  - As descrições de `create_node`, `apply_changes` e `suggest_changes` dizem o
    mesmo.
  - `get_context` sem argumentos começa na raiz e devolve o id dela.
- **DA-195 — O estado "ancorado" é calculado sobre o grafo real** (consenso;
  risco aceito pelo usuário).
  - Um Node oculto para agentes pode ser a única ponte entre a raiz e um Node
    visível. O alcance é calculado sem o filtro de leitura, e o agente recebe só
    `anchored: true/false`, e só para Nodes que ele já pode ler.
  - Nunca se expõe caminho, distância nem motivo. A lista de Desancorados vista
    por um agente passa pelo filtro central.
  - **Risco residual aceito pelo usuário:** um "ancorado" sem caminho visível
    revela ao agente que existe algo que ele não enxerga. O risco é de um bit por
    Node, sem identidade, contagem nem conteúdo.
  - Por quê: calcular através do filtro mostraria como solto um Node que está
    ancorado para o humano, e empurraria o agente a criar Relations de
    "conserto" erradas.
- **DA-196 — REST com a mesma âncora, opcional para o usuário** (consenso).
  - O REST de criação de Node aceita `anchor` opcional, com a mesma semântica
    atômica, pelo mesmo caso de uso do MCP.
  - A captura rápida e a Inbox continuam sem âncora: ancorar por padrão
    destruiria o sinal de "ainda não triado".
- **DA-197 — UI: grafo abre na raiz, view Desancorados e "Conectar a…"**
  (consenso).
  - O grafo local abre centrado na raiz, ou no último foco do usuário quando
    houver.
  - "Desancorados" é uma View de sistema (uma query), com contador na Inbox. Ela
    distingue os Nodes que também estão na Inbox.
  - O Node Inspector ganha a ação "Conectar a…", com a raiz como atalho de um
    clique. Ela usa o mesmo caso de uso de criar Relation.
  - O texto da tela pode ser mais amigável ("Nós soltos"); o termo do código e da
    API é Unanchored/Desancorado.
- **DA-198 — Nada de migração para os Nodes desancorados atuais** (usuário).
  Só há dados de dev; se for preciso, o banco é purgado. A criação da Raiz da
  Conta para as Accounts existentes continua: idempotente, por Account, sob RLS
  e com o papel certo (nunca o da app).

## Checklist

- [ ] Type de sistema da Raiz da Conta e Node raiz criado no provisionamento (DA-189, DA-190)
- [ ] Criação idempotente da raiz para Accounts existentes (DA-198)
- [ ] Raiz protegida: renomear sim; excluir, purgar sozinha ou ocultar para agentes, não (DA-189)
- [ ] Raiz fora dos limites do Free e das sugestões semânticas (DA-189)
- [ ] Query de alcance (CTE recursiva, não direcionada, só Hard) com teste de desempenho (DA-191)
- [ ] `anchor` no caso de uso de criação, atômico no mesmo GraphChangeSet (DA-192, DA-193)
- [ ] Recusa por falta de âncora para AgentIdentity, incluindo lotes do `apply_changes` (DA-192)
- [ ] `anchor` opcional no REST (DA-196)
- [ ] `instructions` do servidor MCP e descrições das ferramentas (DA-194)
- [ ] `get_context` sem argumentos começa na raiz (DA-194)
- [ ] Flag `anchored` para agentes calculado sobre o grafo real (DA-195)
- [ ] View Desancorados com contador na Inbox (DA-197)
- [ ] Grafo local abre na raiz (DA-197)
- [ ] Ação "Conectar a…" no Node Inspector (DA-197)
- [ ] Testes de integração: rollback atômico da âncora, lote com Node solto,
      alvo de outra Account ou oculto (NotFound), ponte oculta sem id oculto em
      nenhum payload, raiz nunca alcançável por um visitante de share

## Critérios de saída

Pedir a um agente para cadastrar os ADRs do Life Graph produz um Node "Life
Graph" ancorado na raiz e os ADRs ancorados nele, num GraphChangeSet que pode ser
desfeito. Um `create_node` sem âncora é recusado com um erro que diz como
corrigir. Um Node criado solto pelo usuário aparece em Desancorados e se liga com
um clique.

## Fora de escopo

Invariante rígida de conectividade, árvore ou pai único, reorganização automática
do grafo, sugestão de pai por IA (fica para as Soft Relations do E9) e migração
dos Nodes desancorados que já existem.
