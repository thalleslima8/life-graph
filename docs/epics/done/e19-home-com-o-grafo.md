# E19 — Home com o grafo

- **Ordem:** 19 (executa **logo depois do E21**, antes do E20 e do E14, DA-305)
- **Depende de:** E5, E6, E10
- **Última revisão:** 2026-11-03

## Contexto

Quem abre o Life Graph depois do login cai numa Home que é um stub: um título e
o texto "Seu grafo de contexto pessoal." (`web/src/features/home/HomePage.tsx`).
O grafo local existe e funciona (E6), mas só em `/nodes/:nodeId/graph`, e a Home
não leva até ele. Nenhum épico definiu o conteúdo da Home. O E16 só pedia que ela
"deixasse de ser stub", sem dizer o que passa a mostrar.

O grafo é o produto e a visualização é o foco principal do sistema. A Home tem de
mostrar o grafo ao abrir. Este épico define o conteúdo da Home e a implementa
com o que o backend já oferece.

## Decisões

- **DA-270 — A Home é o grafo local ancorado na Raiz da Conta** (usuário;
  consenso dos especialistas).
  - Reaproveita `GraphCanvas` e `GET /nodes/{id}/graph`, com o id vindo de
    `GET /account-root`. A Home é o foco principal de visualização.
  - Por quê: "o grafo é o produto" e a lacuna era de composição, não de
    capacidade. Redirecionar para outra tela esconderia a lacuna, e um painel
    sem grafo tiraria o grafo da primeira impressão.
- **DA-271 — Painel de entrada leve ao lado do grafo** (consenso).
  - Atalhos e links para Inbox, Desancorados, Recent e Agent Changes, e ações de
    criar Node e conectar agente. Só leituras (GET), nada de LLM ao abrir a tela.
  - Contadores só se houver contagem barata. Sem ela, a v1 mostra só links, e a
    fonte da contagem do Inbox vira DA próprio (total na lista ou endpoint de
    contagem). Não se cria endpoint agregador de "dashboard" na v1.
  - A contagem "aguardando você" do E20, se existir, usa a mesma consulta da
    Fila de revisão: a regra "Proposed e não expirada" vive num lugar só.
- **DA-272 — Recorte do grafo na Home** (consenso; proposto pelo analista, com
  o arquiteto de acordo; o usuário não contestou).
  - A Raiz da Conta aparece rotulada "Seu grafo", com 1 nível de profundidade e
    um teto de Nodes, reaproveitando o limite que o endpoint de grafo local já
    tiver.
  - Por quê: sem teto, a Home vira uma bola de fios em contas grandes. Se o
    endpoint não aceitar limite de profundidade e de Nodes, a mudança de backend
    entra neste épico.
- **DA-273 — Estado vazio orientado à ação; a Raiz não conta como conteúdo**
  (consenso).
  - Conta sem Node além da Raiz não mostra um canvas com um ponto solitário.
    Mostra o estado vazio com ações: criar o primeiro Node e conectar um agente
    (o fluxo central do MVP).
  - O critério de "vazio" e os contadores ignoram a Raiz, para uma conta nova não
    mostrar "1 Node". O texto exato é do E17 (microcopy); a estrutura e as ações
    são deste épico.
- **DA-274 — Sem endpoint novo na v1** (consenso).
  - As chamadas rodam em paralelo no TanStack Query e cada uma passa pelo filtro
    central de leitura. Um agregador `/home` só entra se o spike medir lentidão.
- **DA-275 — Posição: antes do E14, número E19; o E16 deixa de listar a Home**
  (usuário).
  - O épico depende só de concluídos (E5, E6, E10). Nenhum épico menor depende
    dele; a ordem de execução está no README.
  - O E16 adota os tokens e o layout nas telas existentes, e a Home entra nessa
    migração quando ele rodar. A Home deixa de "sair do stub" no E16.

- **DA-312 — Critério de vazio e terceiro estado "sem ligação à Raiz"**
  (consenso).
  - "Vazio" é `GET /nodes?limit=2` sem nenhum Node além da Raiz. Se há Nodes mas
    nenhum ligado à Raiz, a Home mostra um terceiro estado que leva a
    Desancorados com a contagem da query de Desancorados. O hook não decide
    entre vazio e terceiro estado antes de essa leitura resolver; falha nela vira
    erro, nunca um vazio falso. Se só a contagem falhar, o link aparece sem número.
  - Por quê: pela DA-196 todo Node criado pela UI nasce sem âncora, então o
    terceiro estado é o comum. Tratá-lo como vazio diria "crie seu primeiro
    Node" a quem já tem Nodes.
- **DA-313 — "Criar Node" na Home é um link para `/nodes`** (consenso).
  - Sem formulário inline, sem `returnTo`. O Node nasce sem âncora (DA-196) e a
    Home passa a mostrar o terceiro estado com a contagem atualizada. O texto não
    promete que o Node "entra no grafo".
  - Por quê: um formulário inline duplicaria validação e caminho de escrita e
    abriria uma exceção à DA-196. Criar já ancorado na Raiz fica para um épico
    próprio, se o usuário quiser.
