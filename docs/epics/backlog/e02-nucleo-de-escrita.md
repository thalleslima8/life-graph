# E2 — Núcleo de escrita do grafo

- **Ordem:** 2
- **Depende de:** E1
- **Última revisão:** 2026-10-02

## Contexto

O núcleo do produto: Nodes, Relations, Types e Properties, gravados por um
pipeline único que gera **GraphChangeSet + Provenance** desde a primeira
escrita. Proveniência e ChangeSet não podem ser adicionados depois, ou o
histórico e o Undo ficam com buracos. Este épico também traz a UI humana
mínima para inspecionar e desfazer, que precisa existir antes de qualquer
agente escrever (princípios "Human-visible result" e "Reversible automation" da
v2).

## Decisões

- **DA-013 — Pipeline único de escrita** (consenso, ADR: [docs/adr/0002-pipeline-unico-de-escrita-com-changeset.md](../../adr/0002-pipeline-unico-de-escrita-com-changeset.md)). Toda
  escrita, de qualquer ator (UI, API, MCP, import), passa por um único comando
  da camada de aplicação. Ele grava o `GraphChangeSet` + Provenance **na mesma
  transação** da mudança no grafo. Um comando que falha não persiste nada. Sem
  event sourcing completo: `changesets`/`change_entries` guardam o antes e o
  depois.
- **DA-014 — `assertion` (hard|soft) separado de `origin`
  (user|agent|system|import)** (consenso). A spec misturava os dois num só enum.
  Uma relação afirmada por agente é Hard com origin=agent. Soft Relations só
  vêm do sistema (E9) e ficam **fora** do log de ChangeSet (DA-070).
- **DA-015 — Exceção a DB-008: valores de Property em JSONB, com chave
  `property_id`** (consenso). A ontologia flexível da v2 (§7–8) não cabe num
  esquema fixo. Mitigações:
  - validação por Type na camada de aplicação;
  - chave por id (renomear não toca nos dados);
  - índice GIN `jsonb_path_ops` para igualdade;
  - filtros de intervalo varrem a partição da conta, o que é aceitável na
    escala de um grafo pessoal. Índices de expressão só se uma medição
    justificar.
  ADR: [docs/adr/0003-properties-em-jsonb-e-definicoes-da-account.md](../../adr/0003-properties-em-jsonb-e-definicoes-da-account.md)
- **DA-016 — Property Definitions pertencem à Account e são anexadas a Types
  (N:N)**, com **Outras propriedades** (consenso + usuário Q11=B). Ao mudar o Type de um Node, os valores cuja definição não está no
  novo Type continuam guardados e visíveis numa seção "Outras propriedades".
  Eles voltam sozinhos, pelo `property_id`, quando o Node retorna a um Type
  compatível. Entram no export, no purge, no FTS e no `get_context`. Isso evita
  perder dados ao reclassificar itens, o que é comum ao processar a Inbox.
  ADR: [docs/adr/0003-properties-em-jsonb-e-definicoes-da-account.md](../../adr/0003-properties-em-jsonb-e-definicoes-da-account.md)
- **DA-017 — NodeReference não é Property, é sempre Relation** (consenso,
  ADR: [docs/adr/0004-nodereference-sempre-como-relation.md](../../adr/0004-nodereference-sempre-como-relation.md)). Dois jeitos de ligar Nodes quebrariam a travessia, o
  filtro de ocultos (vazaria por valor de propriedade), o Undo e a
  explicabilidade.
- **DA-018 — Type é opcional no Node** (consenso + usuário). Todo Node tem
  `title` e `body` (markdown). Um Type padrão "Note" poluiria os filtros. A UI
  oferece um filtro "Sem Type".
- **DA-019 — Inbox é um estado explícito, com saída explícita** (consenso). O
  campo `inbox_entered_at` não é uma query derivada: uma regra como "sem Type e
  sem Relations" faria o item sair da Inbox como efeito colateral. "Arquivar" é
  um comando que gera ChangeSet e pode ser desfeito.
