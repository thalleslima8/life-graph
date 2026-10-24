# E5 — Escrita via MCP + Agent Changes

- **Ordem:** 5
- **Depende de:** E4
- **Última revisão:** 2026-10-23

## Pendências do `/flow` de 2026-10-04 (contrato de erro)

Resolvidas no `/flow` de 2026-10-08, antes de iniciar o épico: a pré-decisão
Q9 virou a DA-130, e a Q4b virou a DA-131. As duas dependem do "Contrato de
resultado e erro" do [E2](../done/e02-nucleo-de-escrita.md) e da DA-099.

## Contexto

Com este épico, o fluxo da v2 §63 funciona de ponta a ponta: o agente cria ou
atualiza Nodes, gera um GraphChangeSet, a UI atualiza e o usuário inspeciona ou
desfaz. A v2 troca o "IA propõe, usuário decide" do v1 por "resultado visível
e automação reversível".

## Decisões

- **DA-040 — Agentes escrevem direto no nível Write** (usuário). Criar, editar
  (inclusive conteúdo escrito pelo usuário) e remover relações, inclusive as
  criadas pelo usuário, são aplicados na hora como ChangeSet que pode ser
  desfeito. Remover uma relação é menos destrutivo que sobrescrever texto, por
  isso não faz sentido protegê-la mais.
  ADR: [docs/adr/0009-agentes-escrevem-direto-e-aplicam-propostas.md](../../adr/0009-agentes-escrevem-direto-e-aplicam-propostas.md)
- **DA-041 — O nível Sensitive é sempre Proposed e só é aprovado pelo usuário
  na UI** (consenso). O nível cobre delete, bulk, share e permissões. Defende
  contra prompt injection que leve um agente a apagar ou compartilhar dados.
- **DA-042 — Mudar o Type de um Node do usuário é Proposed** (consenso). Isso
  pode inverter a herança de "oculto para agentes", o que na prática é mudar
  permissões, e mexe nos valores. Um agente pode definir o Type de um Node
  criado por ele mesmo. Ele nunca atribui um Type oculto, nunca edita
  definições de Type, nunca mexe na flag de oculto, nunca mexe na flag da Inbox
  e nunca cria shares.
- **DA-043 — `apply_changes` é uma ferramenta MCP** (usuário), com estas
  proteções, todas checadas de novo no momento de aplicar (consenso):
  1. a proposta é da mesma AgentIdentity (de outra identidade, 404);
  2. o status é Proposed e não expirou;
  3. não contém nenhuma operação Sensitive;
  4. uma proposta criada com o toggle ligado fica marcada como `requires_user`
     para sempre;
  5. a versão base confere (senão, conflito);
  6. autorização, scopes e filtro de ocultos rodam de novo;
  7. chave de idempotência;
  8. conteúdo idêntico a uma proposta rejeitada não pode ser reenviado.
  ADR: [docs/adr/0009-agentes-escrevem-direto-e-aplicam-propostas.md](../../adr/0009-agentes-escrevem-direto-e-aplicam-propostas.md)
- **DA-044 — Toggle "exigir aprovação para escritas" por AgentIdentity, padrão
  desligado** (usuário). Mantém o caminho simples da §63: se o usuário deixa o
  agente sobrescrever, a escolha é dele. O toggle aparece na tela de
  consentimento.
- **DA-045 — Limite rígido de 50 operações por ChangeSet**, recusado com erro
  de validação acima disso (usuário, seguindo o Arquiteto). Subir o limite
  depois não quebra nenhum agente; baixar quebraria. Aumentar se a telemetria
  mostrar divisões frequentes. A definição de "operação" (criar, atualizar ou
  apagar Node, Relation, vínculo de Tag ou valor) fica neste épico.
- **DA-046 — Bulk: mais de 20 operações que alteram ou removem entidades do
  usuário num ChangeSet exigem aprovação** (consenso). Criações do próprio
  agente não contam, para não travar capturas legítimas.
- **DA-047 — Janela deslizante contra divisão de lotes: obrigatória no MVP**
  (consenso). Um agente com defeito, ou manipulado por conteúdo capturado,
  poderia contornar o DA-046 mandando vários ChangeSets de 19 operações.
  Acima do limite, os ChangeSets viram Proposed. Números e unidade de contagem
  na DA-132.
