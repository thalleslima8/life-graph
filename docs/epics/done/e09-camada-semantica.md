# E9 — Camada semântica

- **Ordem:** 9
- **Depende de:** E2, E7
- **Última revisão:** 2026-10-24

## Contexto

A Semantic Layer (v2 §45) descobre proximidade por embeddings, sem LLM: busca
semântica básica, Soft Relations sugeridas e o passo semântico do
`get_context`. O conteúdo dos Resources é a principal entrada dos embeddings,
por isso este épico vem depois do E7.

## Decisões

- **DA-069 — Embeddings assíncronos via outbox** (consenso). Nunca bloqueiam
  nem derrubam uma escrita. Ficam numa tabela à parte, com chave
  `(node_id, model_id)`, `dimension` e `content_hash`. Só são recalculados
  quando o texto muda (título, corpo, propriedades de texto, Outras
  propriedades de texto, metadados de Resource). Uma troca de modelo dispara
  reindexação em lote.
- **DA-070 — Soft Relations persistidas como projeção derivada, fora do log de
  ChangeSet** (consenso, ADR: [docs/adr/0011-soft-relations-fora-do-changeset.md](../../adr/0011-soft-relations-fora-do-changeset.md)). Top-k ≈ 5 por Node acima de um
  limiar (~0.75, a calibrar), via HNSW, com pares sem ordem e explicação
  (método, modelo, score). Se passassem pelo ChangeSet, o recálculo encheria o
  Recent Changes de ruído e geraria conflitos de Undo falsos. Só as ações do
  usuário (Accept/Ignore) geram ChangeSet.
- **DA-071 — Accept e Ignore** (consenso, glossário). Accept cria uma Hard
  Relation com origin=user. Ignore grava uma supressão por par
  (`soft_dismissal`), que sobrevive a trocas de modelo e evita que o par seja
  sugerido de novo.
- **DA-072 — Nodes ocultos para agentes nunca vão para um provedor de
  embeddings de terceiros** (consenso). O usuário espera que "oculto" signifique
  "não enviado a uma empresa de IA". Com modelo local, eles podem ter
  embeddings para as sugestões do próprio usuário, mas sempre filtrados das
  respostas a agentes.
- **DA-073 — Provedor de embeddings: decisão do usuário neste épico**
  (pendente). Resumo das opções:
  - **Modelo local** (ex.: `multilingual-e5-small`, 384 dimensões, bom em PT):
    sem suboperador e com residência igual à do host, mas pede ~1 GB de RAM.
  - **API** (ex.: OpenAI `text-embedding-3-small`, custo desprezível): processa
    fora do Canadá, entra como suboperador, exige linha na PIA e o nosso Purge
    não apaga a retenção do lado do provedor.
  - Cada épico que adiciona um suboperador registra uma linha no registro
    contínuo de "suboperadores e fluxos de dados".
- **DA-174 — Provedor de embeddings: modelo local no processo da API** (Q394;
  consenso entre arquiteto e analista na execução do /flow; **o épico marcava
  como decisão do usuário: confirme ou reverta no PR**). Usamos o
  `multilingual-e5-small` (384 dimensões) via ONNX Runtime +
  Microsoft.ML.Tokenizers, com prefixos `query:`/`passage:`. O modelo é baixado
  por script com SHA-256 fixado para um volume, nunca para o git. Ele fica atrás
  de uma porta de embedder, para que um sidecar (ex.: text-embeddings-inference)
  o substitua sem mexer nos chamadores. O sidecar também é o plano B se o
  tokenizer não suportar o SentencePiece do XLM-R. Por quê: sem suboperador, sem
  linha na PIA e sem retenção do lado de terceiros que o Purge não alcança. Nodes
  ocultos podem ter embeddings para as Soft Relations do próprio usuário
  (DA-072). Custo: cerca de 1 GB de RAM na API, a considerar no E14. Trocar de
  modelo exige reindexar e mudar a dimensão. Não adiciona suboperador.