- **DA-314 — Contadores da v1: só Desancorados** (consenso).
  - Desancorados usa o `totalCount` da query existente; Inbox é link sem número;
    sem número enquanto carrega ou em erro. Sem mudança de API.
  - Por quê: contar o Inbox exigiria mudar a API, e a DA-274 e a DA-271 pedem
    só o que é barato.
- **DA-315 — A Home é ponto de entrada do grafo** (consenso).
  - Selecionar leva a `/nodes/{raizId}/graph?depth=1&selected={id}`; reancorar
    leva a `/nodes/{id}/graph`. Mantém zoom, legenda e a queda para a lista se o
    desenho falhar; sem controle de profundidade nem Inspector embutido. Nunca
    chama `rememberLastFocus`. Mostra a linha de truncamento quando o servidor
    cortar os vizinhos (teto de 20).
  - Por quê: evita uma segunda superfície de seleção para manter.
- **DA-316 — Smoke de fumaça em Vitest + MSW; E2E em navegador real adiado**
  (consenso).
  - O teste de rota cobre login → "/" → Home com o grafo. Não exercita cookie
    BFF, proxy nem renderização real, e o épico não o chama de E2E. A harness de
    E2E em navegador real (Playwright contra a SPA e o Host) é trabalho de épico
    próprio. Aceite manual enquanto isso.
  - Por quê: o Host não serve a SPA e `web/` não tem Playwright; montar isso
    seria infraestrutura nova, fora do tamanho de um épico de Home. Risco a
    confirmar pelo usuário na revisão.
- **DA-317 — h1 da Home é "Seu grafo"** (consenso).
  - `routes.test.tsx` muda (a regra do stub "Life Graph" deixa de valer), em
    duas asserções, mais o teste de axe. Só existe um h1 por tela; o rótulo da
    Raiz no canvas não é heading. O texto final é do E17.
  - Por quê: o stub tinha h1 da marca; a Home agora é a tela do grafo.
- **DA-318 — Interface dos módulos da Home** (consenso; arquiteto escolheu,
  analista validou).
  - `useTypeNames` vai para `graphData.ts`; `LocalGraphView`, `ZoomControls` e
    `GraphLegend` vão para `graph/LocalGraphParts.tsx` com props inalteradas e a
    `LocalGraphPage` passa a importá-los. `useHomeGraph()` devolve a união
    `loading | error{retry} | empty | unlinked | graph{rootId, rootTitle, graph}`
    (Raiz ausente é `error`). `HomeEmptyState({variant})` lê a contagem sozinho;
    `HomeEntryPanel({onConnectAgent})` aparece em todos os estados, inclusive
    erro; `HomePage()` sem props.
  - Por quê: uma fonte única para canvas, lista, zoom e queda (FE-010) em vez
    de duplicar composição; o painel sempre visível mantém os atalhos mesmo com o
    grafo falhando.
  - Nota (revisão): o painel virou `HomeEntryPanel()` sem props; "Conectar
    agente" é um link para `/agentes`, como no estado vazio.

## Estrutura proposta

- `web/src/features/home/`: `HomePage` com o canvas e o painel de entrada,
  hooks de leitura sobre o cliente gerado, componente de estado vazio.
- Reuso de `web/src/features/graph/GraphCanvas.tsx`, sem fork.
- Backend só se o endpoint de grafo local não aceitar limite (DA-272).

## Checklist

### Home
- [x] Verificar se `GET /nodes/{id}/graph` aceita limite de profundidade e de Nodes (DA-272)
- [x] Se não aceitar, adicionar o limite ao endpoint, com teste
- [x] `HomePage` carrega a Raiz por `GET /account-root` e mostra o grafo local dela (DA-270)
- [x] Raiz rotulada "Seu grafo", 1 nível, teto de Nodes (DA-272)
- [x] Painel de entrada com links e atalhos (DA-271)
- [x] Decidir a fonte dos contadores do Inbox e de Desancorados, ou cortá-los da v1 (DA-271)
- [x] Estado vazio com ações, ignorando a Raiz (DA-273)
- [x] Estados de carregando e erro na Home
- [x] Usar só os tokens de design já existentes, sem cores nem espaçamentos literais, para a migração do E16 ficar barata (DA-305)
- [x] Textos novos no catálogo (FE-062)

### Testes obrigatórios
- [x] Componente: 0 Nodes além da Raiz mostra o estado vazio; com Nodes mostra o canvas
- [x] Contadores e critério de vazio ignoram a Raiz
- [x] axe na Home nos estados vazio, com grafo e erro
- [x] Integração: a Home lê só o que o filtro central permite (outra conta e Node oculto)
- [x] Smoke de rota (Vitest + MSW): login leva à Home com o grafo (DA-316)

O E2E em navegador real (Playwright contra a SPA e o Host) fica adiado para
épico próprio (DA-316); até lá, aceite manual.

## Critérios de saída

Abrir o Life Graph depois do login mostra o grafo da conta com o painel de
entrada, e uma conta nova mostra o estado vazio com as ações. Nenhuma leitura da
Home chama modelo de linguagem.

## Fora de escopo

Identidade visual e tokens (E16, E17). Fila de revisão e decisão em lote (E20).
Endpoint agregador de Home. Semantic Zoom novo ou 3D. Home personalizável ou
configurável pelo usuário.
