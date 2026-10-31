# E10 — Raiz da Conta e ancoragem de Nodes

- **Ordem:** 10
- **Depende de:** E2, E5, E6, E9
- **Última revisão:** 2026-10-25

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
  e com o papel certo (nunca o da app). *Emendado pelo DA-201.*

### Decisões da implementação

- **DA-199 — A âncora de lote vai nos ops de `suggest_changes`** (consenso;
  emenda o DA-192).
  - `apply_changes` não recebe operações: só aplica uma proposta guardada. O
    `anchor` entra nos ops `create_node` de `suggest_changes`.
  - Um único validador, no pipeline de escrita, roda três vezes: ao criar a
    proposta, no `apply_changes` e na aprovação da proposta pela pessoa.
  - Por quê: o grafo muda entre a proposta e a aplicação (alvo excluído ou
    oculto). Checar só na proposta deixaria um agente criar Node solto pelo
    caminho da aprovação humana.
- **DA-200 — O que conta como âncora num lote** (consenso; emenda o DA-192).
  - Conta o campo `anchor` ou qualquer Hard Relation do lote entre o Node novo e
    um Node existente visível. O alcance é calculado sobre o grafo real mais o
    lote.
  - O alvo precisa estar ancorado (alcançável a partir da raiz), ou ser um Node
    do mesmo lote que o esteja.
  - `capture_resource` sem `relatedNodeId` fica isento: cai na Inbox, sai com
    `anchored: false` e aparece em Desancorados.
  - Por quê: ancorar num Node solto deixaria a regra só de fachada. Exigir
    `relatedNodeId` quebraria a captura, cujo lugar natural é a Inbox.
- **DA-201 — A raiz nasce no sign-in, por uma entrada System do pipeline**
  (consenso; emenda o DA-198 e o DA-153).
  - Uma entrada System interna e nomeada do pipeline único cria a raiz. Só o
    participante do AccountSetup no sign-in a chama, com o papel da app sob RLS
    (DA-149/DA-158). O papel do provisionador não ganha grants.
  - É idempotente, uma raiz por Account (índice único parcial). Um sign-in
    repetido ou uma corrida não cria duas.
  - Accounts existentes recebem a raiz no próximo sign-in, e o banco de dev é
    purgado (DA-198). Não há backfill por SQL.
  - Por quê: SQL escrito à mão contornaria o pipeline (DA-013), e o papel do
    migrador teria de escrever em tabelas com RLS.
- **DA-202 — Sem raiz, erro `graph.root_missing`, sem criação lazy**
  (**provisório** — decidido pelo arquiteto).
  - Nenhuma leitura nem escrita de agente cria a raiz. Sem raiz, o `get_context`
    sem argumentos e as escritas ancoradas respondem `graph.root_missing`, nunca
    NotFound.
  - Outra visão (analista): criar a raiz na primeira escrita do agente, pela
    mesma entrada System e num ChangeSet separado.
  - Para reverter: o caso de uso de escrita ancorada chama a entrada System, num
    ChangeSet próprio com ator System. Somam-se um teste de corrida e um teste de
    que o Undo do agente não remove a raiz.
  - Por quê: depois do purge, a janela só existe em teoria. Um erro explícito
    mostra a causa e diz que ela se resolve no próximo sign-in.
- **DA-203 — O que mais a raiz recusa** (consenso; completa o DA-189).
  - O Undo do ChangeSet que criou a raiz é recusado com 422. O ChangeSet continua
    visível no histórico.
  - O Type da raiz fica travado: sem troca de Type, conversão ou merge.
  - Corpo, Tags e Collections ficam livres. Remover Relations da raiz também
    (DA-188).
  - Todas as recusas usam o código `graph.account_root_locked`.
    `graph.system_entry_locked` tem outro significado.
  - Por quê: o índice único parcial depende do Type. O resto é conteúdo do
    usuário e não afeta invariante.
- **DA-204 — Limites do Free e ShareVisitor ficam para o E13 e o E11**
  (consenso).
  - Agora entra só o que dá para testar: um predicado único que identifica a
    raiz, e um teste de que o filtro não entrega nada a um ShareVisitor.
  - O E11 ganha a linha "o visitante nunca alcança a raiz". O E13 ganha "a raiz
    fica fora dos limites do Free".
  - Por quê: ainda não há código de limite nem de share. Escrever um agora seria
    especulação.