- **DA-175 — Verificação do modelo real e limiar por modelo** (Q395; consenso).
  Os testes automáticos usam um embedder falso e determinístico (GEN-072). Uma
  categoria de teste opt-in, ignorada sem o arquivo do modelo, roda localmente
  antes de fechar o épico. Ela usa uma fixture fixa em PT com pares
  relacionados, não relacionados e quase duplicados, e verifica a separação no
  limiar. O limiar é configurado por modelo, não global, e o valor calibrado é
  registrado aqui. Por quê: o e5 dá de 0.7 a 0.8 para textos sem relação, então
  o ~0.75 do DA-070 é suspeito. Um limiar errado enche o usuário de Soft
  Relations ruins, e baixar o modelo em todo PR não compensa.
  Valor calibrado: **0.89** para `intfloat/multilingual-e5-small` (pares
  relacionados de 0.906 a 0.923, não relacionados de 0.844 a 0.879, quase
  duplicados ≈ 0.99).
  Limiar da busca (`Semantic:Search:MinimumScores`), calibrado à parte porque
  a pergunta entra com o prefixo `query:` e o Node com `passage:`: **0.835**
  para `intfloat/multilingual-e5-small` (pergunta curta contra Node que trata
  do assunto de 0.838 a 0.937, contra Node sem relação de 0.802 a 0.834). A
  faixa é estreita: o limiar deixa fora todo Node sem relação, e um Node ligado
  à pergunta só por conhecimento que o texto não diz (`estoicismo` contra o
  título sobre Marco Aurélio, 0.831) fica a cargo das palavras.
- **DA-176 — `node_embeddings` pertence ao Graph** (Q396; consenso; refina o
  DA-069). A tabela é um dado derivado do Node, como o `search_vector`. O SQL
  vetorial (busca híbrida e passo semântico) fica no Graph, ao lado do
  `GraphReadFilter`. O Semantic implementa o adapter do embedder (inclusive o
  `IQueryEmbedder` de `Graph.Contracts`), o job e a lógica de projeção, e só
  fala com o Graph por `Graph.Contracts`. Um teste de arquitetura garante a
  direção. Por quê: o filtro de visibilidade é interno ao Graph. Ler a tabela de
  outro módulo violaria o DA-112, e duplicar o predicado arriscaria vazar Nodes
  ocultos.
- **DA-177 — Kind das Soft Relations e Accept** (Q397; consenso). Toda Soft
  Relation tem o kind fixo `related_to`, reservado ao sistema, e é guardada com
  source = menor id. O Accept cria uma Hard Relation `related_to` na mesma
  direção, com origin=user, num GraphChangeSet, sem pedir nada ao usuário. Ele
  pode editar o kind depois, pelo editor de Relation. Por quê: a similaridade é
  simétrica, e pedir direção ou kind inventaria um significado.
- **DA-178 — Soft Relations na tabela `relations`, atrás de uma porta de
  leitura estrutural** (Q398a; consenso após contra-argumento; candidata a
  ADR). As linhas Soft ficam na `relations`, que já tem o check e o teste para
  isso. A exclusão delas não depende de um predicado por consulta:
  - uma única porta de leitura (view ou filtro central) entrega só Hard aos
    caminhos existentes;
  - um teste de arquitetura proíbe ler `relations` por fora dessa porta;
  - um teste de integração cobre todos os caminhos: REST, MCP, Export,
    SharedGraphView, Undo, Purge, grau do Node, delete em cascata e feeds de
    mudança.

  Export (E12) e SharedGraphView (E11) ainda não existem. Neste épico, o teste
  de integração cobre REST, MCP, `get_context`, Undo, Purge, grau do Node,
  delete em cascata e feeds de mudança; para Export e SharedGraphView valem
  só a porta estrutural e o teste de arquitetura. O E11 e o E12 devem incluir
  o caminho que criarem nesse teste de integração.

  Delete, Undo e Purge ignoram ou removem as linhas Soft sem gerar
  GraphChangeSet. Se a porta central não for viável, as Soft vão para uma
  tabela separada. Por quê: esquecer o predicado numa leitura futura expõe Soft
  em Export ou SharedGraphView sem nenhum aviso.
