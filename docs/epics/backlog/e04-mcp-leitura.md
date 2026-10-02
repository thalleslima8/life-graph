# E4 — Leitura via MCP

- **Ordem:** 4
- **Depende de:** E3
- **Última revisão:** 2026-10-02

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

## Checklist

- [ ] Flag "oculto para agentes" em Type e Node + UI para marcar
- [ ] Filtro central de leitura com principal (Human | AgentIdentity), funcionando como parede na travessia
- [ ] Ferramentas MCP: `search_graph` (FTS + unaccent), `get_node`, `get_context`, `list_types`
- [ ] CTE recursiva com limites de profundidade, vizinhos e total; ranking determinístico
- [ ] Envelopes de texto não confiável
- [ ] Log de auditoria de leitura (sem conteúdo)
- [ ] Rate limit de leitura por AgentIdentity

### Testes obrigatórios
- [ ] Node oculto nunca aparece em nenhum caminho de leitura (busca, contexto, relações, contagens, histórico)
- [ ] Hub com 300 arestas não esgota o orçamento dos irmãos
- [ ] Mesma entrada produz a mesma saída
- [ ] Ambiguidade devolve candidatos sem expandir

## Critérios de saída

No Claude.ai ou no ChatGPT, o usuário pergunta sobre um assunto e o agente
responde usando `get_context`, sem nunca ver Nodes ocultos.

## Fora de escopo

Escrita por agentes (E5), busca semântica (E9).
