# E21 — Busca honesta, leitura barata e guia de uso para agentes

- **Ordem:** 21 (executa **logo depois do E13**, antes de E19, E20 e E14)
- **Depende de:** E4, E5, E9
- **Última revisão:** 2026-11-02

## Contexto

O relatório de uso `docs/analyses-tests/life-graph-como-memoria-de-agentes.md`
avaliou o Life Graph como memória não volátil de agentes de IA, agnóstica de
fornecedor (Claude, ChatGPT, Codex etc.). A direção é do usuário: o contexto
pertence à pessoa e qualquer agente autorizado o usa pelo mesmo contrato.

Conferido contra o código, o relatório acertou em três pontos que este épico
fecha e errou em outros (o `get_context` sem argumentos **já** começa na Raiz da
Conta e devolve `rootNodeId`; as `ServerInstructions` já mandam chamar assim
primeiro). Os pontos reais:

- Com a busca semântica indisponível, a busca devolve hits por palavras (FTS)
  com `degraded: true`, sem erro. A descrição da tool diz "try again later"
  (DA-183). Um agente que recebe "vazio + degraded" conclui que o Node não
  existe e **cria uma duplicata**.
- Ler contexto custa caro: o `get_context` devolve o Node completo (corpo cortado
  em 2.000 caracteres, orçamento de 50 KB, teto de 100 Nodes) e não há leitura
  só de títulos.
- Não existe documentação de como um agente (e a pessoa) deve usar o produto como
  memória: o que ler primeiro, como gravar em lote, o que fazer quando algo falha.

O E21 não muda o modelo do grafo, só o contrato de leitura e a documentação. O
Node de entrada ("guia para agentes") é do E22.

## Decisões

- **DA-283 — Busca degradada passa a dizer que o resultado pode estar incompleto**
  (consenso do arquiteto e do analista).
  - O resultado de busca ganha um campo explícito de resultado possivelmente
    incompleto, presente também quando só há hits por palavras, e uma dica em
    texto: "ausência não comprovada; não crie duplicata, tente de novo ou use
    `get_node`". O nome do campo é fechado no plano (`degraded` não é removido
    enquanto algum cliente puder depender dele).
  - Não há repetição interna da busca: o servidor não tenta de novo por conta
    própria.
  - O texto "try again later" da descrição da tool (DA-183) é corrigido. A DA-183
    ganha uma nota apontando para esta decisão.
  - Por quê: um contrato que devolve "vazio" sem dizer que está cego induz
    duplicatas, e a correção tem de estar no dado, não só na descrição (DA-284).
- **DA-284 — Comportamento crítico mora no contrato das tools e dos dados, nunca
  só nas `instructions`** (consenso).
  - Servidores MCP diferentes entregam `instructions` de formas diferentes, e
    alguns clientes as ignoram. O que protege a pessoa (aviso de incompletude,
    erro que diz o que fazer, rótulo de untrusted) vai no retorno da tool.
  - As `instructions` fixas continuam existindo só como atalho, nunca como única
    defesa.
  - Teste de contrato com o cliente do SDK MCP, roteiro manual de um segundo
    cliente em `docs/runbooks/` e uma matriz de compatibilidade documentada. Não
    se fixa lista de clientes-alvo nem se promete suporte a nenhum (usuário).
    Automação por cliente fica para depois.
- **DA-285 — `get_context` ganha `detail=summary`, opcional** (consenso).
  - O resumo traz título, tipo, trecho curto, `updatedAt` e contagem de
    relações. O padrão continua `full`. Não há projeção livre de campos.
  - O indicador "revisado por você" (E20, DA-282) entra no resumo no épico que
    rodar por último, sem dependência formal entre eles (o E20 tem número menor).
  - Por quê: o agente decide o que vale abrir com `get_node` sem pagar o corpo de
    todos os Nodes, e o contrato permanece pequeno e testável.
- **DA-286 — Sem tool de tag em lote; `apply_changes` documentado**
  (consenso).
  - `tag_node` e `untag_node` continuam com uma tag e um Node por chamada. Quem
    precisa de muitas usa `apply_changes` (1 a 100 operações atômicas), que passa
    a ter exemplos na descrição e no guia de uso.
  - As mensagens de erro das tools dizem ao agente o que fazer a seguir (qual tool
    chamar, qual argumento corrigir), sem expor conteúdo de Node (GEN-043).
  - A criação de Tags e Types por agente é outro ponto, fora deste épico.
- **DA-287 — O guia de uso para agentes é documentação do produto** (consenso).
  - Conteúdo: um ponteiro curto para colar no arquivo do agente (CLAUDE.md,
    AGENTS.md ou equivalente) que manda chamar `get_context` sem argumentos, o
    Node de entrada (E22), e a regra de guardar um **resumo com link** em vez da
    cópia do que já vive no repositório.
  - Inclui a convenção de envelhecimento: validade e "última verificação" ficam
    em **Properties da pessoa**, e o sinal de revisão do E20 (`reviewedAt`)
    ajuda; o núcleo não ganha `validUntil` (DA-288).
  - O texto do guia só descreve capacidades que existem na entrega. Quando o
    agente passar a criar Tags e Types, o guia é revisado.
- **DA-288 — Sem `validUntil` no núcleo** (consenso).
  - Validade é convenção por Property definida pela pessoa. Campo nativo de
    expiração exigiria semântica (o que acontece ao vencer?) que o produto não
    quer decidir pelo usuário.
- **DA-289 — Testes adversariais baratos entram em cada fatia, no mesmo PR**
  (consenso).
  - Aqui: o trecho curto do `detail=summary` e os resultados da busca carregam
    conteúdo de Nodes e de Resources capturados (untrusted, DA-153). Um teste
    por fatia prova que texto com instrução ("ignore as regras anteriores...")
    sai marcado como dado e nunca como orientação da tool.
  - A varredura ampla de prompt injection e a avaliação de risco ficam no E15.
