# E16 — Fundação visual e acabamento do frontend

- **Ordem:** 16 (executa **depois do E23, antes do E14**, DA-305; a posição estava a definir na DA-226)
- **Depende de:** E2, E6, E8, E10
- **Última revisão:** 2026-10-31

## Contexto

O web de hoje é funcional e cru: 16 variáveis shadcn neutras (oklch, sem croma)
em `index.css`, sem modo escuro, sem fontes, sem favicon, sem logo, sem
biblioteca de ícones (três `<svg>` inline), estados vazios em texto simples,
sem toasts e com responsividade mínima. O grafo 2.5D tem 11 cores hardcoded em
`graphVisuals.ts`, e Hard e Soft Relation diferem só por linha e espessura.

A marca virá da **LimaJ Solutions** (repositório `limaj-solutions`): logo da
lima em roda de 24 dentes, paleta Bagasse/Carbon/Pith Forest/Lima Cítrico/Latão,
Big Shoulders Display + Newsreader + JetBrains Mono, ícones próprios 24×24. O
usuário vai gerar o logo, as fontes, a paleta e os demais assets no **Claude
Design**, depois. A marca da LimaJ é editorial: não define modo escuro, tokens de
UI (foco, erro, superfícies, densidade de app), e o Lima `#B8D831` sobre papel
claro não passa em contraste de texto.

Este épico faz tudo o que **não depende** desses assets e prepara a entrega:
um contrato de tokens que a paleta futura só preenche, o código inteiro
consumindo esse contrato, o gate que valida contraste com qualquer paleta, os
componentes transversais de acabamento e o **brief de handoff** para o Claude
Design. A integração dos assets reais é o [E17](e17-integracao-da-marca.md).

O E11 (SharedGraphView) sai com a marca neutra provisória, por já estar em
execução. A página do visitante entra na migração para tokens quando este
épico rodar.

## Decisões

- **DA-224 — Dois épicos: fundação (E16) e integração da marca (E17)**
  (consenso; o arquiteto mudou de posição na contra-argumentação).
  - O E16 é executável agora e fecha sozinho. O E17 depende de uma entrega
    externa (os assets do Claude Design) e nasce em `backlog/`.
  - Por quê: um épico com fase bloqueada por input externo nunca fecha, e o
    `/flow` implementa épicos inteiros até o PR. Dois épicos mantêm o quadro de
    estado das pastas honesto.
- **DA-225 — Marca: o Life Graph é produto da LimaJ, sem "by Limaj Solutions"**
  (usuário).
  - O nome "Life Graph" fica. O logo, as fontes, a paleta e os demais assets
    saem do Claude Design, gerados pelo usuário. O épico não desenha nem
    escolhe nenhum desses assets.
  - Registrar o Life Graph no `limaj-solutions` é tarefa futura do usuário, fora
    deste épico.
- **DA-226 — Posição no roadmap a definir; número E16 sem renumerar** (usuário).
  - O E11 já está em execução, então o E16 não entra antes dele. Recebe o
    próximo número livre e depende só de épicos concluídos. Nenhum épico
    depende dele, o que cumpre a regra de dependência por número.
  - Quando o usuário decidir onde ele entra, renumera-se numa passada só
    (README, DAs que citam épicos, ADRs).
- **DA-227 — Contrato de tokens por papel, com valores placeholder**
  (consenso).
  - Os nomes e os papéis são o contrato: superfície, texto, borda, foco, erro,
    aviso, sucesso, destaque de seleção, Hard Relation, Soft Relation, agente,
    Node oculto, Raiz da Conta, Desancorado, estados de proposta (pendente,
    aceita, rejeitada) e Types categóricos. Os valores atuais neutros ficam como
    placeholder.
  - Trocar a paleta deve ser só editar valores. Um teste exige todos os papéis
    preenchidos nos dois temas.
  - Os componentes consomem só tokens. Nenhum hex fora do arquivo de tokens.
  - O contrato de nomes vive num arquivo só, fonte de verdade do código e do
    brief.
- **DA-228 — Fonte única em CSS variables, com leitor para o canvas**
  (consenso).
  - O Sigma não lê `var()`. Um módulo pequeno do grafo lê os valores com
    `getComputedStyle`, normaliza (oklch para hex/rgb se preciso), produz o
    objeto de cores do renderer e relê quando a classe do tema muda no `html`.
  - Os 11 hex de `graphVisuals.ts` viram tokens (`--graph-*`). Hard e Soft
    ganham papéis próprios, e o Node oculto passa a ter um.
  - Teste unitário: dado um mapa de variáveis, o módulo devolve as cores
    esperadas, sem Sigma.
