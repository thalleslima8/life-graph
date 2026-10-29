# E11 — SharedGraphView (link compartilhado)

- **Ordem:** 11
- **Depende de:** E2, E6, E8, E10
- **Última revisão:** 2026-10-29

## Contexto

O usuário pode compartilhar parte do grafo por link (v2 §47–49). Por decisão do
usuário, isso entra **no MVP e no plano Free**. Quem recebe o link nunca pode
atravessar relações para Nodes não autorizados (§48): a filtragem é feita no
backend. O épico vem depois do 2.5D, para o visitante reaproveitar a
visualização, e depois do E8, porque a lista de propriedades expostas depende
das definições e das Tags.

## Decisões

- **DA-074 — SharedGraphView no MVP e no Free** (usuário). Os especialistas
  recomendaram deixá-la fora; o usuário decidiu incluir.
- **DA-075 — Membros fixados na criação, conteúdo exibido ao vivo** (consenso,
  ADR: [docs/adr/0012-share-com-membros-fixos-e-conteudo-ao-vivo.md](../../adr/0012-share-com-membros-fixos-e-conteudo-ao-vivo.md)).
  - Na criação, o usuário escolhe RootNode + MaxDepth, revisa a lista proposta
    (máximo de 25) e desmarca o que quiser. A lista confirmada é a allowlist.
  - Node novo nunca entra sozinho: um share totalmente "Live" vazaria Nodes
    sensíveis adicionados depois.
  - Um snapshot copiado sobreviveria ao Purge, contra LGPD e Lei 25.
  - Node apagado some do link; se o Node raiz for apagado, o link fica suspenso
    (404); se a raiz for restaurada, o link volta; se a raiz for purgada, o
    link é revogado.
- **DA-076 — Nodes ocultos para agentes nunca entram em um share, sem exceção
  no MVP** (consenso após desempate). Um link público pode ser lido por
  qualquer IA que navegue, o que tornaria falso o rótulo "oculto". Uma regra
  sem exceção é mais simples de provar. Quem quiser compartilhar desoculta o
  Node antes. Relações que tocam Nodes ocultos e qualquer contagem que revele o
  descarte também ficam de fora.
- **DA-077 — O que o visitante vê é uma allowlist fechada** (consenso após
  desempate).
  - Sempre: título, corpo, nome do Type, URL de Resource (com aviso sobre
    links assinados) e Hard Relations entre membros.
  - Properties: nenhuma por padrão; o usuário escolhe `property_id` por
    `property_id`. Um toggle único exporia automaticamente, nos links já
    publicados, uma propriedade sensível criada depois, como "medicação".
  - Tags: booleano, desligado por padrão. **Risco residual aceito:** com o
    booleano ligado, novas Tags passam a aparecer, e a UI avisa isso.
  - Nunca: Outras propriedades, Soft Relations, proveniência, nomes de
    agentes, ChangeSets, Inbox, e-mail do dono ou contagens de vizinhos
    excluídos.
  - A projeção usa um DTO de visitante próprio, que não reaproveita os DTOs
    internos nem os do MCP (BE-005).
- **DA-078 — Links sem validade no MVP** (usuário). A validade fica
  registrada para uma versão futura (`expires_at` anulável, migration
  trivial). Mitigações obrigatórias (consenso):
  - lista de links com `created_at`, último acesso (timestamp grosseiro, sem
    IP nem user agent ligados ao link) e contagem;
  - revogar um ou todos, com efeito imediato e sem cache longo;
  - um selo "compartilhado" nos Nodes que fazem parte de um link ativo;
  - um kill-switch do operador, global e por link.
- **DA-079 — Acesso por URL de capacidade não listada** (consenso):
  - token aleatório de ≥128 bits, guardado com hash, no caminho (nunca na
    query) e redigido nos logs;
  - mesmo 404 para link inválido e revogado;
  - `noindex` (header e meta), `Referrer-Policy: no-referrer` e
    `Cache-Control: private, no-store`;
  - rate limit por IP e por token;
  - **origem separada**, sem cookies de sessão e com CSP estrita. Em dev, é uma
    segunda porta localhost.
  - **Substituída em parte:** o token no caminho pela DA-221 (vai no
    fragmento) e a segunda porta pela DA-215 (é outro host). O resto vale.
- **DA-080 — Criar share é Sensitive, e agentes não criam shares no MVP**
  (consenso). Não há nenhuma ferramenta MCP para isso; o scope
  `lifegraph.share` fica reservado.
- **DA-081 — Limites de 3 links com 25 Nodes cada, como constantes** (consenso)
  até o E13.
- **DA-213 — O agregado de share vive no Graph; o Sharing é só o adapter HTTP
  e a origem do visitante** (consenso, rodada de implementação). O filtro
  central é interno e a DA-163 já decidiu não abrir ponto de extensão. Assim
  o `ShareVisitor`, o purge em cascata e o selo "compartilhado" ficam na mesma
  transação e sob o mesmo dono. O Sharing usa uma porta estreita em
  `Graph.Contracts` (criar, listar, revogar, resolver token em principal).
