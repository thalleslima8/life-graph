# E20 — Agent Changes: fila de revisão e decisão em lote

- **Ordem:** 20 (executa **depois do E19**, antes do E22 e do E14, DA-305)
- **Depende de:** E5, E13
- **Última revisão:** 2026-10-31

## Contexto

A tela Agent Changes mostra duas listas: as ChangeProposals, com um filtro de
situação, e os GraphChangeSets. Aprovar ou rejeitar só existe no detalhe de cada
ChangeProposal (`ProposalPage.tsx`), uma por vez, e não há ação em lote. Com
vários agentes escrevendo, revisar uma a uma ficou trabalhoso. Além disso, o
que aguarda a decisão da pessoa não se destaca do que já foi resolvido.

Este épico separa o que **aguarda você** do histórico e permite marcar várias
ChangeProposals e aprová-las ou rejeitá-las de uma vez. Não muda o modelo do
E5: cada ChangeProposal continua sendo decidida pelo mesmo caso de uso, pelo
pipeline único (DA-013).

O E13 (Entitlements) está em andamento e não é tocado. O E20 só depende dele
para provar que a decisão em lote não consome a quota de escrita do agente.

## Decisões

- **DA-276 — "Fila de revisão" é a visão padrão da tela** (consenso).
  - Lista as ChangeProposals em Proposed e **não expiradas**, a que expira
    primeiro no topo, com "expira em X" nas próximas do prazo e a contagem no
    item de menu. O histórico (aplicadas, rejeitadas, expiradas, retiradas e
    GraphChangeSets) fica numa segunda aba.
  - Uma ChangeProposal expirada nunca aparece na fila, mesmo antes do job de
    expiração rodar (a leitura já calcula a expiração efetiva). O filtro do
    backend usa o mesmo critério.
  - Por quê: aprovar uma expirada falharia, e a pessoa precisa ver primeiro o
    que depende dela. "Fila de revisão" é só uma visão filtrada, sem estado nem
    entidade nova (`GLOSSARY.md`).
- **DA-277 — Decisão em lote: N decisões independentes, sem GraphChangeSet
  agregado** (consenso).
  - Endpoint novo, adapter fino sobre o mesmo caso de uso de aprovar e rejeitar,
    com uma transação por item e resultado por item (aplicada, rejeitada, já
    decidida, expirada, requer confirmação, não encontrada).
  - Não é atômico: uma ChangeProposal expirada derrubaria todas as outras.
  - Cada ChangeProposal aprovada mantém seu GraphChangeSet, sua Provenance e seu
    Undo individual. Não existe `BatchId`: o lote é conveniência de interface.
  - Há teto de itens por requisição (valor definido no épico). Item já decidido
    devolve o estado atual, e ownership responde `NotFound`. O resultado não
    carrega conteúdo de Node (GEN-043).
  - Depois do lote a tela recarrega a fila, porque a seleção pode ter ficado
    velha.
- **DA-278 — Ações sensíveis fora do "selecionar todos"** (recomendação dos dois
  especialistas; o usuário autorizou a escrita do épico sem objetar, **a
  confirmar**).
  - ChangeProposals com operação sensível (ex.: excluir Node) não entram em
    "selecionar todos". Só são aprovadas em lote se a pessoa as marcar uma a
    uma, com confirmação explícita que cita quantas exclusões serão feitas.
  - O backend exige o sinal de confirmação para esses itens: sem ele o item
    volta como "requer confirmação" e nada é aplicado. A tela não é a única
    defesa.
  - Rejeitar em lote pede só uma confirmação simples com a contagem, porque a
    rejeição não altera o grafo e não tem volta.
- **DA-279 — O lote decide a ChangeProposal inteira** (consenso).
  - A aprovação parcial por operação continua só no detalhe, com o aviso "para
    aprovar só parte, abra a proposta".
  - Como a pessoa aprova sem abrir, cada item da lista mostra um resumo: agente,
    número e tipos de operação.
- **DA-280 — Decidir, rejeitar e desfazer em lote sempre no Free, sem consumir
  quota** (consenso).
  - Aprovar pela pessoa é `ApprovedByThePerson` e não consome a janela de
    operações do agente (DA-132). O lote herda a mesma exceção.
  - Teste de integração com a quota de escrita do E13 esgotada: a decisão em
    lote continua funcionando.
- **DA-281 — Épico novo e dedicado, antes do E14** (usuário; consenso).
  - Não entra no E13 (tema alheio e em andamento) nem no E19 (outro tema). A
    ordem entre E19 e E20 é livre, desde que ambos fiquem antes do E14.
  - O número é maior que o do E14, mas a regra de dependência vale para
    dependências, não para a ordem de execução. O README registra a ordem
    (o E21 roda antes, DA-290).
- **DA-282 — Marcador "revisado por você", sem mudar `trust`** (consenso; o
  usuário o quis dentro do E20).
  - A confiança é binária: Trusted só se a Source for o usuário, e um Node escrito
    por agente fica untrusted para sempre (DA-038). Isso não muda. O marcador é
    **outra coisa**: "revisado por você em tal data" (`reviewedAt`), separado do
    `trust`.
  - Só um Human marca. Uma edição posterior por AgentIdentity **invalida** o
    marcador. O "marcar como revisada" do GraphChangeSet, que já existe e não
    altera `trust`, passa a poder gerar o marcador nos Nodes que ele criou ou
    alterou (o plano fecha se é por Node ou por GraphChangeSet).
  - O indicador aparece em `get_node`, no `get_context` completo e no Node
    Inspector. O `detail=summary` do E21 recebe o campo no épico que rodar por
    último, sem dependência formal (o E20 tem número menor).
  - Serve de sinal de envelhecimento (convenção por Property da pessoa, E21,
    DA-288) e de peso para o agente ao ler conteúdo de Resource (E22, DA-296),
    sem esconder nada.
  - Por quê: o agente e a pessoa precisam distinguir "um agente escreveu isso" de
    "eu conferi isso", e promover o Node a Trusted apagaria a proveniência.