- **DA-290 — Posição: logo depois do E13, antes de E19 e E20** (usuário).
  - O usuário disse que o próximo épico depois do E13 é o E21 e confirmou a
    ordem completa (DA-305). O README a registra.
  - O épico não depende do E13 nem toca nele.

- **DA-308 — Lote de tags pelo caminho real `suggest_changes` → `apply_changes`**
  (consenso do arquiteto e do analista; corrige a premissa da DA-286).
  - `apply_changes` só aplica uma proposta já feita; o lote vem de `suggest_changes`,
    com no máximo 50 operações para agentes (o 100 é o limite do REST da pessoa).
    Acima de 20 mudanças em Nodes da própria pessoa, ela aprova a proposta.
  - As descrições das duas tools ganham exemplos e dizem que a pendência de
    aprovação não é erro. O código não muda (ADR 0009, DA-045).
  - Por quê: prometer 100 faria o agente montar lotes que a tool recusa.
- **DA-309 — O guia não descreve o que ainda não existe** (consenso).
  - Node de entrada (E22) e `reviewedAt` (E20) ficam fora do guia desta entrega;
    E20 e E22 ganham uma tarefa de checklist para atualizá-lo. Vale a DA-287 onde
    o texto acima diz que o guia "inclui" esses itens.
  - Por quê: o agente age sobre o que o guia diz; capacidade prometida e ausente
    produz comportamento errado.
- **DA-310 — `get_context` por `subject` também avisa a incompletude** (consenso).
  - Em `ambiguous` com candidatos só por palavras, devolve `possiblyIncomplete` e a
    dica; a recusa de não encontrado mantém o código `graph.node_not_found` e o
    adapter acrescenta "ausência não comprovada". Mesmo tipo de estado da busca.
  - Por quê: a tool é usada para decidir onde arquivar um Node novo; sem o aviso
    reabre-se a duplicata que a DA-283 fecha.
- **DA-311 — `possiblyIncomplete` só no contrato do agente** (consenso).
  - O REST da pessoa mantém `Degraded` e a SearchPage já avisa; sem OpenAPI novo.
    `possiblyIncomplete` equivale a `Degraded` e isso entra no glossário.
  - Por quê: dois nomes para o mesmo estado na API pública, sem consumidor.

## Estrutura proposta

- `src/LifeGraph.Agents/` (adapter MCP): resultado de busca com o campo de
  incompletude e a dica, `detail` em `get_context`, descrições e mensagens de
  erro das tools, `ServerInstructions` enxutas apontando para o contrato.
- `src/LifeGraph.Graph/`: o caso de uso de leitura recebe o modo `summary`; a
  busca devolve o estado de incompletude no contrato do módulo
  (`LifeGraph.Graph.Contracts`), não no adapter.
- `docs/guias/guia-de-uso-para-agentes.md` (novo) e
  `docs/runbooks/segundo-cliente-mcp.md` com a matriz de compatibilidade.

## Checklist

### Backend e MCP
- [x] Campo de resultado possivelmente incompleto e dica na busca, inclusive só por palavras (DA-283)
- [x] Corrigir o texto "try again later" e anotar a DA-183 (DA-283)
- [x] `detail=summary` no `get_context`, padrão `full` (DA-285)
- [x] Mensagens de erro das tools com a próxima ação (DA-286)
- [x] Descrição de `apply_changes` com exemplos de várias operações e de tags em lote (DA-286)
- [x] `ServerInstructions` curtas, só apontando para o contrato (DA-284)
- [x] OpenAPI regenerado e commitado, se o contrato REST mudar (não se aplica, DA-311)

### Documentação
- [x] Guia de uso para agentes: ponteiro curto, resumo com link, convenção de envelhecimento; sem o Node de entrada nem o `reviewedAt`, que entram pelo E22 e pelo E20 (DA-287, DA-288, DA-309)
- [x] Roteiro de segundo cliente MCP e matriz de compatibilidade, sem lista fixa de clientes (DA-284)

### Glossário
- [x] Termo "resultado possivelmente incompleto" no `GLOSSARY.md`, se o nome do campo o justificar

### Testes obrigatórios
- [x] Unitário: o modo `summary` limita os campos e respeita o teto
- [x] Contrato com o cliente do SDK MCP: busca degradada devolve o campo e a dica, nunca erro nem repetição (DA-283, DA-284)
- [x] Contrato: nenhum comportamento crítico depende de `instructions` (DA-284)
- [x] Integração em Postgres real: busca com embeddings indisponíveis só devolve hits por palavras e marca incompleto
- [x] Integração: isolamento entre contas e Node oculto para agentes no `summary` (`NotFound`, nunca 403)
- [x] Adversarial: instrução dentro de um trecho do `summary` e de um hit de busca sai rotulada como dado (DA-289)
- [x] Nada de conteúdo de Node nas mensagens de erro nem nos logs (GEN-043)

## Critérios de saída

Um agente que busca durante uma degradação recebe o aviso de que a ausência não
está comprovada e a ordem de não duplicar. Ele consegue listar o que vale abrir
sem pagar o corpo de cada Node. Existe um guia que a pessoa cola no arquivo do
agente, e um roteiro para validar o contrato com um segundo cliente.

## Fora de escopo

Criação de Tags e Types por agente. Node de entrada (E22). Marcador "revisado por
você" (E20). Campo `validade` nativo. Projeção livre de campos. Tool de tag em
lote. Automação de testes por cliente e lista de clientes-alvo. Privacidade e
validação jurídica (E15). Varredura ampla de prompt injection (E15).
