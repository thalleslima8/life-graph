# E6 — Grafo local 2.5D + Semantic Zoom básico

- **Ordem:** 6
- **Depende de:** E2
- **Última revisão:** 2026-10-21

## Contexto

A interface 2.5D é a representação humana do grafo (v2 §18–20, §64). Ela deixa
o usuário navegar pelo repertório que ele e os agentes construíram. O MVP
valida o grafo local (a partir de um Node, com profundidade controlável), o
Semantic Zoom básico e o Node Inspector, sem o universo visual inteiro.

## Decisões

- **DA-052 — A biblioteca de renderização é escolhida num spike no início do
  épico** (consenso). Critérios: FPS com 1k Nodes, Semantic Zoom, fallback
  acessível (visão em lista) e tamanho do bundle. A inclinação é por
  **Sigma.js + graphology** (WebGL, já que não há 3D no MVP). A alternativa é
  react-force-graph. O estado do grafo fica num renderer imperativo, fora do
  estado do React, para evitar re-renders.
- **DA-053 — Zero LLM na navegação e na renderização** (invariante da v2 §46).
  Layout, zoom e filtros são algoritmos tradicionais.
- **DA-054 — Atualizações em "tempo real" por polling do feed de ChangeSets**
  (DA-024). Nodes criados por agentes aparecem sem o usuário recarregar.
- **DA-137 — A biblioteca é escolhida em caráter provisório, sem medir FPS, e
  o FPS com 1k Nodes vira item de aceite manual** (consenso, no `/flow` de
  2026-10-04, retomado em 2026-10-20). O implementador não tem navegador real:
  jsdom não tem WebGL e o WebGL por software do Playwright só dá números
  relativos. Parar o run para o usuário medir não adiantaria a medição, que só
  acontece na revisão do PR. Por isso o spike escolhe pelos demais critérios da
  DA-052 (Semantic Zoom, fallback acessível, bundle) mais benchmarks
  publicados. Se os benchmarks preverem queda de FPS, vence a candidata mais
  conservadora em desempenho. O spike registra aqui a biblioteca escolhida e a
  candidata de reserva. Ele entrega um harness descartável fora do repositório
  (GEN-075), com 1k Nodes e 1,5k arestas em pan e zoom, para o usuário medir FPS
  médio e p5 num navegador real. Condições:
  - a biblioteca fica atrás de uma porta fina (`GraphRenderer`, DA-142), e um
    teste de arquitetura impede que outro arquivo a importe;
  - o critério de FPS da DA-052 fica registrado como **não validado** e é um
    risco aceito visível no PR; o épico não vai para `done/` sem a medição;
  - se a medição reprovar, troca-se o adapter pela candidata de reserva.
  - **Resultado do spike (2026-10-20): Sigma.js 3.0.3 + graphology 0.26.0**,
    com layout ForceAtlas2 (`graphology-layout-forceatlas2` 0.10.1). A
    **candidata de reserva é o force-graph 1.52.0**, a base em Canvas 2D do
    react-force-graph. O adapter o usaria direto, porque a porta
    `GraphRenderer` já é imperativa e o wrapper `react-force-graph-2d` só
    repassa props.
  - Bundle medido com um `vite build` descartável (minificado, gzip, acima de
    uma base só com React): Sigma 3 + graphology + ForceAtlas2, +38,6 KiB;
    `react-force-graph-2d`, +57,8 KiB; Sigma 4.0.3 + graphology + ForceAtlas2,
    +107,2 KiB. As três são MIT, e o `npm audit` não apontou vulnerabilidades.
  - Os benchmarks publicados não trazem um número comparável com 1k Nodes, só
    alegações de escala: o README do Sigma fala em "thousands of nodes and
    edges" em WebGL, e o do force-graph tem um exemplo de cerca de 75k
    elementos em Canvas. Nenhuma dessas fontes aponta queda de FPS em 1k Nodes.
    Mesmo assim, a candidata mais conservadora em desempenho é a Sigma, em
    WebGL.
  - Semantic Zoom (DA-140): a Sigma tem o evento `updated` da câmera,
    `nodeReducer`/`edgeReducer` e o limiar `labelRenderedSizeThreshold`. No
    force-graph, o equivalente é `onZoom` mais um `nodeCanvasObject`, que o
    README diz ser chamado para cada Node a cada quadro.
  - Hard vs Soft: a Sigma 3 não tem aresta tracejada nativa. A Soft se
    distingue por curva (pacote `@sigma/edge-curve`) e espessura, não só pela
    cor (FE-034), e o detalhe fica com o item de representação visual. O
    force-graph tem `linkLineDash`, e a Sigma 4 tem uma camada tracejada.
  - O ForceAtlas2 do graphology não usa aleatoriedade, então posições iniciais
    semeadas dão sempre o mesmo layout (GEN-072). No jsdom dos testes, o canvas
    não devolve contexto 2D nem WebGL, e sem contexto nenhuma das duas desenha.
    Por isso os testes trocam o renderer pelo `rendererFactory` da DA-142.
    Nenhuma traz fallback acessível, e a lista é nossa.
  - A Sigma 4.0 ficou de fora por ora: quase três vezes o bundle da 3.0, num
    major lançado em 2026-10-08 que recebeu três patches em uma semana. Migrar
    é uma tarefa explícita, contida no adapter.
  - O harness mede as três (Sigma 3.0.3, force-graph 1.52.0 e Sigma 4.0.3) com
    o mesmo grafo semeado e o mesmo trajeto de pan e zoom.
  - Candidata a ADR: escolha provisória com o risco não medido.
