# E8 — Tags, Collections e editor completo de Properties

- **Ordem:** 8
- **Depende de:** E2
- **Última revisão:** 2026-10-23

## Contexto

Tags dão classificação flexível. Collections são conjuntos manuais ou
dinâmicos que viram itens de navegação (v2 §9–10). O editor completo de
Properties fica junto com as Collections porque os critérios salvos delas
filtram por tipo de propriedade: mudar o tipo exige reconciliar as queries
salvas.

## Decisões

- **DA-063 — Collections são queries, não donas dos dados** (invariante). Um
  Node que satisfaz o critério aparece automaticamente. Collections **não**
  servem como fronteira de privacidade (DA-035).
- **DA-064 — Tags complementam, não substituem Relations, e não substituem
  Types** (glossário). Uma "tag genérica" não resolve Nodes sem Type; quem faz
  isso é a Inbox.
- **DA-065 — Matriz de mudança segura de tipo** (consenso):
  Select→MultiSelect, Date→DateTime e Number/URL/Date→Text. Qualquer outra
  mudança é bloqueada: crie uma nova Property. Remover uma opção de Select em
  uso é bloqueado ou exige remapeamento.
- **DA-066 — Editar uma Property Definition afeta todos os Types em que ela
  está anexada** (consenso). A UI mostra "usada em N Types". Desanexar move os
  valores para Outras propriedades; reanexar os traz de volta. Apagar uma
  definição ainda anexada é bloqueado. Apagar uma definição solta remove os
  valores remanescentes, após uma confirmação que mostra a contagem.
- **DA-067 — "Promover" uma Outra propriedade** = anexar a definição existente
  ao Type atual, ou mapear para uma definição compatível. Há um passo de
  "casar com definição existente" para não criar duplicatas.
- **DA-068 — Ferramentas MCP de Tag chegam neste épico** (consenso). O E5 não
  promete tagging.
- **DA-160 — Tag é entidade própria, e o vínculo com o Node é entrada própria
  do ChangeSet** (consenso, Q378). Novo `GraphEntityKind` Tag e entradas de
  vínculo NodeTag. Marcar ou desmarcar **não** muda a `version` do Node: o
  vínculo não é estado do Node (esclarece a DA-022), então uma tag de agente
  nunca causa 409 numa edição do usuário. Apagar uma Tag em uso desvincula de
  todos os Nodes, tombstones incluídos, no mesmo ChangeSet, com Undo. Restaurar
  um Node restaura os vínculos dele. Undo, Restore, Purge, feeds e o diff da web
  ganham o ramo de Tag.
- **DA-161 — Nome de Tag** (consenso, Q379). Texto livre: trim, sem o `#`
  inicial (o `#` é só apresentação), espaços internos colapsados, máximo de 50
  caracteres. `#` em qualquer outra posição, nome vazio ou só `#` são recusados
  (`ErrorType.Validation`). A unicidade por Account usa uma chave normalizada
  insensível a caixa e a acento, calculada no domínio e guardada em coluna com
  índice único. O nome de exibição fica como o usuário digitou. Sem slug.
- **DA-162 — Ferramentas MCP de Tag** (Q380). Consenso: `list_tags` (Safe),
  `tag_node` e `untag_node` (Write, cada um conta na janela pela DA-132) e
  operações de tag em `suggest_changes`. `list_tags` mostra só as Tags com ao
  menos um Node visível para o agente, mais as Tags sem nenhum Node; a contagem
  conta só Nodes visíveis. `get_node` e `get_context` trazem as Tags só de Nodes
  visíveis. **Provisório (decidido pela analista; o arquiteto chegou à mesma
  posição no contra-argumento):** agentes só aplicam Tags existentes; nome
  desconhecido responde `graph.tag_not_found`, sem `create_tag` no MVP, porque
  vocabulário criado por agente vira lixo mais rápido que ontologia. Outra
  visão: `tag_node` faz find-or-create pela chave normalizada, com teto por
  ChangeSet. Para reverter: find-or-create com a criação como entrada desfazível
  no mesmo ChangeSet.
- **DA-163 — Collections vivem no Graph e são escritas pelo pipeline**
  (consenso, Q381). Definição de Collection e associação manual são
  `GraphOperations` com um novo `GraphEntityKind`, com Provenance e Undo
  (DA-013). Pipeline e filtro de leitura continuam internos ao Graph; não há
  ponto de extensão público. O módulo `LifeGraph.Collections` fica vazio (no
  máximo um adapter HTTP). Emenda a DA-112: "Collections escrevem pelo
  pipeline" significa o slice de Collections dentro do Graph, não o módulo. A
  reconciliação da DA-171 fica atômica na mesma transação.
