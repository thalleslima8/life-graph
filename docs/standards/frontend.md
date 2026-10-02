# Padrões de frontend (`FE`)

Complementam [general.md](general.md) para interfaces de usuário (web, e no que
couber, mobile e desktop). Independem de framework: "componente" significa a
unidade de UI reutilizável da stack escolhida.

## 1. Componentes

**FE-001 — DEVERIA: componentes pequenos e com uma responsabilidade.** Separe
componentes de apresentação (recebem dados, emitem eventos) de componentes que
orquestram dados e efeitos.
_Por quê:_ componentes de apresentação são reutilizáveis e fáceis de testar.

**FE-002 — DEVE: contrato de componente explícito e tipado.** Propriedades,
eventos e slots são declarados com tipos; nada de "passar o objeto inteiro e
ver o que serve".
_Por quê:_ o contrato documenta o uso e o compilador pega quebras.

**FE-003 — DEVERIA: composição em vez de configuração.** Prefira compor
componentes menores a criar um componente com dezenas de props/flags.
_Por quê:_ componentes "faz-tudo" ficam impossíveis de manter.

**FE-004 — DEVERIA: organizar por funcionalidade.** Código de uma tela/feature
(componentes, estado, chamadas, testes) fica junto; só o realmente
compartilhado vai para uma pasta comum.
_Por quê:_ mesma razão de BE-004 — mudanças ficam localizadas.

**FE-005 — DEVE: usar o design system / biblioteca de componentes do projeto**
em vez de recriar botões, inputs e modais. Novos padrões visuais entram no
design system, não em uma tela isolada.
_Por quê:_ consistência visual e de acessibilidade vem de graça.

## 2. Estado e dados

**FE-010 — DEVE: uma fonte de verdade para cada dado.** Não duplique estado que
pode ser derivado; calcule-o.
_Por quê:_ estado duplicado diverge e gera bugs de sincronia.

**FE-011 — DEVERIA: estado o mais local possível.** Estado global só para o que
é realmente compartilhado (sessão, preferências). Estado de servidor (dados da
API) é gerenciado por camada de cache/requisição, não copiado para store
global manualmente.
_Por quê:_ estado global desnecessário acopla telas e complica testes.

**FE-012 — DEVE: estado de URL para o que deve ser compartilhável.** Filtros,
paginação, abas e item selecionado relevantes ficam na URL.
_Por quê:_ permite recarregar, voltar e compartilhar links sem perder contexto.

**FE-013 — DEVE: acesso à API centralizado.** Chamadas HTTP passam por uma
camada de cliente única (base URL, autenticação, tratamento de erro,
desserialização), nunca `fetch` espalhado em componentes.
_Por quê:_ mudanças de contrato e de autenticação afetam um ponto só.

**FE-014 — DEVE: tratar todos os estados de dados assíncronos:** carregando,
vazio, erro e sucesso — cada um com UI definida.
_Por quê:_ a tela em branco ou travada é o bug mais visível ao usuário.

**FE-015 — DEVERIA: evitar condições de corrida em requisições.** Cancele ou
ignore respostas obsoletas (ex.: busca digitada), e use debounce em entradas
que disparam requisições.
_Por quê:_ resposta antiga chegando depois sobrescreve a nova.

## 3. Formulários e validação

**FE-020 — DEVE: validação no cliente é conveniência, não segurança.** O
servidor sempre revalida (ver BE-020).
_Por quê:_ qualquer validação no cliente pode ser contornada.

**FE-021 — DEVERIA: mensagens de erro claras e próximas do campo,** dizendo
como corrigir; erros do servidor são mapeados para os campos quando possível.
_Por quê:_ o usuário precisa saber o que fazer, não só que errou.

**FE-022 — DEVE: impedir envio duplicado.** Desabilite a ação ou trate como
idempotente enquanto a requisição está em andamento.
_Por quê:_ duplo clique não pode criar dois pedidos.

## 4. Acessibilidade (a11y)

