# E4 — Leitura via MCP

- **Ordem:** 4
- **Depende de:** E3
- **Última revisão:** 2026-10-08

## Contexto

Agentes passam a consultar o grafo como repertório pessoal do usuário (v2
§32–33): buscar, ler um Node e receber o contexto relevante (`get_context`),
nunca o banco inteiro. Antes de qualquer agente ler, os controles de
privacidade precisam estar ativos, porque a leitura é o momento em que os dados
saem do sistema rumo a provedores de IA de terceiros.

## Decisões

- **DA-035 — Oculto para agentes: flag por Type e por Node, aplicada no filtro
  central de leitura** (consenso). Não usamos Collections como fronteira: elas
  são queries dinâmicas, e editar uma propriedade poderia expor um Node
  silenciosamente. Um Node oculto **é inexistente para agentes**:
  - a travessia não passa por ele (funciona como parede);
  - fica fora de busca, relações, contagens, `omitted_count`, similaridade e
    histórico;
  - qualquer referência a ele responde como "não encontrado".

  Mudar o Type de um Node e torná-lo visível mostra um aviso na UI.
- **DA-036 — Contrato do `get_context`** (consenso + usuário):
  - **Entrada:** `node_id` (preferido) ou `subject`. O subject é resolvido
    nesta ordem: id → nome exato (sem diferenciar maiúsculas nem acentos) → FTS
    → busca semântica (só a partir do E9).
  - **Ambiguidade:** sem um match único forte, retorna `status: ambiguous` com
    até 10 candidatos e não expande nada. Expandir um palpite errado colocaria
    contexto errado no prompt do agente.
  - **Limites:** profundidade 1 por padrão, 2 no máximo; até 20 vizinhos por
    Node; **100 Nodes** no total (usuário: "começar devagar"); textos cortados
    em ~2.000 caracteres; resposta de ~50 KB no máximo.
  - **Ranking determinístico:** distância → Hard antes de Soft → `strength` →
    recência. Soft só na profundidade 1 e opcional.
  - **Saída:** grafo plano (`nodes[]`, `edges[]`, `truncated`, `omitted_count`
    só de visíveis). Cada Node traz um resumo de proveniência e o bloco
    `other_properties`.
- **DA-037 — Log de auditoria de leitura por agente** (consenso). Registra
  agente, ferramenta, consulta e IDs devolvidos, **sem conteúdo**. A Provenance
  só cobre escritas, e é na leitura que os dados vão para LLMs de terceiros.
  Retenção sugerida: 90 dias, a confirmar no E12/E14.
- **DA-038 — Envelopes de conteúdo não confiável** (consenso). Todo texto livre
  volta como `{text, trust, source}`. Conteúdo capturado da web **e** texto
  escrito por outros agentes vem marcado `untrusted`, para mitigar prompt
  injection entre agentes. A descrição das ferramentas diz que o conteúdo é
  dado, nunca instrução.
- **DA-039 — `list_types` é uma ferramenta Safe que empurra o agente a escolher
  um Type** (usuário + consenso). Types ocultos não aparecem na lista.
- **DA-092 — Sem criptografia ponta a ponta na aplicação** (consenso,
  ADR: [docs/adr/0013-sem-criptografia-ponta-a-ponta.md](../../adr/0013-sem-criptografia-ponta-a-ponta.md)). Os dados ficam criptografados em repouso pelo provedor.
  Criptografia ponta a ponta tornaria impossíveis a busca no servidor, o
  `get_context` e os embeddings, que são a proposta central do produto. A
  proteção vem do isolamento por conta, do filtro de ocultos e da auditoria.
- **DA-127 — Chaves em camelCase no REST e no MCP; valores de enum em
  snake_case** (consenso, emenda a DA-036). Os nomes de campo da DA-036
  (`omitted_count`, `other_properties`, `node_id`) nomeiam conceitos, não o
  formato no fio. As ferramentas MCP são adapters finos sobre os mesmos casos de
  uso do REST, e o `whoami` e o REST já usam camelCase. Responder o mesmo Node
  em dois formatos seria inconsistência de contrato. Os argumentos de entrada
  das ferramentas seguem a mesma regra que as saídas. Ainda não há consumidor
  externo, então a mudança é barata agora e cara depois do E13.
- **DA-128 — `edges[]` do `get_context` é o subgrafo induzido, com teto**
  (consenso, emenda a DA-036). Toda Relation visível entre dois Nodes
  devolvidos entra, e não só as que a travessia percorreu. Sem isso, a falta de
  aresta entre dois irmãos parece um fato ("não se relacionam"), que é o
  contexto errado que a DA-036 quer evitar. Cada aresta passa pelo filtro
  central (Nodes ocultos, outra conta) e segue a mesma regra de Soft (só na
  profundidade 1, e só quando pedida). Também carrega `origin`, `confidence` e
  `strength`. As arestas são ordenadas como os Nodes (Hard antes de Soft →
  `strength` → recência) e contam no orçamento de ~50 KB. Quando o teto corta
  arestas, a resposta marca `truncated` e informa quantas arestas visíveis
  ficaram de fora.
- **DA-129 — Só id e título exato contam como "match único forte"**
  (consenso, emenda a DA-036). Um match por id ou por título exato normalizado
  (sem diferenciar maiúsculas nem acentos) expande. Qualquer resultado de FTS,
  mesmo que seja um só, volta como `status: ambiguous` com os candidatos e não
  expande nada. Um limiar de `ts_rank` não é calibrado: depende do tamanho do
  corpus e do texto, varia por conta e não se explica ao usuário. Expandir um
  hit fraco é justamente o "palpite errado" da DA-036. `ambiguous` significa
  "não confirmado", e não só "mais de um", e o agente confirma chamando de novo
  com `node_id`. A mesma regra vale para a busca semântica no E9: um match que
  não é exato nunca expande.