- **DA-138 — Grafo local por `GET /api/nodes/{nodeId}/graph?depth=1..3`, com
  exceção à API-060** (consenso na estrutura; o teto padrão é **provisório**,
  decidido pelo arquiteto). A rota é nova, em GraphReads, reaproveita o
  `ContextWalk` e passa pelo filtro central com principal Human. Não é auditada
  como a leitura de agente nem expõe o `get_context`, que tem orçamento de
  50 KB com bodies e foi feito para agentes.
  - O payload é enxuto, sem body. O teto é de 20 vizinhos por Node e o teto
    total vem de configuração: padrão de 500 Nodes / 1.500 arestas e teto
    rígido de 1.000 / 3.000, que nenhuma requisição nem configuração de runtime
    ultrapassa.
  - **Revisão do teto padrão (usuário, 2026-10-21):** com a medição da DA-137
    (Sigma 3 a 60 FPS, com 1 000 Nodes e 1 500 arestas), o padrão sobe para
    **1.000 Nodes / 1.500 arestas**. É a carga medida. O teto rígido de
    1.000 / 3.000 não muda. Isso confirma a visão do analista e encerra o
    caráter provisório do teto. Para reverter, volte os padrões de
    `Graph:LocalGraph` e o teste que os fixa.
  - A ordem de corte é determinística (DA-036), para a mesma requisição devolver
    o mesmo subgrafo. A resposta traz `truncated` e as contagens omitidas, e a
    UI mostra "N Nodes omitidos". Nodes ocultos ou fora do alcance do principal
    nunca entram nas contagens.
  - **Exceção à API-060 (DEVE):** o subgrafo limitado por profundidade e teto
    não é uma coleção paginável, porque um cursor sobre a travessia quebraria a
    consistência visual. Quem precisa de mais re-centra o grafo em outro Node.
  - Subir o padrão para 1.000 depende da medição de FPS da DA-137 e é só uma
    mudança de configuração. Outra visão (analista, que depois trocou de lado
    com o arquiteto): começar em 1.000, alinhado ao alvo da DA-052. Para
    reverter, muda-se o padrão na configuração e o teste que o fixa.
- **DA-139 — Tamanho do Node = grau de Relations Hard visíveis no grafo
  inteiro** (consenso). A contagem vem do servidor, em lote, no padrão do
  `NodeViews`, e passa pelo mesmo filtro de visibilidade do principal. Assim o
  grau não vaza a existência de Nodes ocultos. A escala tem teto (log ou
  faixas), para um hub não esmagar os demais. Contar só dentro do subgrafo
  devolvido faria o Node mudar de tamanho com a profundidade e o corte. O
  ranking da travessia decide o que entra, não o tamanho. Recência é o canal
  "intensidade" da v2 §18.
- **DA-140 — O Semantic Zoom básico é por distância, em 3 níveis, e não é
  navegação conceitual** (consenso). Afastado: rótulo só na raiz e na
  profundidade 1, o resto em pontos. Médio: todos os títulos, com o Type em cor
  ou forma. Próximo: Relation kinds e um resumo do Inspector. A hierarquia
  conceitual da v2 §19 (Minha Vida → Conhecimento → …) não existe no modelo, e
  agrupar por Type daria a impressão de uma hierarquia que não há. Ela fica para
  quando houver Collections (E8) ou clustering. Os limiares ficam numa função
  pura `levelOfDetail(zoom, distância)`, com constantes e teste unitário.