- **DA-205 — Payloads de agente com `anchored`** (consenso;
  **provisório** quanto aos hits de busca — decidido pelo arquiteto).
  - Levam `anchored`: `get_node`, os Nodes do `get_context`, os candidatos
    ambíguos e os hits de busca. Nas respostas de escrita, só o
    `capture_resource` (que pode dar `false`).
  - Uma consulta de alcance em lote por payload, compartilhada por MCP e REST.
    Um teste garante que o número de consultas não cresce com o número de hits.
  - Campo ausente significa "não calculado", nunca `false`. Isso fica
    documentado no OpenAPI e nas descrições MCP.
  - Outra visão (analista): adiar os hits de busca, o payload de maior volume.
  - Para reverter: tirar `anchored` do hit e do cálculo em lote da busca, e
    trocar o teste de contagem por um teste de que o hit não traz o campo.
- **DA-206 — Contrato REST da raiz e de Desancorados** (consenso).
  - `GET /account-root` devolve `{ id }`.
  - `GET /nodes?unanchored=true` é paginado e segue os filtros existentes. Cada
    item traz `inInbox` e `isAccountRoot`, e a raiz nunca aparece na lista.
  - O contador da Inbox usa o `totalCount` dessa lista (pageSize 1). Não há
    endpoint de contagem separado.
  - A view Desancorados não vai para o MCP: o DA-192 e o `anchored` já cobrem o
    agente.
  - O ownership vale como sempre: Node de outra Account responde 404.
- **DA-207 — `get_context` sem argumentos deixa de ser recusado** (consenso;
  mudança de regra, test-failure-triage categoria B).
  - O teste `A_read_with_invalid_arguments_is_refused_with_the_field` perde a
    asserção de `nodeId`. A checagem de depth > 2 vira um caso separado.
  - Novos testes cobrem "sem argumentos devolve a raiz e o id dela" e "sem raiz
    responde `graph.root_missing`".
  - Regra anterior: `nodeId` ou `subject` obrigatório. Regra nova: sem ambos,
    começa na raiz (DA-194).
- **DA-208 — Interfaces do frontend para raiz, Desancorados e "Conectar a…"**
  (consenso: escolhida pelo arquiteto, validada pelo analista após ajuste).
  - `anchoringData.ts` segue o layout de `collectionsData.ts`. `useAccountRoot()`
    devolve `rootId`, os estados de carga e erro e `isRootMissing`. A chave dele
    fica fora de `graphKeys.all`, com `staleTime` infinito.
  - A lista e o contador de Desancorados ficam sob `graphKeys.all`, e o
    `useGraphWrite` os atualiza depois de cada escrita.
  - O grafo local abre no `?focus=`, depois no último foco, depois na raiz. Um
    foco que responde NotFound cai na raiz.
  - A página `/unanchored` marca os Nodes que também estão na Inbox. O contador
    fica na Inbox.
  - `ConnectToNode({ nodeId, isAccountRoot?, onConnected? })` é um formulário
    inline ao lado do `CreateRelationForm`.
    - A busca só dispara no submit (DA-183).
    - Há um botão explícito "Conectar à Raiz".
    - A Relation sai por `useCreateRelation`/`useGraphWrite`, com kind
      `related_to`, do Node para o alvo.
    - Há um estado para cada caso: carga, vazio, erro de busca, alvo inexistente,
      Relation duplicada e raiz ausente.
  - Por quê: segue o padrão da casa (AddToCollectionMenu, TagPicker, a página
    `/search`). A raiz é um alvo especial que o usuário usa sempre, e merece um
    botão visível em vez de se misturar aos resultados da busca.
- **DA-209 — Os testes existentes contam só as linhas do usuário** (consenso;
  mudança de regra, test-failure-triage categoria B).
  - Regra anterior: uma Account recém-logada tem só as linhas de sistema do
    Resource, nenhum Node e nenhum ChangeSet. Regra nova (DA-189, DA-201): ela
    também tem a raiz, o ChangeSet System dela e o Type "Raiz da Conta".
  - Os testes de escrita, Purge e contagem de Nodes passam a contar só as linhas
    do usuário (`UserRowsOnly` ou `system_key IS NULL`). Assim uma nova linha de
    sistema não os quebra de novo.
  - Os testes da ontologia de sistema e o `list_types` passam a esperar
    explicitamente o Type "Raiz da Conta", de preferência pelo conjunto de
    chaves de sistema. O Type não fica oculto para agentes.
  - O `AccountRootTests` cobre, num só lugar, "exatamente uma raiz e um
    ChangeSet System por Account" e "o Purge de outros Nodes nunca remove a
    raiz".
  - Por quê: esses testes verificam dados do usuário, não a raiz. Esconder o
    Type seria uma regra nova sem DA, e incoerente com o `get_context`, que
    começa na raiz.