- **DA-164 — Linguagem de critérios da Collection dinâmica** (consenso, Q382).
  Dois tipos sem mistura: manual e dinâmica. Critérios numa lista plana com a
  chave todos/qualquer (sem grupos OR de AND). Critérios por Type, Tag, Property
  e Relation Hard para um Node dado (nos dois sentidos, qualquer kind ou um kind
  dado). Operadores: igual, contém (MultiSelect) e vazio. Faixas
  (Number/Date/DateTime) ficam fora até haver medição, porque só há GIN de
  igualdade (DA-015). O operador é um discriminador extensível e o JSON dos
  critérios é versionado desde o primeiro dia. Valores em Outras propriedades
  contam (o critério referencia `property_id`). Ordem: mais recente primeiro.
  Critérios guardam referências por id, nunca por nome.
- **DA-165 — Mudança de tipo reescreve os valores no mesmo ChangeSet**
  (consenso, Q384). Os valores dos Nodes, tombstones incluídos, são reescritos
  como entradas Updated, e o Undo os reverte (recusado com explicação se um
  Node foi editado depois). Date→DateTime vira meia-noite UTC (sem fuso
  guardado; a UI avisa na confirmação). Number→Text usa a forma invariante;
  Date→Text e URL→Text, a forma guardada. A matriz da DA-065 só vale quando
  existe algum valor, tombstones incluídos; sem valores, qualquer mudança segue
  livre. A checagem de valores roda na mesma transação da mudança. Há um teto
  de Nodes reescritos por transação, e acima dele a mudança é recusada com erro
  claro.
- **DA-166 — Opção de Select em uso: só bloqueio** (Q385). **Provisório
  (decidido pela analista; o arquiteto chegou à mesma posição no
  contra-argumento):** remover uma opção em uso continua bloqueado, com a
  contagem de Nodes na mensagem; sem remap no MVP. Para aposentar uma opção em
  uso, arquive-a (DA-167). Outra visão: remap para outra opção da mesma
  definição, com dedupe em MultiSelect, reaproveitando a reescrita da DA-165.
  Para reverter: adicionar a ação de remap e reescrever os critérios que citam
  a opção.
- **DA-167 — Arquivar e restaurar** (consenso, Q386). Arquivar uma Property
  Definition: ela continua anexada, os valores ficam visíveis e editáveis ou
  removíveis, ela some dos seletores de anexar e não aceita valores novos; o
  agente a vê em `list_types` marcada como arquivada. Arquivar uma opção de
  Select: deixa de ser selecionável e os valores existentes mantêm o rótulo
  (entra porque a DA-166 tira o remap). Arquivar não é exclusão nem Purge.
- **DA-168 — Apagar definição solta com valores** (Q387). Consenso: uma
  leitura de uso devolve as contagens e o DELETE leva a contagem esperada;
  divergiu, é recusado com erro de conflito, para nunca apagar mais do que o
  usuário viu. Valores de Nodes vivos saem como entradas Updated no mesmo
  ChangeSet, e o Undo os restaura. **Provisório (decidido pela analista; o
  arquiteto chegou à mesma posição no contra-argumento):** valores em
  tombstones continuam bloqueando com `property_in_use_by_deleted_nodes`, com
  a contagem e a data de Purge mais distante, e a mensagem aponta o Purge
  manual. Apagar valor de Node restaurável seria perda silenciosa. Esclarece a
  DA-066: "valores remanescentes" são os de Nodes vivos. Outra visão: remover
  também os valores dos tombstones. Para reverter: incluir os tombstones como
  entradas Updated e reportar as contagens separadas.
- **DA-169 — Código de transição bloqueada** (consenso, Q388; muda a regra da
  DA-023, triagem categoria B). As transições da DA-065 com valores passam a ter
  sucesso, e os testes que esperavam recusa mudam. As transições fora da matriz
  respondem `graph.property_kind_change_blocked` (422 pelo catálogo, DA-101).
  `graph.property_has_values` fica para os casos que ainda descreve (opção em
  uso).
- **DA-170 — Outras propriedades: só "promover" no MVP** (Q389).
  **Provisório (decidido pela analista; o arquiteto chegou à mesma posição no
  contra-argumento):** promover = anexar a definição existente ao Type do Node,
  com o passo de casar com definição existente (candidatos com nome igual sem
  diferenciar caixa e acento, mais kind compatível). O "mapear para definição
  compatível" da DA-067 fica adiado, como desvio registrado da DA-067. Outra
  visão: mapear só neste Node, destino anexado ao Type, conversão pela DA-065,
  recusa se o destino já tem valor. Para reverter: entregar esse mapeamento
  mínimo.
- **DA-171 — Reconciliação dos critérios salvos** (Q390). Consenso: mudança de
  Property, opção, Tag ou Type nunca é bloqueada por Collection. Mudanças sem
  perda reescrevem o critério na mesma transação (Select→MultiSelect: igual
  vira contém). **Provisório (decidido pela analista):** um critério inválido
  não casa com nada, e a Collection fica com `needsReview` e um aviso que nomeia
  o critério quebrado, visível também na lista de Collections; referências
  pendentes ficam guardadas e marcadas, sem remoção automática, e se corrigem
  sozinhas se o Undo trouxer a referência de volta. API e payloads de agente
  expõem `needsReview` com o motivo. Outra visão (arquiteto): ignorar o critério
  inválido com aviso e remover referências pendentes no mesmo ChangeSet. Para
  reverter: avaliar o critério inválido como ausente e remover as referências
  pendentes no ChangeSet da mudança.