**FE-030 — DEVE: atender WCAG 2.2 nível AA** como meta mínima.
_Por quê:_ acessibilidade é requisito legal em vários contextos e melhora a
experiência de todos.

**FE-031 — DEVE: HTML semântico.** Use o elemento correto (`button`, `a`,
`label`, `nav`, `main`, cabeçalhos em ordem) antes de recorrer a ARIA; nunca
`div` clicável no lugar de botão.
_Por quê:_ semântica nativa dá teclado, foco e leitor de tela sem esforço.

**FE-032 — DEVE: tudo operável por teclado,** com foco visível e ordem de foco
lógica; modais prendem e devolvem o foco.
_Por quê:_ muitos usuários não usam mouse.

**FE-033 — DEVE: textos alternativos e rótulos.** Imagens informativas têm
`alt`; decorativas têm `alt=""`; todo campo tem rótulo associado; ícones-botão
têm nome acessível.
_Por quê:_ leitores de tela dependem disso para descrever a interface.

**FE-034 — DEVE: contraste e cor.** Contraste mínimo AA e informação nunca
transmitida só por cor.
_Por quê:_ daltonismo e baixa visão são comuns.

## 5. Performance

**FE-040 — DEVERIA: metas de Core Web Vitals** (LCP, INP, CLS) definidas e
monitoradas em produção.
_Por quê:_ performance percebida afeta conversão e SEO.

**FE-041 — DEVERIA: carregar só o necessário.** Code splitting por rota,
carregamento tardio de componentes pesados, orçamento de tamanho de bundle
verificado no CI.
_Por quê:_ cada KB custa tempo em redes e aparelhos lentos.

**FE-042 — DEVE: imagens otimizadas** (formato moderno, dimensões corretas,
`width`/`height` declarados, carregamento lazy fora da dobra).
_Por quê:_ imagens são a maior fonte de peso e de layout shift.

**FE-043 — DEVERIA: evitar renderizações desnecessárias** em listas grandes
(virtualização, chaves estáveis), sem memoização prematura em toda parte.
_Por quê:_ listas grandes travam a UI; memoização indiscriminada só adiciona
complexidade.

## 6. Segurança no cliente

**FE-050 — DEVE: nunca inserir HTML não sanitizado.** Evite APIs de injeção de
HTML bruto; quando inevitável, sanitize com biblioteca consagrada.
_Por quê:_ é o principal vetor de XSS.

**FE-051 — DEVE: nenhum segredo no bundle.** Tudo que vai para o cliente é
público, inclusive variáveis de ambiente de build.
_Por quê:_ qualquer pessoa pode inspecionar o código entregue ao navegador.

**FE-052 — DEVERIA: tokens de sessão em cookies `HttpOnly`, `Secure`,
`SameSite`** em vez de `localStorage`, e Content Security Policy configurada.
_Por quê:_ reduz o impacto de XSS no roubo de sessão.

**FE-053 — DEVE: autorização na UI é só cosmética.** Esconder botões não
substitui a verificação no servidor (BE-021).
_Por quê:_ a API pode ser chamada diretamente.

## 7. Estilo, i18n e testes

**FE-060 — DEVERIA: estilos com tokens de design** (cores, espaçamentos,
tipografia) em vez de valores soltos, e escopo de estilo que evite vazamento
global.
_Por quê:_ permite temas (inclusive modo escuro) e mudanças consistentes.

**FE-061 — DEVERIA: layout responsivo, mobile-first.**
_Por quê:_ boa parte do acesso é por telas pequenas.

**FE-062 — DEVERIA: textos visíveis externalizados** para i18n desde o início
quando houver chance de múltiplos idiomas; datas, números e moedas formatados
pela localidade.
_Por quê:_ extrair strings depois é caro e propenso a erro.

**FE-063 — DEVERIA: testar como o usuário usa.** Testes de componente consultam
por papel/rótulo/texto visível, não por classe CSS ou estrutura interna; fluxos
críticos têm testes ponta a ponta.
_Por quê:_ testes acoplados ao markup quebram a cada ajuste visual.