- **DA-214 — Criar e revogar share não geram GraphChangeSet nem Undo**
  (consenso). Desfazer uma revogação republicaria um link público sem ato
  consciente do dono, e desfazer uma criação seria uma revogação silenciosa. A
  proveniência fica no próprio share: `created_at`, `created_via`,
  `created_by`, `revoked_at`, `revoked_by`. Revogação é irreversível: um novo
  link é outro share, com outro token. Share não é conteúdo do grafo, então o
  pipeline único (DA-013) não se aplica a ele; escopo registrado aqui.
- **DA-215 — A origem do visitante é um host diferente, não só outra porta**
  (consenso; emenda a DA-079). Cookies não isolam por porta (RFC 6265).
  Em dev, `share.localhost:<porta>`; o pipeline do visitante também descarta
  o header `Cookie` e nunca autentica por sessão, em dev e em produção.
  Teste de integração: uma requisição com cookie de sessão válido na origem
  do visitante se comporta como uma sem cookie. Em produção, a origem deve
  ficar em domínio registrável separado (E14).
- **DA-216 — O bundle do visitante é uma segunda entrada do mesmo projeto
  Vite, servida pelo Host só na origem do visitante, com CSP estrita**
  (consenso). Em dev usa `vite build --watch`, sem CSP relaxada, para que a
  CSP real seja testada. Uma regra de import do lint impede `web/src/share` de
  importar cliente autenticado, auth ou CSRF, e um teste verifica o bundle
  construído. A origem do app nunca serve o bundle do visitante.
- **DA-217 — Kill-switch por link: tabela de diretório sem RLS e papel
  `lifegraph_operator`** (consenso após contra-argumento). A resolução do
  token já precisa de uma leitura sem contexto de Account, então uma tabela
  estreita (`token_hash`, `share_id`, `account_id`, `public_ref`,
  `blocked_at`) a atende, no mesmo precedente da DA-098. O papel da app só lê;
  o novo `lifegraph_operator` escreve apenas `blocked_at` pela CLI
  (`shares disable <public_ref>`), sem SELECT nas tabelas de Account, e a
  DA-107 não muda. O operador usa o `public_ref` opaco, nunca o token nem o id
  interno. Link bloqueado responde ao visitante igual a um revogado; o dono vê
  "desativado pela plataforma" e não pode limpar o bloqueio. O bloqueio vale
  para aquele link, não para o conteúdo. Global: flag `Sharing:Enabled`.
  Exceção a DB-xxx por tabela sem RLS: citar a regra exata ao implementar.
- **DA-218 — "Denunciar esta página" é um `mailto:`** (consenso).
  `Sharing:AbuseReportAddress`, validado na partida, com o `public_ref` do
  share no assunto, nunca o token. Evita uma escrita pública anônima (spam,
  retenção, dado pessoal) antes do E14. Reavaliar com motivo em enum, sem
  texto livre, se houver volume.
- **DA-219 — A Raiz da Conta nunca é RootNode nem membro de um share**
  (consenso; esclarece a DA-189). O walk não passa por ela nem emite arestas
  para ela. 422 `sharing.root_not_shareable`, validado na proposta, no POST e
  no ramo `ShareVisitor` do filtro. "Escolha explícita" da DA-189 passa a
  significar "escolha entre Nodes comuns".
- **DA-220 — Padrões da proposta** (consenso):
  - `MaxDepth` de 1 a 2, padrão 1;
  - os 25 Nodes ficam por profundidade crescente, depois `WalkStep.Ranking`,
    depois id do Node decrescente; a resposta traz `truncated` e o total
    elegível, e a UI avisa o dono;
  - o POST valida os ids contra o conjunto elegível completo e aplica o limite
    de 25 à parte;
  - share suspenso (raiz apagada) e desativado pelo operador contam no limite
    de 3; só revogado ou purgado libera vaga;
  - "último acesso" em dia UTC, atualizado só quando o dia muda, sem contar
    crawlers e unfurlers.
- **DA-221 — O token vai no fragmento da URL, não no caminho** (consenso;
  substitui essa parte da DA-079). `/s#<token>`: a SPA lê, remove com
  `history.replaceState` e chama `GET /api/shared` com header
  `Authorization`. Cumpre a API-081 sem exceção, e o token nunca chega a
  servidor, proxy ou Referer. Mantém: ≥128 bits guardado só como hash,
  `no-referrer`, `no-store`, `noindex`, nenhum recurso de terceiros na página
  e revogação. Custo aceito: o link exige JS e não há preview. A decisão
  reverte um trecho de DA em vigor; foi tomada agora porque os dados de dev
  são descartáveis até o E14 e o formato da URL fica caro depois.