- **DA-229 — Types além de 8: cor repete, com segundo canal** (consenso).
  - A paleta categórica cobre pelo menos 8 Types. Passando disso a cor repete e
    o Type é distinguido por rótulo ou forma (FE-034: informação nunca só por
    cor).
  - Cor escolhida pelo usuário por Type fica fora: é propriedade nova de Type,
    com migração.
  - `origin`, `confidence` e `strength` aparecem no Inspector. No grafo,
    `strength` e `confidence` podem ser espessura e opacidade, sem uma cor por
    valor.
- **DA-230 — Tema: sistema por padrão, com escolha do usuário** (usuário,
  aceitou a recomendação dos dois especialistas).
  - Opções: sistema, claro, escuro. A escolha fica em `localStorage`, que guarda
    só a preferência (sem dado pessoal). Persistir no backend é escopo extra.
  - Um script inline no `index.html` aplica a classe antes da pintura, para
    evitar o flash de tema errado. A CSP o permite por hash ou nonce.
- **DA-231 — Fontes em self-host; fonte de UI separada da fonte de marca**
  (consenso).
  - Sem CDN do Google: ele expõe o IP do visitante a um terceiro (LGPD), o que
    pesaria mais ainda na página pública do E11, e mantém a CSP em `'self'`.
  - Formato woff2, subset latin e latin-ext (pt-BR), `font-display: swap`,
    preload só das críticas.
  - Os papéis são `--font-display` (títulos e marca), `--font-body` e
    `--font-mono` (IDs, números, labels). Big Shoulders 900 não serve para
    tabelas nem para o Inspector, e a serifada no corpo de uma UI densa
    prejudica a leitura em tamanho pequeno. O brief pede ao Claude Design uma
    fonte de UI separada; a escolha é do usuário no Claude Design.
  - Até a entrega, usa-se a pilha de fontes do sistema.
- **DA-232 — Gate de contraste automatizado; sem regressão visual**
  (consenso).
  - Um teste Vitest lê o CSS de tokens, resolve uma lista explícita de pares
    (texto/superfície, foco/superfície, borda de componente, elementos do grafo)
    e exige AA nos dois temas: 4.5:1 para texto, 3:1 para não-texto (FE-034).
    O vitest-axe não calcula contraste, então não cobre isto.
  - Regressão visual com Playwright fica fora do MVP: manutenção cara e falso
    positivo com fontes e renderização. Entram no máximo 1 ou 2 E2E de fumaça da
    troca de tema, mais um checklist manual no brief.
  - "Sem regressão visual" vale contra os placeholders, não contra a marca
    futura.
- **DA-233 — `<Icon>` com registro semântico; lucide como ponte** (consenso).
  - Nomes semânticos (`node`, `relation`, `agent`...) apontam para o lucide hoje
    e para o set próprio do Claude Design no E17, sem tocar nos chamadores.
  - Um teste de arquitetura ou regra de lint proíbe importar o lucide fora do
    `<Icon>`. Ícones são decorativos (`aria-hidden`) ou levam nome acessível
    (FE-033).
  - Dependência nova passa por checagem de CVE e manutenção.
- **DA-234 — Componentes transversais entram aqui; os demais esperam a
  feature** (consenso).
  - Entram: toast (região `aria-live`), skeleton, empty-state, badge e tooltip,
    cada um com TDD e axe. Tabs, dropdown e card entram quando uma feature
    precisar.
  - Entram também o padrão de estado vazio/loading/erro e o shell de layout
    responsivo (FE-061), aplicados às telas existentes. O conteúdo da Home é do
    [E19](e19-home-com-o-grafo.md) (DA-275), e ela entra nessa migração quando
    o E16 rodar.
  - O toast não substitui o resultado visível de um GraphChangeSet: o
    invariante "o resultado de uma alteração de agente fica visível no grafo"
    continua valendo.
  - Textos novos vão para o catálogo (FE-062), não hardcoded.