## Checklist

- [x] Flag "oculto para agentes" em Type e Node + UI para marcar
- [x] Filtro central de leitura com principal (Human | AgentIdentity), funcionando como parede na travessia
- [x] Ferramentas MCP: `search_graph` (FTS + unaccent), `get_node`, `get_context`, `list_types`
- [x] CTE recursiva com limites de profundidade, vizinhos e total; ranking determinístico
- [x] Envelopes de texto não confiável
- [x] Log de auditoria de leitura (sem conteúdo)
- [x] Rate limit de leitura por AgentIdentity

### Testes obrigatórios
- [x] Node oculto nunca aparece em nenhum caminho de leitura (busca, contexto, relações, contagens, histórico)
- [x] Hub com 300 arestas não esgota o orçamento dos irmãos
- [x] Mesma entrada produz a mesma saída
- [x] Ambiguidade devolve candidatos sem expandir

### Condições das DA-127 a DA-129
- [x] DA-127: argumentos de entrada das ferramentas em camelCase, como as saídas
  e o REST (teste sobre o schema de todas as ferramentas)
- [x] DA-128: `edges[]` como subgrafo induzido pelo filtro central, com
  `origin`, `confidence` e `strength`, ranking Hard → Soft → `strength` →
  recência, teto de arestas e `omittedEdgeCount` (testes: irmãos ligados,
  aresta para oculto, outra conta, Soft não pedido, teto)
- [x] DA-129: só id e título exato expandem; qualquer resultado de FTS volta
  `ambiguous` (testes unitários da resolução e de integração com um hit de FTS)

### Aceite
- [ ] Aceite manual: no Claude.ai ou no ChatGPT, pelo túnel, o agente responde
  sobre um assunto usando `get_context`, sem nunca ver Nodes ocultos (junto com
  o aceite do E3)

### Notas de implementação

- A flag mora em `nodes.hidden_from_agents` e `types.hidden_from_agents` e muda
  pelo pipeline único (`UpdateNode.HiddenFromAgents`, `SetTypeHiddenFromAgents`), com
  GraphChangeSet e Undo (DA-013). No snapshot do histórico ela só é gravada
  quando ligada, para o histórico anterior continuar comparável no Undo.
- O filtro central é o `GraphReadFilter` (principal Human → tudo o que está
  vivo; AgentIdentity → sem ocultos; outro ou nenhum → nada). As leituras REST
  e as do MCP partem dele, e o SQL cru usa o mesmo predicado.
- O `get_context` é uma CTE recursiva com `LATERAL ... LIMIT 20` por Node; o
  segundo passo não volta aos vizinhos diretos da raiz. O `omittedCount` vem de
  uma segunda CTE sem limites, só sobre visíveis.
- As arestas (DA-128) saem do filtro central sobre os Nodes devolvidos, até
  `GraphReadLimits.ContextMaxEdges` (300). O orçamento de ~50 KB corta primeiro
  os Nodes de menor ranking e depois, com os Nodes que couberam, as arestas de
  menor ranking: um Node nunca sai para dar lugar a arestas. O que o teto e o
  orçamento cortam entra em `omittedEdgeCount`.
- O resumo de Provenance de cada Node (primeira e última mudança, se um agente
  ou uma importação já escreveu) é agregado em SQL, uma linha por Node.
- Envelopes `{text, trust, source}` em título, corpo e valores Texto/URL. Um
  Node que um agente ou uma importação já escreveu fica `untrusted` para
  sempre. Nomes de Type, de propriedade e o tipo da Relation saem sem envelope.
- `get_node` corta o corpo em 20.000 caracteres e lista até 20 Relations, com
  a contagem total de visíveis.
- Auditoria em `agent_reads` (RLS, purge da Account), sem retenção automática
  ainda (E12/E14).
- Orçamento de leitura: `Api:RateLimits:AgentReadPermitLimit` (120 por janela)
  por AgentIdentity, dentro do orçamento MCP da DA-121; o excesso responde 429
  com `Retry-After`.
- `agent_reads.agent_identity_id` tem chave estrangeira para `agent_identities`
  (DB-004) com `ON DELETE CASCADE`, só no SQL da migration, porque o modelo do
  Graph não referencia a entidade do Accounts (DA-111, DA-121). Uma conexão é
  revogada (`revoked_at`), nunca apagada; só o purge da conta a apaga, e as
  leituras vão junto, qualquer que seja a ordem dos participantes.
- `list_types` é paginado por cursor (API-060), como a lista REST de Types:
  ordem por id, 100 por padrão e 200 no máximo, com `nextCursor` enquanto
  houver mais.
- O ranking das Relations (Hard → `strength` → recência → id) vive em
  `RelationRanking.Ranked`; o `ORDER BY` da CTE aponta para ele e desempata
  pelo id da Relation, como ele.
- O JSON que os agentes recebem é definido uma vez, em `GraphWireJson`
  (Graph.Contracts): as ferramentas MCP escrevem a resposta com ele e o
  orçamento de bytes do `get_context` mede com ele.

## Critérios de saída

No Claude.ai ou no ChatGPT, o usuário pergunta sobre um assunto e o agente
responde usando `get_context`, sem nunca ver Nodes ocultos.

## Fora de escopo

Escrita por agentes (E5), busca semântica (E9).
