# E8 — Tags, Collections e editor completo de Properties

- **Ordem:** 8
- **Depende de:** E2
- **Última revisão:** 2026-10-22

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

## Checklist

- [ ] Tags: CRUD, aplicar e remover via ChangeSet
- [ ] Ferramentas MCP de Tag (Write)
- [ ] Collections manuais e dinâmicas (critérios por Type, Property, Tag, Relation), como itens de navegação
- [ ] Editor completo de Properties: matriz de mudança de tipo, opções de Select, arquivar e restaurar, "usada em N Types"
- [ ] Promover/mapear Outras propriedades
- [ ] Reconciliar Collections salvas quando muda o tipo de uma propriedade
- [ ] Corrigir o teste instável herdado do E1,
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