- **DA-141 — "Mudança recente de agente" = Node ou Relation tocado por um
  GraphChangeSet de agente ainda não revisado** (consenso). O estado já existe
  (DA-135) e é o mesmo da tela Agent Changes. Uma janela de tempo criaria um
  segundo conceito de "recente". O cálculo é do servidor, em lote, na leitura
  do grafo local, com um flag por Node e por Relation, e respeita o filtro de
  visibilidade.
  - Um ChangeSet desfeito não conta: o Undo já resolveu a questão.
  - Relations são destacadas também.
  - Nodes excluídos ou purgados não aparecem.
  - Uma edição do usuário depois da mudança do agente não limpa o flag, porque
    editar não é revisar. Só "Marcar revisado" limpa.
- **DA-142 — Interface dos módulos do grafo no frontend** (consenso; o
  design-an-interface teve três designs, o arquiteto escolheu e o analista
  validou depois de uma contra-argumentação). É o padrão da casa (DA-118,
  DA-135), mais um módulo puro de navegação.
  - `localGraphQuery(nodeId, depth)`, com a chave `graphKeys.localGraph` e
    `placeholderData: keepPreviousData`.
  - `useGraphView()`, com URL validada por Zod: `?depth` (padrão 2), `?view`
    (`canvas | list`) e `?selected`, este gravado com replace. Inclui
    `graphPath(id)` e `levelOfDetail` (DA-140).
  - `graphNavigation.ts` é puro: `buildGraphIndex`, `nextFocus` e `keyAction`.
    As duas views usam o mesmo modelo de teclado.
  - `GraphCanvas` é controlado: `graph`, `rootId`, `selectedId`,
    `highlightAgent`, `zoom`, `onZoomChange`, `onSelect`, `onReroot?`,
    `labels`, `onUnavailable?`, `onRenderError?` e `rendererFactory?`. Ele usa a
    porta `GraphRenderer` (`mount`, `update` por id que preserva posições,
    `dispose`).
  - `GraphList` tem as mesmas props, sem as de zoom.
  - `NodeInspector` é embutível: `nodeId`, `headingLevel`, `onClose?`,
    `onNavigate?` e `readOnly?`. `NodeInspectorPage` vira o wrapper.
  - `useChangeSetProbe()` devolve `{ hasNewer, acknowledge }`.
  - A rota é `/nodes/:nodeId/graph` e a raiz vem do caminho.
  - Clique, Enter ou setas selecionam e abrem o Inspector. Duplo clique,
    Shift+Enter ou o botão "Centralizar aqui" re-centram, navegando.
  - A página tem cinco estados: carregando, não encontrado (idêntico para
    qualquer causa), erro com nova tentativa, vazio com "Criar Relation", e
    sucesso.
  - Novidades de agentes aparecem num banner "Atualizar", sem refetch
    automático, para o layout não se mover enquanto o usuário inspeciona.
  - Um `selected` que sumiu é limpo e anunciado. Se a renderização falhar, a
    página cai na lista.
  - O zoom fica fora da URL, porque é estado transitório de visualização.
  - Candidata a ADR: o renderer atrás de uma porta com factory injetada, e a
    raiz no caminho, com re-centrar como navegação.