## Estrutura proposta

- `src/LifeGraph.Graph/`: endpoint de decisão em lote em
  `Http/AgentChangeEndpoints.cs`, contrato de entrada e resultado por item em
  `Contracts/`, filtro "fila de revisão" na consulta de `GET /proposals`.
- `web/src/features/agents/`: `AgentChangesPage` com a fila e o histórico em
  abas, seleção múltipla, barra de ação do lote e resumo de cada item.
- Marcador de revisão: coluna e invalidação no pipeline único (DA-013) em
  `src/LifeGraph.Graph/`, ação "Marcar como revisado" no Node Inspector e no
  "marcar como revisada" do GraphChangeSet, campo `reviewedAt` nos retornos de
  leitura (`src/LifeGraph.Agents/`).

## Checklist

### Backend
- [ ] Filtro "fila de revisão" em `GET /proposals`: Proposed e não expirada, ordenada por expiração (DA-276)
- [ ] Contrato do lote com resultado por item e teto de itens (DA-277)
- [ ] Endpoint de aprovação e de rejeição em lote, sobre o caso de uso existente (DA-277)
- [ ] Sinal de confirmação para itens sensíveis, exigido no backend (DA-278)
- [ ] Idempotência por item e `NotFound` para outra conta (DA-277)
- [ ] Migration e RLS do marcador `reviewedAt`, separado do `trust` (DA-282)
- [ ] Só Human marca; edição posterior de AgentIdentity invalida, pelo pipeline único (DA-282)
- [ ] "Marcar como revisada" do GraphChangeSet pode gerar o marcador (DA-282)
- [ ] `reviewedAt` em `get_node` e no `get_context` completo; no `summary` do E21 se já existir (DA-282)
- [ ] Marcador entra no filtro de leitura, no purge e no export (regra transversal)

### Frontend
- [ ] Aba "Aguardando você" como padrão e aba de histórico (DA-276)
- [ ] Contagem no menu e "expira em X" (DA-276)
- [ ] Seleção múltipla e "selecionar todos" que exclui as sensíveis (DA-278)
- [ ] Resumo por item: agente, nº e tipos de operação (DA-279)
- [ ] Confirmações de aprovar e de rejeitar em lote, com contagens (DA-278)
- [ ] Resultado por item e recarga da fila depois do lote (DA-277)
- [ ] Aviso de aprovação parcial só no detalhe (DA-279)
- [ ] Ação "Marcar como revisado" e indicador de revisão no Node Inspector (DA-282)
- [ ] Usar só os tokens de design já existentes, sem cores nem espaçamentos literais, para a migração do E16 ficar barata (DA-305)
- [ ] Textos novos no catálogo (FE-062)

### Glossário
- [ ] Termo "Fila de revisão" no `GLOSSARY.md`
- [ ] Termo "revisado por você" no `GLOSSARY.md` (DA-282)

### Testes obrigatórios
- [ ] Unitário: decisão por item (expirada, já decidida, sensível sem confirmação)
- [ ] Integração em Postgres real: falha parcial, um GraphChangeSet por ChangeProposal aprovada e Undo individual
- [ ] Integração: isolamento entre contas (RLS) no lote
- [ ] Integração: lote com a quota de escrita do E13 esgotada (DA-280)
- [ ] Frontend: seleção, "selecionar todos" sem as sensíveis, resultado por item
- [ ] axe na lista com checkboxes
- [ ] Unitário: o marcador nunca altera `trust`; edição de agente o invalida; agente não consegue marcar (DA-282)
- [ ] Integração em Postgres real: marcar, invalidar por edição de agente e isolamento entre contas (RLS) (DA-282)
- [ ] Contrato MCP: nenhuma tool marca como revisado e o indicador aparece nos retornos (DA-282)
- [ ] Adversarial: Node escrito por agente com instrução fica sem o marcador até a pessoa marcar (DA-282)

## Critérios de saída

A tela Agent Changes abre na fila do que aguarda a pessoa. Ela marca várias
ChangeProposals e as aprova ou rejeita de uma vez, vê o resultado de cada uma, e
cada ChangeProposal aprovada continua desfazível individualmente. Ações
sensíveis nunca entram no lote sem confirmação explícita. A pessoa marca um Node
como revisado por ela, o agente enxerga isso, e o `trust` fica como estava.

## Fora de escopo

Aprovação parcial por operação em lote. Desfazer o lote inteiro de uma vez.
Entidade ou identificador de lote. Aprovação automática por regra ou por
agente. Mudanças na quota do E13. Notificação fora da tela (e-mail, push).
Promover Node de agente a Trusted. Validade nativa (`validUntil`).

- [ ] Atualizar `docs/guias/guia-de-uso-para-agentes.md` com o sinal `reviewedAt` (DA-309 do E21)