- **DA-048 — Propostas expiram em 7 dias** (consenso). O usuário costuma revisar
  no dia seguinte ou no fim de semana. Propostas expiradas ganham o status
  `Expired`, continuam visíveis e ficam sujeitas à redação no purge.
- **DA-049 — Node sem Type criado por agente vai para a Inbox, com uma dica
  `suggested_type`** (usuário + consenso). Nenhum Type é criado. Node sem Type
  aceita título, corpo e relações, mas não propriedades. Types propostos por
  agentes ficam para depois do MVP.
- **DA-050 — `update_node` exige `expected_version`** (consenso). Sem isso, o
  agente sobrescreveria uma edição concorrente do usuário.
- **DA-051 — Rate limits de segurança por AgentIdentity**, por exemplo 60
  escritas/min mais um teto diário (consenso). São limites de segurança, não
  de plano, e não esperam o E13.

- **DA-130 — Ação Sensitive pedida por agente é sucesso pendente, com dedupe,
  consulta de status e teto de pendentes** (consenso; revalida a pré-decisão
  Q9, que segue compatível com a DA-099..DA-106).
  - O caso de uso devolve `Result<ChangeOutcome>`, com `Applied(changeSetId)`
    ou `PendingApproval(proposalId)`. Pendente é sucesso, não `Error`.
  - O REST responde `202 Accepted` com `Location` para a proposta. Antes de
    implementar, conferir se o `IHttpResultResponder` do limaj 3.0.0 aceita 202
    com `Location`. Se não aceitar, a lacuna vai para o owner do limaj
    (DA-099), sem substituto local.
  - O MCP devolve um resultado normal de ferramenta, não erro, com status
    pendente, o id da proposta e as orientações "nada foi aplicado", "não tente
    de novo" e "avise o usuário onde aprovar". Um erro induziria retry, com
    propostas duplicadas, ou um "falhou" para o usuário.
  - **Dedupe:** um pedido repetido com o mesmo conjunto canônico de operações e
    as mesmas versões base, da mesma AgentIdentity, devolve a proposta
    `Proposed` não expirada que já existe, com `deduplicated: true`. É o mesmo
    hash do DA-043 item 8. A chave de idempotência sozinha não basta, porque o
    agente pode gerar uma chave nova a cada tentativa. O dedupe roda sob
    advisory lock por AgentIdentity.
  - **`get_proposal(id)`:** ferramenta Safe (`lifegraph.read`), só para as
    propostas da própria AgentIdentity (das outras, NotFound). Devolve só o
    status (`proposed`, `applied`, `rejected`, `expired`, `withdrawn`), as
    datas e o `changeSetId` quando aplicada, nunca o conteúdo: um alvo pode ter
    ficado oculto depois. Sem ela, o agente não fecha o ciclo da §63.
  - **Teto de 10 propostas pendentes por AgentIdentity**, configurável. É limite
    de segurança, não de plano. O dedupe não pega variações, e uma fila cheia
    cansa o usuário até ele aprovar sem ler, o que anula a DA-041. Ocupa vaga
    tudo que vira Proposed (Sensitive, `requires_user`, bulk, janela), exceto o
    pedido deduplicado. Aprovar, rejeitar, expirar ou retirar libera a vaga. No
    teto, o pedido é recusado com `agents.proposal_queue_full` (Validation,
    422), e no MCP com "nada foi aplicado; não tente de novo; peça ao usuário
    para revisar Agent Changes". Aqui o erro é o comportamento certo, porque o
    objetivo é parar o agente. A proposta mais antiga nunca é descartada para
    abrir vaga. A contagem roda no mesmo advisory lock do dedupe, e criar
    proposta conta no rate limit da DA-051.
  - Candidata a ADR.
- **DA-131 — Todo scope é checado antes de buscar o alvo** (consenso; resolve a
  antiga Q4b). Isso vale também para `.delete` e `.share`. Sem o scope, a
  resposta é 403 (`insufficient_scope`) e é idêntica para alvo visível,
  oculto, de outra conta ou inexistente. O scope depende só do principal e da
  operação, que são conhecidos antes de qualquer leitura. Uma regra única tem
  menos combinações para testar e não cria oráculo de existência. Num lote
  (`apply_changes`), o conjunto de scopes vem da lista de operações, antes de
  carregar qualquer alvo. Um teste de integração cobre as quatro combinações.