- **DA-222 — Interfaces do frontend do compartilhamento** (consenso, via
  design-an-interface: o arquiteto escolheu, o analista validou após uma
  contra-argumentação). Dono: módulo `features/sharing` no padrão do
  `collectionsData.ts` (queryOptions e mutations; a URL só no resultado da
  mutation, em memória), criação em rota `/shares/new?rootNodeId=&depth=`,
  `SharesPage` e `SharedBadge({nodeId})` alimentado por uma query de ids
  compartilhados. Visitante: `GraphCanvas` e `GraphList` recebem um
  `RenderableGraph` só de tipos, os helpers puros saem de `localGraph.ts`
  (que importa o cliente autenticado), `VisitorNodeInspector` recebe os dados
  por prop, e o lint mais um teste do bundle impedem código autenticado no
  visitante. Dobra a FE-013 (o visitante tem cliente próprio, DA-215/216). O
  contrato completo e os estados exigidos estão no arquivo de interface do run.
- **DA-223 — Diretório de shares sem RLS** (exceção à BE-022 e à regra de
  RLS do E0 em CLAUDE.md: "toda tabela de Account tem `account_id` e uma
  policy"; nenhuma regra DB-xxx trata de RLS). A tabela
  `shared_graph_view_directory` (hash do token, share, Account, referência
  pública, `blocked_at`) fica fora da RLS porque o token do visitante é
  resolvido antes de existir Account em contexto (DA-217). Precedente: DA-098.
  Mitigações: só guarda o hash SHA-256 do token, nunca conteúdo; o papel da
  app só lê e insere (UPDATE e DELETE revogados na migration); a linha sai
  com o share pela cascata da FK; o teste de catálogo de RLS lista a tabela
  como a única exceção além do diretório de credenciais.
- **DA-239 — A visita do `GET /api/shared` grava o dia UTC** (exceção à
  API-020: "GET nunca altera estado"). A DA-078 pede último acesso e
  contagem, e só a leitura do visitante sabe que houve visita: não existe
  outra chamada a que prender o registro sem expor mais ao visitante. Para
  ficar no espírito da regra, a gravação acontece no máximo uma vez por share
  e por dia UTC (só quando `last_accessed_on` muda, como pede a DA-220): a
  leitura é idempotente dentro do dia. A contagem passa a ser de dias com
  acesso (`access_day_count`), não de requisições. A gravação roda depois da
  leitura, em transação própria, e uma falha de banco só gera log: não
  transforma uma visita válida em erro (só `DbException` é capturada; outra
  exceção ainda vira 500, porque esconder bug de código seria pior). Crawlers e unfurlers não executam o JS que lê o
  token do fragmento (DA-221), então não chegam a contar. O rename da coluna
  sem expand/contract (DB-012) é aceito porque os dados de dev são
  descartáveis até o E14 e a cadeia de migrations do E11 ainda não entrou na
  `master`.
- **DA-240 — Coleções do visitante sem paginação** (exceção à API-060 e à
  DB-023). Em `GET /api/shared` e `GET /api/shared/nodes/{id}`, os Nodes, as
  Relations, as Tags e as propriedades vêm inteiros. O conjunto é fechado:
  no máximo 25 Nodes por share (DA-081) e as propriedades da allowlist
  (`SharingLimits.MaxPropertiesPerShare`). As Relations entre esses Nodes e
  as Tags de um Node não têm teto próprio, mas ficam presas ao que o dono
  escreveu nesses 25 Nodes. Paginar obrigaria o visitante a fazer mais
  chamadas com o token e mostraria um grafo parcial. Reavaliar se o E13 subir
  o limite de Nodes.

## Checklist

- [x] Modelo de share: raiz, profundidade, allowlist de Nodes, allowlist de propriedades, booleano de Tags, token com hash
- [x] Fluxo de criação: Node raiz → lista proposta → revisão → aviso legal ("visível para quem tiver o link; você tem o direito de compartilhar dados de outras pessoas?")
- [x] Endpoint de visitante com principal `ShareVisitor` no filtro central e DTO próprio
- [x] Visualização read-only (lista + Inspector + 2.5D read-only) na origem separada
- [x] Lista de links, revogar um ou todos, selo "compartilhado", kill-switch do operador
- [x] Link "denunciar esta página" em todo share

### Testes obrigatórios
- [x] Tentar atravessar para IDs fora da allowlist por todos os endpoints e parâmetros
- [x] Node oculto nunca aparece, nem como contagem
- [x] O visitante nunca alcança a raiz (DA-204)
- [x] Snapshot do schema do DTO de visitante; propriedade fora da allowlist nunca aparece
- [x] Revogação imediata; inválido e revogado respondem igual

## Critérios de saída

O usuário compartilha "Como estou aprendendo Estoicismo" com 12 Nodes. O
visitante vê só o que foi escolhido, e revogar derruba o link na hora.

## Fora de escopo

Validade do link, destinatários autenticados, Constellations, compartilhamento
por agentes.

⚖️ **Validação jurídica humana antes do beta:** publicação de dados de
terceiros, crianças e saúde (LGPD arts. 11 e 14, PIPEDA, Lei 25), e a redação
dos termos e da AUP.