- **DA-179 — Onde as Soft Relations aparecem** (Q398; consenso).
  - No grafo 2.5D local, só as arestas Soft que saem do Node raiz, a mesma regra
    do `get_context`.
  - No Inspector, numa seção "Soft Relations" separada, com Accept e Ignore. A
    lista de relations atual segue só com Hard, e o "Remover" nunca aparece numa
    linha Soft.
  - Export e SharedGraphView excluem as Soft.
  - Nenhum Node oculto para agentes aparece como ponta de uma Soft numa resposta
    a agente, nem no `get_context` com `includeSoft`.
- **DA-180 — `get_node` do MCP só com Hard e sem flag `includeSoft`** (Q398b;
  **provisória: decidida pelo arquiteto**). Quem quer Soft usa o `get_context`
  com `includeSoft`, que é o ponto único. Outra visão (analista): um
  `includeSoft` com padrão false em `get_node`, para seguir a convenção do
  `get_context`. Para reverter: adicionar o parâmetro opcional ao `get_node`,
  passando pelo mesmo filtro central.
- **DA-181 — Backfill e reindexação por migration, com rede de segurança no
  login** (Q399; consenso). Uma migration, rodando com o papel migrator, enfileira
  um job `semantic.reindex` por Account, tanto no backfill inicial quanto a cada
  troca de modelo. Um `IAccountSetupParticipant` idempotente agenda a
  reindexação quando a Account não tem linhas para o modelo configurado. Nodes
  novos ou com texto alterado enfileiram o job na mesma transação da escrita.
  O job é idempotente por `(node_id, model_id, content_hash)` e processa em
  lotes. Por quê: segue o precedente do DA-149/158 e não amplia privilégios com
  uma SECURITY DEFINER.
- **DA-182 — A Inbox mostra só a contagem de Soft Relations** (Q400; consenso).
  Accept e Ignore ficam só no Inspector. Na Inbox, cada item mostra "N Soft
  Relations", com link para o Inspector. Os rótulos seguem o glossário: "Soft
  Relation", Accept e Ignore. Nunca "sugestão", "confirmar", "promover" ou
  "remover". Por quê: a Inbox é uma fila de triagem e não deve virar uma segunda
  fila de revisão.
- **DA-183 — Busca híbrida em REST, UI e MCP** (Q401; consenso).
  - `GET /api/search?q=&limit=` e uma página `/search?q=` com um campo de
    busca no cabeçalho. O filtro por Type ficou fora do E9 (DA-184).
  - O `search_graph` do MCP também vira híbrido, e a descrição da ferramenta é
    atualizada.
  - A busca cai para FTS quando o embedder está indisponível ou a Account ainda
    está indexando.
  - Tudo passa pelo filtro central. Para agentes, Nodes ocultos não aparecem.
  - O texto da busca não vai para log (GEN-043), e a busca tem rate limit.
- **DA-184 — Interfaces do frontend do E9** (Q400/Q401; consenso: o arquiteto
  escolheu um híbrido e o analista validou depois de um contra-argumento).
  - Soft Relations: `nodeSoftRelationsQuery`, `useAcceptSoftRelation` e
    `useIgnoreSoftRelation` sobre `useGraphWrite`. A seção `NodeSoftRelations`
    é privada do `NodeInspector`, respeita o `readOnly`, fica oculta quando
    vazia ou NotFound e tem a âncora `#soft-relations`. A Inbox ganha o campo
    `softRelationCount` no item da lista.
  - Busca: `searchQuery({ q })` com mínimo de 2 caracteres, `SearchBox` sem
    props e `SearchPage` lazy. A resposta é `{ items: { id, title, typeName,
    snippet, match }[], degraded }`.
  - A busca no cliente é sempre por envio, nunca enquanto o usuário digita.
    Isso protege o custo de embedding e o rate limit.
  - Sem filtro por Type no E9. O seletor fica para o backlog.
  - Referência completa: o arquivo de interface da run.