- **DA-132 — Janela da DA-047: 20 operações por AgentIdentity e 40 pela Account
  a cada 10 minutos, deslizante** (consenso na estrutura; o teto da Account é
  **provisório**, decidido pelo analista).
  - A unidade é a operação sobre entidade do usuário (DA-133), não o
    ChangeSet. Contar ChangeSets deixaria o fracionamento passar.
  - A fonte é a soma das operações dos ChangeSets já aplicados pelo agente, sem
    contador paralelo, na mesma transação e sob advisory lock por Account.
  - O ChangeSet guarda, no momento da aplicação, quantas operações tocaram
    entidades do usuário. Assim a regra de posse não muda retroativamente o que
    já foi consumido.
  - Só consome a escrita autônoma do agente. Propostas não consomem, nem depois
    de aprovadas, porque passaram pela decisão humana. O ChangeSet de Undo é do
    humano e não consome. Um ChangeSet desfeito continua contando, para que um
    agente manipulado não lave a janela alternando escrita e Undo.
  - O ChangeSet que cruzaria qualquer dos dois limites vai inteiro para
    Proposed, com a mesma resposta `PendingApproval` da DA-130. Não há
    aplicação parcial.
  - Remover Relation e alterar ou remover valor de Property ou vínculo de Tag
    são Write (DA-040) e consomem a janela. Apagar Node é Sensitive (DA-041)
    e nunca chega a consumir.
  - Os números ficam em configuração, separados dos limites de plano e do rate
    limit duro (429) da DA-051.
  - Provisório: o teto da Account. O analista ficou com 40 (2× o limite por
    agente): a exposição a uma injeção lida por vários agentes fica limitada ao
    dobro, não importa quantas conexões existam. A outra visão, do arquiteto,
    era 50, para casar com o limite da DA-045 e folgar dois agentes legítimos.
    Para reverter, basta trocar o padrão de configuração de 40 para 50.
- **DA-133 — "Criação própria do agente" é monotônica e tem um único predicado**
  (consenso).
  - Uma entidade (Node ou Relation) é criação própria de uma AgentIdentity só
    enquanto essa identidade a criou e nenhum outro principal (humano ou outro
    agente) escreveu nela depois.
  - Depois da primeira escrita de outro principal, a entidade é do usuário
    para sempre. Desfazer essa escrita não devolve a posse. O humano investiu
    nela, e uma regra reversível é difícil de explicar e de testar.
  - Um único predicado (`IsAgentOwned`) na política central de escrita serve
    à DA-042, à DA-046 e à DA-132.
  - Implementação: uma coluna anulável `created_by_agent_identity_id` em Node e
    Relation, preenchida na criação por agente e zerada pelo pipeline único
    (DA-013) na primeira escrita de outro principal. Isso evita varrer a
    Provenance no caminho quente.
  - Candidata a ADR.

- **DA-134 — Propor delete exige só `lifegraph.write`** (consenso, Q33;
  esclarece a DA-131).
  - O `suggest_changes` aceita `delete_node` sob `lifegraph.write`. A proposta
    é sempre Sensitive e `requires_user` (DA-041): o agente não executa nada,
    e a aprovação humana em Agent Changes é o controle real.
  - `.delete` e `.share` valem para ferramentas que executam direto. Nenhuma
    existe no MVP, e o Restore da DA-115 não tem ferramenta para agentes.
    Propostas são checadas contra `.write`, também antes de buscar o alvo
    (DA-131).
  - Propor delete de um Node oculto, de outra conta ou inexistente responde
    `NotFound` igual.
  - O texto do toggle de escrita na tela de consentimento avisa que o agente
    "pode propor exclusões, que só você aprova".
  - Fica em aberto para quando `lifegraph.delete` for oferecido. O arquiteto
    quer manter a regra para sempre: scope é sobre execução, e exigir `.delete`
    para propor forçaria todo agente conectado a reconsentir. O analista quer
    passar a exigir `.delete` para propor, com step-up (DA-122), anunciado aos
    agentes. Não bloqueia o E5. Candidata a ADR.