- **DA-172 — Interfaces do frontend do E8** (consenso: o arquiteto escolheu e a
  analista validou após um ajuste; Q391–Q393). Um hook por ação sobre
  `useGraphWrite`, como em `graphData.ts` (mantém `isPending` e erro por ação,
  FE-021/FE-022). Tags: `tagsData.ts` com `tagsQuery`, `useCreateTag`,
  `useRenameTag`, `useDeleteTag`, `useTagNode`, `useUntagNode` e
  `tagNameSchema`; `<TagPicker nodeId tags readOnly />` (combobox novo em
  `components/ui`, "Criar «x»" para humanos); `TagsPage` em `/tags` reaproveita
  o `NameForm` extraído. Collections: `collectionsData.ts` com critérios
  versionados em união discriminada, manual derivado de `criteria == null`
  (FE-010), `<CollectionCriteriaEditor value onChange invalid />` controlado,
  `<AddToCollectionMenu nodeId readOnly />`, `<CollectionNavLinks />`; as chaves
  ficam sob `graphKeys.all`, e o probe de Recent Changes mais o refetch no foco
  atualizam os membros quando um agente escreve (o critério de saída vale "no
  próximo probe ou ao focar, sem recarregar"). O cursor de paginação não vai
  para a URL: é estado transitório, não compartilhável (FE-012 cobre estado
  compartilhável; precedente `nodesQuery`). Properties: `Dialog` e
  `ConfirmDialog` em `components/ui` (FE-005), um único
  `PropertyDefinitionEditor` para criar e editar, uso carregado ao expandir a
  linha, `ALLOWED_KIND_TRANSITIONS` só pré-filtra (o servidor é a autoridade),
  `PromoteDialog` por linha de Outras propriedades. Páginas do dono não entram
  na visão compartilhada; só os componentes do Inspector recebem `readOnly`.
  `relationKind` do critério é texto, porque o kind de Relation é texto livre
  no domínio. As assinaturas exatas vivem no código: `web/src/features/tags/`,
  `web/src/features/collections/` e
  `web/src/features/graph/PropertyDefinitionEditor.tsx`.
- **DA-173 — Collection fixada na navegação** (consenso). Collection ganha
  `pinned` (booleano), que é estado do grafo: muda pelo pipeline, com ChangeSet,
  Provenance e Undo. A navegação mostra as 8 primeiras fixadas, por ordem de
  criação, e o link "Todas as Collections". As fixadas além de 8 recebem a nota
  "fora do menu", e o toggle avisa quando já há 8. O servidor não limita
  quantas são fixadas. Ordem explícita (`order`) fica para quando houver UI de
  reordenar, como migration aditiva.

## Checklist

- [x] Tags: CRUD, aplicar e remover via ChangeSet
- [x] Ferramentas MCP de Tag (Write)
- [x] Collections manuais e dinâmicas (critérios por Type, Property, Tag, Relation), como itens de navegação
- [x] Editor completo de Properties: matriz de mudança de tipo, opções de Select, arquivar e restaurar, "usada em N Types"
- [x] Promover/mapear Outras propriedades (mapear adiado, DA-170)
- [x] Reconciliar Collections salvas quando muda o tipo de uma propriedade
- [x] Corrigir o teste instável herdado do E1,
  `SessionTests.After_the_window_an_existing_and_an_unknown_email_answer_the_same_again`
  (`tests/LifeGraph.IntegrationTests/Accounts/SessionTests.cs:125`), pedido do
  usuário em 2026-10-22. Ele falhou uma vez na suíte completa durante o E7 e
  passou nas três vezes em que rodou sozinho (triagem: categoria C). A causa
  provável: as 6 tentativas de login, cada uma com hash de senha, precisam
  caber numa janela fixa de 1 s (`Accounts:RateLimits:WindowSeconds = 1`). Com
  a suíte carregada a janela vira antes da sexta tentativa, e ela não recebe
  429. O teste vizinho `After_the_window_the_user_signs_in_with_the_right_password`
  tem o mesmo padrão. Pela GEN-072, teste instável é bug: tornar o teste
  determinístico (por exemplo, um relógio controlado no rate limiter, como
  `TimeProvider`) sem enfraquecer o que ele garante.

## Critérios de saída

O usuário cria "Quero ler" (Type = Book, Status = WantToRead) e a Collection se
atualiza sozinha quando um agente cria um livro.

## Fora de escopo

Views alternativas (Timeline, Calendar, Map), filtros semânticos.