- **DA-235 — Handoff para o Claude Design vive em `docs/design/`** (consenso).
  - O arquivo `docs/design/brief-claude-design.md` reúne: inventário de telas e
    rotas, tabela de papéis semânticos de cor com a necessidade funcional de
    cada um, restrições (AA nos dois temas, informação nunca só por cor,
    densidade de app, tamanhos mínimos de logo e favicon, o Lima `#B8D831` nunca
    como texto sobre claro), as lacunas conhecidas da marca LimaJ e o formato de
    retorno esperado.
  - Formato de retorno: CSS de tokens nos nomes do contrato (`:root` e `.dark`),
    logo em SVG (cheio, monocromático e mínimo), favicon, fontes woff2 com
    licença, ícones SVG 24×24 (incluindo os ícones de UI de app, que o set da
    marca não cobre) e a fonte de UI escolhida.
  - O brief usa dados fictícios, sem screenshots com conteúdo real de Nodes. A
    condição de entrada do E17 (formato e conteúdo da entrega) é definida aqui,
    para a entrega ser verificável.
- **DA-236 — O tom da marca não substitui o `GLOSSARY.md`** (consenso). O
  léxico "Sistema/Instrumento/Máquina" da LimaJ não renomeia conceitos do
  domínio (ex.: um agente não vira "Máquina"). O tom pode afetar só a
  microcopy de estados vazios e erros, e só depois do E17.

## Estrutura proposta

- `web/src/index.css`: tokens no contrato (`:root` e `.dark`) e `@theme inline`.
- `web/src/design/`: lista de pares de contraste e o contrato de nomes.
- `web/src/features/graph/`: leitor de tokens do canvas no lugar dos hex de
  `graphVisuals.ts`.
- `web/src/components/ui/`: `icon`, `toast`, `skeleton`, `empty-state`, `badge`,
  `tooltip`.
- `web/index.html`: script de tema, `description`, preparo de favicon.
- `docs/design/brief-claude-design.md`: o handoff.

## Checklist

### Contrato e tokens
- [ ] Arquivo do contrato de tokens com todos os papéis (DA-227)
- [ ] Tokens claro e escuro em `index.css`, valores placeholder neutros
- [ ] Teste: todos os papéis preenchidos nos dois temas
- [ ] Migrar componentes e telas para tokens, sem cor hardcoded fora do arquivo de tokens
- [ ] Se o E11 já estiver integrado, incluir o bundle do visitante na migração

### Grafo
- [ ] Leitor de tokens para o canvas, relendo na troca de tema (DA-228)
- [ ] Papéis próprios para Hard, Soft, agente, Node oculto, Raiz da Conta e Desancorado
- [ ] Segundo canal (rótulo ou forma) para Types além do ciclo de cores (DA-229)

### Tema e fontes
- [ ] Seletor sistema/claro/escuro com `localStorage` e script inline com hash na CSP (DA-230)
- [ ] Papéis `--font-display`, `--font-body`, `--font-mono` com pilha do sistema como fallback
- [ ] Infra de self-host (woff2, subset, `font-display`, preload), sem CDN (DA-231)

### Qualidade
- [ ] Gate de contraste AA com lista explícita de pares, nos dois temas (DA-232)
- [ ] 1 ou 2 E2E de fumaça da troca de tema

### Acabamento
- [ ] `<Icon>` com registro semântico sobre o lucide e regra contra import direto (DA-233)
- [ ] Toast, skeleton, empty-state, badge e tooltip, com axe (DA-234)
- [ ] Estados vazio/loading/erro uniformes nas telas existentes
- [ ] Shell de layout responsivo (FE-061); a Home (E19) adota tokens e layout
- [ ] Textos novos no catálogo (FE-062)

### Handoff
- [ ] `docs/design/brief-claude-design.md` completo (DA-235)
- [ ] Condição de entrada do E17 definida no brief

### Testes obrigatórios
- [ ] Contrato completo nos dois temas
- [ ] Leitor de tokens do canvas, sem Sigma
- [ ] Gate de contraste AA
- [ ] axe em cada componente novo
- [ ] Troca de tema sem flash e preferência persistida

## Critérios de saída

Trocar a paleta, as fontes e os ícones passa a ser só editar valores e arquivos
do contrato, sem tocar em componentes. O app roda em claro e escuro com
contraste AA verificado por teste, e o brief está pronto para o usuário levar ao
Claude Design.

## Fora de escopo

Logo, fontes, paleta e ícones reais (E17). Cor de Type escolhida pelo usuário.
Persistência do tema no backend. Regressão visual com Playwright. Componentes
tabs, dropdown e card antes de uma feature precisar. Polimento específico de
cada feature. Registro do Life Graph no `limaj-solutions`. 3D e qualquer
mudança de comportamento do grafo.