- **DA-135 — Interface do frontend de Agent Changes, GraphChangeSet e do
  toggle** (consenso, Q32, via `design-an-interface`: três desenhos; o
  arquiteto escolheu o C com ajustes, e o analista validou depois de um
  contra-argumento). Segue a forma da DA-118 e da DA-125.
  - Em `web/src/features/agents/agentChanges.ts`:
    - `FeedFilters = {agentIdentityId?, unreviewed, status?}`;
    - `useFeedFilters()`: estado na URL (FE-012), validado por Zod; valor
      inválido cai no padrão;
    - `agentChangeKeys`;
    - `proposalsQuery(f)` e `agentChangeSetsQuery(f)`, infinitas, com os filtros
      passados como argumento e sem depender do Router;
    - `proposalQuery(id)` e `changeSetQuery(id)`;
    - `useApproveProposal(id)` (`{operations?}` ou nada = tudo),
      `useRejectProposal(id)` e `useMarkReviewed(changeSetId)`;
    - `ProposalRefusal = not_pending | version_conflict{operations} |
      dependency{operations} | not_found` e `toProposalRefusal`;
    - `toApproval(selected, total)`: devolve `null` quando `total = 0` ou a
      seleção está vazia, `undefined` quando tudo está marcado e senão os
      índices em ordem. Lista vazia nunca é enviada.
  - **Duas listas, não um feed unificado:** propostas, e ChangeSets de agente.
    Juntar dois cursores no cliente é frágil. Uma proposta aplicada aparece no
    feed de ChangeSets.
  - `ChangeDiff` em `web/src/features/agents/ChangeDiff.tsx` é
    apresentacional e nunca busca dados. Props:
    - `source`: `{kind: "proposal", detail} | {kind: "changeset", detail}`;
    - `selection?`: controlada pela página, só para proposta pendente;
    - `highlight?`: operações em conflito, anunciadas por texto.

    Ele cuida da acessibilidade: seção e título por operação, `<dl>`
    Antes/Depois, checkbox com rótulo e o aviso "sobrescreveu um valor seu"
    com ícone e texto, nunca só cor. A página cuida das ações, do foco e do
    `aria-live`.
  - No `agentIdentities.ts` (DA-125): `AgentIdentityView.requiresApproval` e
    `useUpdateAgentIdentity(id)` com `{name?, requiresApproval?}`.
    `useRenameAgentIdentity` vira um wrapper fino. `useUndoChangeSet` passa a
    aceitar `Pick<ChangeSetItem, "id" | "entries">`.
  - **Contrato REST que a interface exige:**
    - `ProposalDetail` ganha `contentPurged` (o diff mostra "conteúdo removido
      por purge" e Aprovar fica desabilitado), `agent: AgentRef` e, por
      operação, `dependsOn: number[]` (os `create_node` cujo ref ela usa). A
      seleção cascateia na UI: desmarcar um X desmarca o que depende dele.
    - `ChangeSetDetail` ganha `agent: AgentRef | null` (null quando o ator é
      humano) e
      `undo: available | undone{byChangeSetId} | unavailable{window_closed |
      content_purged}`. O conflito com uma edição posterior continua como
      recusa depois do clique.
    - `AgentRef = {id | null, name | null, revoked}`. A tela mostra
      "Agente removido" ou revogado.
    - O `overwritesYourValue` já vem também no ChangeSet aplicado direto, que
      é o caminho padrão com o toggle desligado.
  - **Regras de domínio que entram junto:**
    - A aprovação parcial com uma referência órfã (`create_relation X→Y` sem
      o `create_node X`) é recusada com `agents.operation_dependency` (422,
      com os índices) e não aplica nada. O servidor é a autoridade, e hoje esse
      caso viraria exceção.
    - O ChangeSet nascido da aprovação da pessoa já sai revisado
      (`ReviewedAt` gravado na mesma transação), porque aprovar já é revisar.
    - As operações desmarcadas numa aprovação parcial não entram no hash da
      DA-043 item 8, que vale só para a proposta rejeitada inteira. O reenvio
      insistente fica contido pelo teto de pendentes e pela janela.
    - Revogar uma AgentIdentity retira (`withdrawn`) as propostas pendentes
      dela, na mesma transação: a pessoa não aprova conteúdo de uma conexão em
      que deixou de confiar. Candidata a ADR.
- **DA-136 — Proposta de agente é uma ChangeProposal, separada do
  GraphChangeSet** (registro de implementação, revisão gate 3).
  - O GraphChangeSet só existe aplicado (estados Applied e Reverted). O que o
    agente pede e ainda espera decisão é uma ChangeProposal, com estados
    próprios: Proposed, Applied, Rejected, Expired e Withdrawn.
  - Por quê: uma proposta guarda operações, não alterações gravadas. O que ela
    muda só se sabe ao aplicar, contra o grafo daquele momento (versões,
    Nodes ocultos depois, Purge). Um GraphChangeSet com entradas antes de
    aplicar teria `before`/`after` falsos e quebraria o Undo e os feeds, que
    leem só o que de fato mudou (DA-013, DA-020).
  - Withdrawn cobre a saída sem decisão da pessoa: o agente retira, a conexão
    é revogada (DA-135) ou a Purge apaga o que ela nomeava (DA-048).
  - Aplicada, a ChangeProposal aponta para o GraphChangeSet que gerou. O
    GLOSSARY.md registra os dois termos.

## Checklist

- [x] Pré-requisito do E2 (DA-115): comando Restore por entidade, com ChangeSet compensatório próprio e conflito checado só na entidade e nas Relations em cascata (`cascade_of`)
- [x] Ferramentas: `create_node`, `update_node`, `create_relation`, `remove_relation`, `suggest_changes`, `apply_changes`, retirar a própria proposta
- [x] Política central de escrita de agentes (Write imediato, Sensitive/requires_user/bulk viram Proposed)
- [x] Proteções do `apply_changes` (DA-043)
- [x] Toggle por AgentIdentity (padrão off) na tela de consentimento e em Agentes conectados
- [x] Limite de 50, bulk de 20, janela deslizante (definir parâmetros), rate limits
- [x] Job de expiração de propostas (7 dias)
- [x] Tela **Agent Changes**: feed por agente, "não revisado", diff campo a campo com aviso "sobrescreveu um valor seu", aprovar/rejeitar total ou parcial, Undo
- [x] Tela de GraphChangeSet (v2 §66)
- [x] Ligação com a Inbox para Nodes sem Type de agente
- [x] Aceite manual: a partir de um agente real, "guarde isso e relacione com X" gera Nodes e Relations visíveis em Agent Changes, e a pessoa desfaz pela tela de GraphChangeSet
  - Feito em 2026-10-21 com o Claude Code no host
    (`docs/analyses-tests/relatorio-teste-mcp-life-graph.md`). O pedido foi
    criar «Linha Norte» e relacioná-la com o Node existente Ferrovias. Isso
    gerou uma proposta única com 2 operações: o Node, com `ref`, e a Relation
    `related_to` com o id de Ferrovias. A proposta ficou aguardando em Agent
    Changes, com o motivo "Você exige aprovação deste agente". A pessoa
    aprovou (ChangeSet `01a15f21-ddc8-7abc-9f93-68919f50f49c`) e desfez pela
    tela do GraphChangeSet. Em seguida, `search_graph("Linha Norte")` voltou
    vazio, e Ferrovias ficou com as 2 relações de antes. Uma tentativa
    anterior, só com `create_node`, foi recusada pela pessoa e não deixou
    duplicado.
  - Depois do Undo, o `get_proposal` continua `applied`. Isso é esperado
    (DA-136): o desfazer mora no GraphChangeSet (Reverted), não na
    ChangeProposal.
  - A mesma prova no Claude.ai ou no ChatGPT, pelo domínio, foi para o E14
    (DA-147).

### Testes obrigatórios
- [x] Um teste por proteção do `apply_changes`
- [x] Limites 50/51 e 20/21; janela no limite, expirando, com dois agentes
- [x] Proposta criada com o toggle ligado não destrava ao desligá-lo
- [x] Token de agente chamando a aprovação de Sensitive é recusado

### Notas de implementação

- **Restore por entidade (DA-115):** `IGraphWriter.RestoreAsync` e as rotas
  `POST /api/nodes/{id}/restoration` e `POST /api/relations/{id}/restoration`
  (só a pessoa). Acha a entrada de Delete mais nova cujo estado gravado ainda é
  o atual; volta o Node ao `before` dela e as Relations em cascata (`cascade_of`)
  cuja outra ponta está viva, num ChangeSet próprio, sem `reverts_changeset_id`
  e sem marcar o ChangeSet do Delete. Entidade viva não muda nada; fora da
  janela, `graph.undo_window_closed`; depois do purge ou de outra Account,
  NotFound. Não há ferramenta de agente para restaurar (Sensitive).
- **Proposta = `change_proposals`:** a proposta guarda operações, não entradas
  de antes/depois, porque o efeito só se conhece ao aplicar. Estados `proposed`,
  `applied`, `rejected`, `expired`, `withdrawn`. Aplicar gera um GraphChangeSet
  comum; a proposta aponta para ele (`changeset_id`), sem ligação de volta, para
  não haver ciclo de chaves. Uma proposta aplicada sem mudança fica `applied`
  sem ChangeSet. O feed mostra o `proposalId` pela proposta.
- **Operação (DA-045):** cada item do pedido é uma operação (`create_node`,
  `update_node`, `create_relation`, `remove_relation`, `delete_node`), não
  importa quantos campos ele mude. Só `update_node`, `remove_relation` e
  `delete_node` sobre entidade do usuário (DA-133) contam para o bulk e a janela.
- **Política (`AgentWritePolicy`):** `requires_user` quando há delete,
  troca de Type de Node do usuário, bulk (> 20), toggle ligado ou janela. Só a
  proposta pedida pelo agente (`suggest_changes`) sem nenhum desses motivos pode
  ser aplicada por ele (`apply_changes`). A janela só entra em escrita
  autônoma (ferramentas diretas e `apply_changes`); aprovação da pessoa grava
  `window_operations = 0`. Tudo roda sob `pg_advisory_xact_lock` por Account.
- **Lock do dedupe (DA-130, DA-132):** a DA-130 pede o dedupe e o teto de
  propostas pendentes sob advisory lock por AgentIdentity. O código usa, em
  toda escrita de agente, o mesmo lock por Account da janela
  (`AgentGraphWriter.LockAsync`). Esse lock é mais forte, porque serializa
  também as AgentIdentities da mesma Account, e serve às duas DAs com um único
  lock, sem ordem de aquisição entre dois.
- **Scopes de um lote (DA-131, DA-134):** no MVP, o conjunto de scopes de
  qualquer lote é `{lifegraph.write}`. Propor `delete_node` exige só `.write`
  (DA-134), e nenhuma ferramenta do MVP exige `.delete` ou `.share`
  (`AgentAccess.RequiredScopeOf` só devolve `.read` ou `.write`). Assim, a
  checagem por ferramenta do `ToolScopes`, feita na mensagem JSON-RPC antes do
  SDK, já é a checagem do lote inteiro, antes de carregar qualquer alvo, tanto
  no `suggest_changes` quanto no `apply_changes`. Uma ferramenta futura que
  exija `.delete` ou `.share` precisa derivar o conjunto da lista de operações.
- **Visibilidade nas escritas (DA-035):** os alvos passam pelo
  `GraphReadFilter` do agente; oculto ou de outra Account é NotFound, também no
  `apply_changes`. Agente nunca recebe Type oculto.
- **Rejeição (DA-043 item 8):** o hash canônico (SHA-256 das operações com
  valores ordenados por id e versões base) é comparado com propostas rejeitadas
  na Account inteira, na criação e na aplicação.
- **Toggle ligado depois:** `apply_changes` também recusa quando o toggle está
  ligado agora, e marca a proposta `requires_user` para sempre.
- **`apply_changes` de proposta que já espera a pessoa (DA-043 itens 3 e 4,
  DA-130):** quando a proposta tem delete (Sensitive), já está marcada
  `requires_user` ou o toggle está ligado agora, a resposta é o erro
  `agents.proposal_requires_user` (regra de negócio, `AskUser`, como o
  `agents.proposal_queue_full`), não `pending_approval`. A DA-130 trata como
  sucesso pendente o pedido que **cria** a proposta. Aqui a proposta já existe
  e já está pendente, então não há resultado novo a devolver, e a mensagem diz
  ao agente que nada foi aplicado e que não tente de novo. A chave de
  idempotência não fica guardada nesse caso. O motivo que só aparece na
  aplicação (janela, bulk, troca de Type) segue a nota de idempotência: marca a
  proposta e responde `pending_approval`.
- **Quem lê o toggle (DA-044):** o caso de uso do Graph (`AgentGraphWriter`), na
  transação do pedido, pelo `IAgentIdentityDirectory` do Accounts. Nenhum
  adapter passa o toggle, então nenhum adapter futuro desliga a proteção. Uma
  conexão revogada depois de autenticado o pedido responde `unauthorized` em
  toda ferramenta de escrita, inclusive ao retirar a própria proposta.
- **Idempotência (DA-043 item 7):** a chave repete a primeira resposta. Se o
  agente aplicou, a resposta é refeita a partir do GraphChangeSet, com os
  Nodes novos por `ref` e as versões, e sem ChangeSet quando nada mudou. Ela
  passa pelo filtro central: um Node oculto para agentes depois da primeira
  resposta, e a Relation que o toca, saem dela (DA-035); um Node apagado
  depois continua, como a primeira resposta o nomeou. Se o
  `apply_changes` achou que a proposta precisava da pessoa (janela, bulk, troca
  de Type), a chave fica guardada e a resposta é `pending_approval`, mesmo
  depois da decisão da pessoa. Outra chave recebe a recusa.
- **Leitura de ChangeProposal e do histórico:** pelo filtro central
  (`GraphReadFilter.Proposals` e `ChangeSets`). A pessoa vê todas as da
  Account, a AgentIdentity só as suas propostas (e nenhum GraphChangeSet) e
  qualquer outro principal nada. Toda leitura de ChangeProposal ou do
  histórico no E5 passa por ele, exceto estas, cada uma pelo seu motivo:
  - a checagem de conteúdo rejeitado (DA-043 item 8) e a soma da janela
    (DA-132): contagens na Account inteira, que não devolvem proposta nem
    GraphChangeSet a ninguém;
  - o "sobrescreveu um valor seu": SQL com uma entrada por pergunta (DB-023),
    só para a pessoa (o caso de uso recusa outro principal), para quem o
    filtro já é a Account inteira;
  - a retirada na revogação, a expiração e o Purge: efeitos de sistema do lado
    da escrita, sem principal que leia (os jobs) ou depois da checagem de
    Human da revogação, e nada volta a quem chamou;
  - a resposta repetida da idempotência lê as entradas do GraphChangeSet do
    próprio agente, que o filtro guarda dos agentes como histórico, e devolve
    só o que o filtro ainda deixa o agente ver (`NodesWithTombstones` e
    `RelationsWithTombstones`).
- **Toggle na reconexão (DA-044):** a tela de consentimento mostra o toggle
  como a conexão existente está (marcado se ligado); desmarcar ali é escolha
  explícita da pessoa. Sem `lifegraph.write` oferecido, o toggle não aparece e
  a reconexão mantém o valor atual. Reconectar nunca desliga a proteção sem a
  pessoa ver.
- **Campos antes da proposta (GEN-060):** uma escrita que vira proposta passa
  antes pelas mesmas regras de campo do pipeline (`Node.Create`/`Edit`,
  `Relation.AssertHard`, valores de Property contra o Type), rodadas sobre o
  grafo atual e descartadas (`GraphWriter.CheckFieldsInTransactionAsync`).
  Entrada inválida responde `validation_failed`, nunca `pending_approval`.
- **Códigos `agents.*`:** ficam em `Graph.Contracts` (`AgentWriteErrors`),
  porque a política é do dono do GraphChangeSet (DA-112); o prefixo é o
  publicado pela DA-130.
- **Nomes das operações nos detalhes de erro:** `operations`, `operations[i]` e
  `operations[i].campo` saem só de `OperationKeys` (`Graph.Contracts`), que
  também renumera a chave na aprovação parcial; o Graph e o adapter MCP usam o
  mesmo construtor (GEN-021).
- **202 + Location (DA-130):** nenhuma rota REST recebe escrita de agente (a
  API é só de sessão, DA-109), então o 202 não aparece no E5. O
  `IHttpResultResponder.ToHttpResult<T>(Result<T>, Func<T, IResult>)` do limaj
  3.0.0 (assinatura conferida no pacote restaurado) aceita qualquer `IResult`
  no sucesso, inclusive `TypedResults.Accepted(location, body)`: não há lacuna
  para o owner.
- **Rate limit (DA-051):** `Api:RateLimits:AgentWritePermitLimit` (60 por
  janela de 60 s) e `AgentWriteDailyLimit` (1.000 por dia) por AgentIdentity,
  em memória, como o orçamento de leitura. Toda ferramenta de escrita conta,
  inclusive criar proposta.
- **Limites (DA-045/046/132/130/048):** `Graph:AgentWrites` (`MaxOperations`
  50, `BulkThreshold` 20, `WindowMinutes` 10, `WindowPerAgent` 20,
  `WindowPerAccount` 40, `ProposalLifetimeDays` 7, `MaxPendingProposals` 10).
- **Expiração:** job `graph.proposal_expiry` agendado na criação; leituras já
  tratam proposta vencida como `expired` antes do job.
- **Purge:** o purge de um Node esvazia as propostas que o citam
  (`target_node_ids`); uma ainda pendente é retirada (`withdrawn`), para que
  ninguém aplique o que sobrou dela. A exclusão da conta apaga as propostas.
- **DA-133:** `created_by_agent_identity_id` em `nodes` e `relations`, com FK
  `ON DELETE SET NULL` para `agent_identities` só na migration; o pipeline
  zera a posse ao salvar qualquer Node/Relation alterado por outro principal
  (escrita, Undo, restore). Não entra no snapshot do histórico.
- **Inbox (DA-049):** `suggested_type` no Node (só sem Type), exibido na Inbox
  como "Sugestão do agente".
- **Filtro de situação:** `GET /api/proposals?status=` recebe o valor como o
  contrato o escreve (`proposed`, `expired`...). O binder de query só conhece
  os nomes do C#, então o caso de uso faz a leitura; valor desconhecido é
  `validation_failed`. O contrato documenta o parâmetro como `ProposalStatus`
  (`WithQueryEnum`, lido por um transformer do OpenAPI no Host; API-001), e o
  cliente gerado o tipa.
- **REST da pessoa:** `GET/POST /api/proposals` (lista, detalhe com diff
  campo a campo e `overwritesYourValue`, `approval` total ou parcial,
  `rejection`), `GET /api/changesets/{id}` (tela de GraphChangeSet),
  `POST /api/changesets/{id}/review` e os filtros `agentsOnly`,
  `agentIdentityId` e `unreviewed` no feed. O toggle muda por
  `PATCH /api/agent-identities/{id}` com `requiresApproval`.

- **DA-135 no backend:** `ProposalDetail` ganhou `agent` e `contentPurged`, e
  cada operação ganhou `dependsOn`; `ChangeSetDetail` ganhou `agent` (null para
  a pessoa) e `undo` (`state` = `available`, `undone` com `byChangeSetId` ou
  `unavailable` com `reason`), calculado pela mesma regra de purge do Undo
  (`ChangeSetUndo.PurgedReason`). O nome do agente vem do Accounts pelo ponto de
  extensão `IAgentIdentityDirectory` (Infrastructure), como o purge de conta: o
  Graph nunca nomeia a entidade do Accounts (DA-121). A escolha da aprovação
  parcial é de `ProposalApproval` (`agents.operation_dependency`, 422, com
  `operations[i]` de cada operação órfã); um conflito de versão numa aprovação
  parcial é renumerado para o índice da proposta. O ChangeSet aprovado nasce com
  `reviewed_at`. As operações desmarcadas não entram no hash da DA-043 item 8,
  que só compara propostas `rejected`. Revogar chama os
  `IAgentRevocationParticipant` na mesma transação; o do Graph retira as
  propostas pendentes sob o advisory lock da Account.
- **Listas de Agent Changes:** `ProposalItem` e `ChangeSetItem` (o feed) trazem
  `agent: AgentRef` (null no ChangeSet da pessoa) e o `ChangeSetItem` traz `undo`,
  pela mesma regra do detalhe. Assim os cartões não dependem da lista de
  conexões, que só alimenta o filtro por agente. O "sobrescreveu um valor seu"
  é respondido em SQL, uma entrada por pergunta (campo × entidade), nunca o
  histórico inteiro (DB-023). O `PATCH /api/agent-identities/{id}` muda nome e
  toggle num único caso de uso, numa transação (`UpdateAgentIdentityRequest`).
- **DA-134:** a descrição do scope de escrita (consentimento e Agentes
  conectados) termina com "Pode propor exclusões, que só você aprova".
- **DA-135 no frontend:** `/agent-changes` (duas listas, filtros na URL),
  `/agent-changes/proposals/:id` (diff, aprovação total ou parcial, recusa) e
  `/changesets/:id` (tela de GraphChangeSet, ligada de Recent Changes). O toggle
  fica em cada agente com `lifegraph.write` em Agentes conectados.

## Critérios de saída

A partir de um agente real (Claude Code), "guarde isso e relacione com X" gera
Nodes e Relations visíveis em Agent Changes, e o usuário consegue desfazer. A
mesma prova no Claude.ai ou no ChatGPT, pelo domínio, fica no E14 (DA-147).

## Fora de escopo

`capture_resource` (E7), ferramentas de Tag (E8), criação de shares por agente.
