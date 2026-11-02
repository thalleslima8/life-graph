# E19 — Home com o grafo

- **Ordem:** 19 (executa **logo depois do E21**, antes do E20 e do E14, DA-305)
- **Depende de:** E5, E6, E10
- **Última revisão:** 2026-10-31

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

## Estrutura proposta

- `web/src/features/home/`: `HomePage` com o canvas e o painel de entrada,
  hooks de leitura sobre o cliente gerado, componente de estado vazio.
- Reuso de `web/src/features/graph/GraphCanvas.tsx`, sem fork.
- Backend só se o endpoint de grafo local não aceitar limite (DA-272).

## Checklist

### Home
- [ ] Verificar se `GET /nodes/{id}/graph` aceita limite de profundidade e de Nodes (DA-272)
- [ ] Se não aceitar, adicionar o limite ao endpoint, com teste
- [ ] `HomePage` carrega a Raiz por `GET /account-root` e mostra o grafo local dela (DA-270)
- [ ] Raiz rotulada "Seu grafo", 1 nível, teto de Nodes (DA-272)
- [ ] Painel de entrada com links e atalhos (DA-271)
- [ ] Decidir a fonte dos contadores do Inbox e de Desancorados, ou cortá-los da v1 (DA-271)
- [ ] Estado vazio com ações, ignorando a Raiz (DA-273)
- [ ] Estados de carregando e erro na Home
- [ ] Usar só os tokens de design já existentes, sem cores nem espaçamentos literais, para a migração do E16 ficar barata (DA-305)
- [ ] Textos novos no catálogo (FE-062)

### Testes obrigatórios
- [ ] Componente: 0 Nodes além da Raiz mostra o estado vazio; com Nodes mostra o canvas
- [ ] Contadores e critério de vazio ignoram a Raiz
- [ ] axe na Home nos estados vazio, com grafo e erro
- [ ] Integração: a Home lê só o que o filtro central permite (outra conta e Node oculto)
- [ ] E2E de fumaça: login leva à Home com o grafo

## Critérios de saída

Abrir o Life Graph depois do login mostra o grafo da conta com o painel de
entrada, e uma conta nova mostra o estado vazio com as ações. Nenhuma leitura da
Home chama modelo de linguagem.

## Fora de escopo

Identidade visual e tokens (E16, E17). Fila de revisão e decisão em lote (E20).
Endpoint agregador de Home. Semantic Zoom novo ou 3D. Home personalizável ou
configurável pelo usuário.