- **DA-143 — Modelo de teclado das vistas do grafo: emenda à redação da
  DA-142** (consenso). A DA-142 dizia "setas selecionam" e também que
  selecionar leva o foco ao Inspector. As duas coisas juntas tirariam o usuário
  de teclado da vista a cada seta (FE-031). Valem estas regras:
  - **Setas** movem a posição do teclado entre vizinhos (`nextFocus`). No
    desenho, o Node fica destacado por uma moldura e é anunciado num
    `role="status"`. Na lista, o foco do DOM se move. **Enter** ou clique
    selecionam, abrem o Inspector e levam o foco a ele.
  - **Escape** devolve o teclado ao Node selecionado, ou à raiz se não houver
    seleção. Ele não limpa a seleção nem fecha o Inspector. Fechar o Inspector
    é trabalho dele, e o foco volta à vista.
  - **"Cada vista é dona dos seus papéis e do seu modelo de foco"**:
    `aria-activedescendant` é um mecanismo possível, não uma exigência.
    - Na lista, cada Node é um botão nativo com foco real, ao lado do seu
      "Centralizar aqui" e das suas Relations.
    - O desenho é um `role="application"` com ajuda visível ligada por
      `aria-describedby`.
    - Não há elementos ocultos por Node, porque duplicariam o grafo no DOM.
  - Testes: o anúncio no `status`, a ajuda ligada por `aria-describedby` e o
    retorno do foco do Inspector à vista.
  - Aceite manual: `role="application"` desliga os atalhos do leitor de tela,
    então vale um teste com NVDA ou VoiceOver. A lista é o fallback acessível
    (FE-030) e reduz esse risco.
    - Feito em 2026-10-21 com o NVDA e o Chrome 155 no Windows, e tudo passou.
      No desenho: entrar com Tab anuncia o grafo e a ajuda de teclado; ↑/↓ e
      →/← andam pelos Nodes com fala; +/− anunciam o nível de zoom e 0
      restaura; Enter seleciona e leva o foco ao Inspector; Shift+Enter
      centraliza; Esc devolve o teclado ao Node selecionado, ou à raiz sem
      seleção. Shift+Tab volta do Inspector para o desenho, e "Fechar" fecha o
      painel e devolve o foco à vista. A Lista é lida por grupos de distância,
      com o tipo da Relation e Hard/Soft.
    - Observação de uso: com o foco dentro do Inspector, Esc não faz nada,
      como manda a DA-143. Para voltar à vista é preciso Shift+Tab ou "Fechar".
    - Nota de ambiente: o NVDA ficou mudo em qualquer site, Gmail incluído, até
      o Chrome ser reaberto com o NVDA já ligado. Não é defeito do app.

## Checklist

- [x] Spike comparando as bibliotecas e registrando a escolha
- [x] Grafo local a partir de um Node, com profundidade 1/2/3
- [x] Representação visual: tamanho = relevância, proximidade = relação, Hard vs Soft distinguíveis (Soft entra no E9)
- [x] Semantic Zoom básico (nível conceitual muda com o zoom)
- [x] Node Inspector integrado ao grafo
- [x] Destaque de mudanças recentes de agentes
- [x] Fallback acessível em lista (FE-030), navegação por teclado
- [x] Medição de FPS com 1k Nodes em navegador real (DA-137)
  - Feita em 2026-10-21 com o harness da DA-137 (1 000 Nodes e 1 500 arestas,
    22 s de pan e zoom automáticos). O navegador foi o Chrome 155 no Windows,
    com viewport 2048×926 e devicePixelRatio 0,9375. O Sigma.js 3.0.3 (WebGL)
    deu FPS médio 60,1, p5 59,5 e pior quadro de 17 ms, em 1 201 quadros. É o
    teto de uma tela de 60 Hz, sem queda. A biblioteca escolhida fica, e a
    reserva (force-graph) não é necessária.
  - Comparação nas mesmas condições: o force-graph 1.52.0 (Canvas 2D) deu
    60,1, p5 59,2 e pior quadro de 17,2 ms, em 1 201 quadros. O Sigma.js 4.0.3
    deu 60,1, p5 59,2 e pior quadro de 18,9 ms, em 1 143 quadros. As três
    batem no teto de 60 Hz, então com 1k Nodes o FPS não as diferencia. A
    escolha da DA-137 se confirma pelos outros critérios: menor bundle
    (+38,6 KiB) e Semantic Zoom pela câmera e pelos reducers.

### Ajuste depois do aceite
- [ ] Subir o padrão do grafo local para 1.000 Nodes / 1.500 arestas (revisão da DA-138, usuário): `LocalGraphReads` (`MaxNodes`) e o teste que fixa os padrões

## Critérios de saída

O usuário entra num Node, navega pelos vizinhos em 2.5D, vê aparecer o que um
agente acabou de criar e abre o Inspector.

Aceite feito em 2026-10-21 com o Claude Code no host. Com "Exigir aprovação
para escritas" desligado, o agente criou direto o Node «Linha Oeste» e uma
Relation com Ferrovias. No grafo local de Ferrovias, os dois apareceram como
"mudança de agente não revisada". O Inspector mostrou o aviso e o link
"Revisar em Agent Changes", e depois de "Marcar revisado" o destaque sumiu. A
pessoa navegou e usou o Semantic Zoom. Com a aprovação ligada, o destaque não
aparece, e isso é esperado: o ChangeSet nascido de uma aprovação já sai
revisado (E5).

## Fora de escopo

Grafo global/3D, clustering semântico avançado.