- **DA-185 — Testes de Purge restritos aos jobs `graph.purge`** (Q402; consenso;
  test-failure-triage categoria B). Toda escrita de Node passa a enfileirar
  `semantic.embed` (DA-069, DA-181). Por isso, `GraphPurgeTests` passa a
  filtrar por `kind = 'graph.purge'` as consultas e contagens de `jobs` em
  cinco pontos. Duas mudanças vão além do filtro: o teste de refazer um delete
  roda antes os `semantic.embed` vencidos (da criação e da restauração), para
  que `RunDueJobsAsync() == 1` siga significando "só o purge antigo rodou"; e o
  teste de exclusão de conta verifica, na preparação, que a outra Account tem
  dois jobs de purge. Regra anterior: depois de
  escritas de Node, `jobs` só tinha jobs de purge. Regra nova: `jobs` pode ter
  outros kinds, e o teste de Purge só olha os dele. O teste de exclusão de
  conta (`The_account_purge_participants_erase_...`) segue contando **todos**
  os kinds para a Account apagada, o que garante que nenhum `semantic.embed`
  pendente sobreviva (LGPD). Os jobs da outra Account também são contados sem
  filtro, ou comparados antes e depois. O filtro só vale para a preparação do
  cenário. Por quê: o teste de Purge não deve quebrar a cada kind de job novo.
  A garantia de apagar tudo da conta continua intacta.
- **DA-186 — O banco impõe a forma da Soft Relation** (Q403; consenso; DB-004;
  test-failure-triage categoria B). O CHECK de Soft passa a exigir
  `inference_method` e `inference_model_id` não nulos, `kind = 'related_to'` e
  `source_node_id < target_node_id`. Antes, ele só exigia origin=system e
  confidence. O setup de `GraphReadToolsTests` deixa de criar Soft por UPDATE
  cru, com `similar_to` e source maior, e passa a montar as linhas no formato do
  domínio. A migration valida ou reescreve as linhas Soft existentes antes de
  aplicar a constraint. Por quê: toda relação automática precisa ser explicável
  (DA-070), e a forma canônica evita pares duplicados (DA-177). Mudar depois,
  com dados já gravados, custaria uma migração.
- **DA-187 — DELETE no id de uma Soft Relation responde 404** (Q404; consenso).
  A resposta é `graph.relation_not_found` e a linha é mantida. O plano previa um
  422 `graph.relation_is_soft`. Por quê: pela porta de leitura Hard-only da
  DA-178, a Soft não existe para a API de relations. Um 422 confirmaria que o
  id existe e furaria a porta. O usuário remove uma Soft pelo Ignore (DA-071).
- **DA-188 — Exceção à API-060 para a busca** (Q406; consenso no resultado:
  nenhuma mudança no código, top-N limitado). `GET /api/search` e o
  `search_graph` do MCP devolvem uma única página ranqueada, com `limit` padrão
  10, máximo 25 e 400 acima disso. Não há página seguinte. Quem quer mais
  resultados refina a consulta. A exceção segue o precedente do subgrafo
  limitado do E6. Por quê: a fusão por posição (RRF, k=60) entre FTS e vetor
  não dá um cursor estável, e paginar mudaria a forma de resposta fixada na
  DA-184 sem demanda do produto. O arquiteto via a API-060 como já cumprida,
  porque há padrão e máximo. O analista pediu a exceção explícita, que é a
  forma registrada aqui. Se a paginação vier depois, é uma decisão nova.

## Checklist

- [x] **Decisão do usuário:** provedor de embeddings (DA-073 → DA-174, consenso; confirmar no PR)
- [x] Tabela de embeddings + índice HNSW
- [x] Job de embedding via outbox (idempotente, com dead-letter: BE-034, BE-036)
- [x] Cálculo de Soft Relations top-k com explicação
- [x] Accept/Ignore no Inspector e na Inbox; arestas Soft no grafo 2.5D
- [x] Busca semântica básica (híbrida FTS + vetor)
- [x] Passo semântico no `get_context` (mesmo contrato, Soft só na profundidade 1)
- [x] Purge e exclusão de conta removem embeddings e Soft Relations

## Critérios de saída

Salvar um vídeo sobre Marco Aurélio sugere uma relação com Estoicismo, com
explicação. Ignore a faz sumir de vez.

## Fora de escopo

Clustering semântico avançado, linguagem natural com LLM interno.