- **DA-210 — O histórico mostra o Undo da criação da raiz como indisponível**
  (consenso; completa o DA-203).
  - `UndoUnavailableReason` ganha `AccountRoot` (no fio, `account_root`). O feed
    de ChangeSets informa `unavailable` + `account_root` para o ChangeSet que
    criou a raiz.
  - Um único predicado de raiz serve o `ChangeSetUndo` e o read model, para que
    os dois não divirjam.
  - O web ganha a mensagem em pt-BR desse motivo e o mapeamento de
    `graph.account_root_locked`.
  - Por quê: o estado exibido precisa dizer o que o endpoint vai fazer. Um botão
    que sempre termina em 422 é uma promessa falsa. Mudar o contrato agora é
    barato, porque ainda não há consumidores externos.
- **DA-211 — A resposta neutra do `capture_resource` não leva `anchored`**
  (consenso; mudança de regra, test-failure-triage categoria B; completa o
  DA-154 e o DA-205).
  - Regra anterior: a resposta neutra tem os campos de uma captura nova, menos
    `nodeId` e `changeSetId`. Regra nova: menos os campos sobre o Resource, que
    são `nodeId`, `changeSetId` e `anchored`.
  - A resposta neutra roda uma consulta de alcance descartável, de mesma forma e
    na mesma transação e no mesmo caminho de acesso da real. Assim o tempo das
    duas respostas continua parecido.
  - Por quê: `anchored: false` afirmaria um valor sobre um Resource que a
    resposta não nomeia, e ausente significa "não calculado". O tempo igual é um
    invariante de segurança do DA-154, e a tolerância do teste sozinha é uma
    proteção fraca.
- **DA-212 — A aprovação de proposta explica uma âncora solta** (consenso;
  interface do cliente).
  - `ProposalRefusal` ganha `{ kind: "anchor_required"; operations: number[] }`
    (índices tirados dos detalhes) e `{ kind: "root_missing" }`.
  - Detalhes ausentes ou malformados viram uma lista vazia.
  - A `ProposalPage` mostra uma mensagem em pt-BR e destaca as operações citadas,
    quando a tela as lista. `root_missing` tem mensagem própria, porque conectar
    um alvo não resolve.
  - Por quê: o DA-199 faz a aprovação revalidar as âncoras. Uma recusa que nomeia
    a âncora solta dá à pessoa algo a fazer, e um erro genérico não.

## Checklist

- [x] Type de sistema da Raiz da Conta e Node raiz criado no provisionamento (DA-189, DA-190)
- [x] Criação idempotente da raiz para Accounts existentes (DA-198)
- [x] Raiz protegida: renomear sim; excluir, purgar sozinha ou ocultar para agentes, não (DA-189)
- [x] Raiz fora dos limites do Free e das sugestões semânticas (DA-189)
- [x] Query de alcance (CTE recursiva, não direcionada, só Hard) com teste de desempenho (DA-191)
- [x] `anchor` no caso de uso de criação, atômico no mesmo GraphChangeSet (DA-192, DA-193)
- [x] Recusa por falta de âncora para AgentIdentity, incluindo lotes do `apply_changes` (DA-192)
- [x] `anchor` opcional no REST (DA-196)
- [x] `instructions` do servidor MCP e descrições das ferramentas (DA-194)
- [x] `get_context` sem argumentos começa na raiz (DA-194)
- [x] Flag `anchored` para agentes calculado sobre o grafo real (DA-195)
- [x] View Desancorados com contador na Inbox (DA-197)
- [x] Grafo local abre na raiz (DA-197)
- [x] Ação "Conectar a…" no Node Inspector (DA-197)
- [x] Testes de integração: rollback atômico da âncora, lote com Node solto,
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