- **DA-020 — Undo é um ChangeSet compensatório** (consenso). Ele é recusado,
  mostrando os conflitos, quando uma alteração posterior tocou as mesmas
  entidades. Nunca sobrescreve às cegas.
- **DA-021 — Delete ≠ Purge** (consenso + usuário, ADR: [docs/adr/0005-delete-separado-de-purge.md](../../adr/0005-delete-separado-de-purge.md)).
  - Delete é um tombstone que pode ser desfeito por **30 dias** (usuário) em
    todos os planos. Essa janela é separada da retenção do histórico.
  - Ao fim da janela, o job de purge remove o Node e **apaga o conteúdo dos
    ChangeSets** que o tocaram, deixando só o esqueleto. Senão o histórico do
    Premium (1 ano) guardaria o conteúdo apagado.
- **DA-022 — Concorrência otimista (`version` por Node)** (consenso, BE-041).
  A UI e os agentes editam ao mesmo tempo.
- **DA-023 — 8 tipos de Property:** Text, Number, Boolean, Date, DateTime,
  URL, Select, MultiSelect (consenso). Status vira Select; Rating vira Number;
  Location vira Text ou Relation; TemporalAnchor vira propriedades de data.
  Até o editor completo (E8), **mudar o tipo de uma propriedade que já tem
  valores fica bloqueado**.
- **DA-024 — UI atualiza por polling/refetch + feed de ChangeSets com cursor
  (`GET changesets?since=`)** (consenso). Sem websocket, é o mesmo contrato que
  um SSE entregaria depois.

## Estrutura por camada

- **Domain:** Node, Relation, Type, PropertyDefinition, GraphChangeSet,
  ChangeEntry, Provenance; invariantes e validação por Type.
- **Application:** comandos de escrita (um caminho único), Undo, Delete,
  arquivar da Inbox, queries de lista, Inspector e feed.
- **Infrastructure:** EF Core + Npgsql, JSONB, worker de purge
  (`BackgroundService` + fila em Postgres com `FOR UPDATE SKIP LOCKED`) e
  tabela de outbox/jobs.
- **Web:** Lista, Inspector (incluindo Outras propriedades), Recent Changes,
  Undo, Inbox mínima e editor mínimo de Types/Properties.

## Checklist

### Fase 1 — Modelo e pipeline
- [ ] Entidades e migrations (nodes, relations, types, property_definitions, type_properties, changesets, change_entries)
- [ ] Comando único de escrita com ChangeSet + Provenance na mesma transação
- [ ] Validação de valores por Type; comando inválido não persiste nada
- [ ] Concorrência otimista por Node

### Fase 2 — Reversibilidade
- [ ] Undo compensatório com detecção de conflito
- [ ] Delete como tombstone + restauração dentro de 30 dias
- [ ] Job de purge (fim da janela) apagando o conteúdo dos ChangeSets
- [ ] Tabela de jobs/outbox e worker em processo

### Fase 3 — Types e Inbox
- [ ] CRUD mínimo de Types e Property Definitions (8 tipos), anexar e desanexar
- [ ] Troca de Type move valores para Outras propriedades e os restaura pelo `property_id`
- [ ] Estado de Inbox + comando "arquivar"
- [ ] Bloquear mudança de tipo de propriedade que já tem valores

### Fase 4 — UI mínima
- [ ] Lista com filtro "Sem Type", Inspector, Recent Changes com Undo, Inbox
- [ ] Feed de ChangeSets com cursor + refetch on focus

### Testes obrigatórios
- [ ] Atomicidade: uma falha no meio do comando não deixa ChangeSet nem dados parciais
- [ ] Undo com e sem conflito
- [ ] Ida e volta de Type preserva os valores
- [ ] Purge apaga o conteúdo do histórico
- [ ] Isolamento cross-account em todas as tabelas novas

## Critérios de saída

Um humano cria, edita, relaciona, apaga e desfaz pela UI. Toda alteração
aparece em Recent Changes com proveniência.

## Fora de escopo

Agentes (E3–E5), grafo 2.5D (E6), Resources (E7), Tags/Collections e editor
completo (E8), embeddings (E9).
