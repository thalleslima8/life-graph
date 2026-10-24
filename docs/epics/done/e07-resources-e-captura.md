# E7 — Resources e captura

- **Ordem:** 7
- **Depende de:** E2, E5 (ferramenta MCP)
- **Última revisão:** 2026-10-21

## Contexto

Conteúdo externo (vídeos, artigos, páginas) é tratado como objeto relevante do
grafo (v2 §11, §17). O caso agent-first da §73 ("guarde essas três casas e
relacione com meu projeto") depende deste épico.

## Decisões

- **DA-055 — Resource é um Node do Type de sistema `Resource`** (consenso,
  ADR: [docs/adr/0010-resource-como-node.md](../../adr/0010-resource-como-node.md)), não uma entidade separada. Assim, relações, travessia,
  filtro de ocultos, busca, export, quotas e ChangeSet não precisam de caso
  especial. Book continua sendo um Type do usuário: um livro físico não é um
  link.
- **DA-056 — `resource_kind` fechado:
  `web_page | article | video | pdf | image | document | other`, mais um campo
  `provider`** separado, por exemplo `youtube` (consenso + usuário).
  - Um valor só entra no enum se o MVP se comportar diferente para ele;
    podcast e social_post ficam para depois.
  - pdf, image e document são **só links**, sem upload e sem leitura do
    arquivo.
  - O tipo é sugerido pelo Content-Type e depois pela extensão, e o usuário tem
    a palavra final.
  - Clientes e agentes precisam aceitar valores de kind desconhecidos e
    tratá-los como `other`; é isso que permite adicionar valores sem quebrar
    nada.
- **DA-057 — Propriedades de sistema travadas e Properties do usuário
  permitidas no Type Resource** (usuário + consenso).
  - As Properties de sistema (`url`, `canonical_url`, `resource_kind`,
    `provider`, `fetch_status`...) não podem ser removidas, renomeadas nem ter
    o tipo trocado, e seus nomes são reservados.
  - As Properties do usuário valem para todos os Resources e contam no limite
    de Custom Properties.
- **DA-058 — Fetch de metadados assíncrono e protegido contra SSRF**
  (consenso):
  - só http/https; resolve o DNS e bloqueia IPs privados, link-local e de
    metadata, de novo a cada redirect (máximo 3);
  - timeout de 5 s e no máximo ~512 KB;
  - HTML só para `text/html`, e só cabeçalhos para pdf/image;
  - URLs com credenciais nunca são buscadas;
  - metadados buscados são não confiáveis (sanitizados na UI e em envelope no
    MCP).

  O mesmo guard é reaproveitado pelo CIMD (E3).
- **DA-059 — Captura nunca falha por causa do fetch** (consenso). Erro, timeout
  ou bloqueio resultam em `fetch_status=failed` e o Node é criado mesmo assim.
- **DA-060 — Idempotência por URL canônica**, chave única
  `(account_id, canonical_url)` (consenso). A canonicalização remove só
  parâmetros de rastreio conhecidos (`utm_*`) e normaliza IDs do YouTube.
  **Nunca** remove tokens da query: links assinados de Drive/S3 deixariam de
  funcionar ou seriam deduplicados errado. URLs nunca vão para logs.
- **DA-061 — Sem thumbnails armazenadas ou exibidas por hotlink no MVP**
  (consenso). Hotlink vaza IP e navegação para terceiros e enfraquece a CSP.
  Armazenar copia conteúdo de terceiros. Guarda-se só a referência.
- **DA-062 — Entrada na Inbox** (consenso). Captura rápida, colar e
  `capture_resource`, de qualquer ator, entram na Inbox.
- **DA-149 — Type e Properties de sistema existem antes de qualquer captura**
  (consenso; emendada pela DA-158: a criação é no sign-in, não na CLI). Uma coluna `system_key` anulável, única por Account, em `types` e
  `property_definitions` marca as linhas de sistema. Uma migration (papel
  migrator) faz o backfill das Accounts existentes. Para Accounts novas, o
  `EnsureSystemTypesAsync`, idempotente, roda com o papel da app logo após a CLI
  `accounts create` e de novo, como guarda, no primeiro login. O
  `lifegraph_provisioner` **não** ganha grants novos, e assim a DA-107 continua
  estreita. As linhas de sistema não geram ChangeSet, ficam fora de Undo,
  Delete e Purge, e não podem ser renomeadas nem ter suas definições
  alteradas. Criar o Type de forma preguiçosa colocaria o ciclo de vida dele
  no Undo de uma captura, e um id fixo por Account não combina com UUIDv7. Um
  teste garante que toda Account nova tem as linhas de sistema e que rodar de
  novo não muda nada.
- **DA-150 — Catálogo das Properties de sistema** (consenso):
  - O catálogo é `url`, `canonical_url`, `resource_kind`, `provider`,
    `fetch_status`, `site_name`, `author`, `description` e `published_at`.
  - `resource_kind` é Text com checagem de domínio. Um Select guardaria ids de
    opção e não aceitaria valores desconhecidos (DA-056).
  - O usuário edita `resource_kind` e `url`. Editar `url` recalcula
    `canonical_url`, passa pela mesma checagem de unicidade (uma colisão vira
    erro de validação) e busca de novo.
  - `canonical_url` e `fetch_status` são só do sistema.
  - O fetch preenche o título e o body do Node só enquanto nenhum Human
    tocou neles.
  - O texto do artigo extraído não é guardado (DA-061). O item "readability"
    do checklist fica reduzido a extração de metadados.
  - A captura de uma URL com credenciais (`user:pass@`) é recusada com erro
    de validação. Guardá-la gravaria um segredo nos dados (GEN-043), o que vai
    além do "nunca busca" da DA-058.
- **DA-151 — Metadados do fetch podem ser sobrescritos pelo usuário**
  (provisória, decidida pelo analista; outra visão: travados no MVP, com o
  fetch como único escritor e uma Property própria como saída para o usuário;
  para reverter: remover o marcador por campo, tornar os cinco campos somente
  leitura para o Human e deixar o refetch sobrescrever todos). `provider`,
  `site_name`, `author`, `description` e `published_at` são preenchidos pelo
  fetch, e o usuário pode editá-los. Um marcador "tocado pelo Human" por campo
  faz o refetch preencher só os campos que ninguém tocou. O grafo pertence ao
  usuário e o fetch é uma estimativa. A regra é a mesma do título e do body.
- **DA-152 — Job em duas fases** (consenso). Rodar o fetch dentro da
  transação do job violaria a DB-040, que é DEVE. O contrato do handler passa a
  ter duas fases:
  - **I/O:** o payload é lido numa transação curta e o HTTP roda fora de
    qualquer transação.
  - **Aplicação:** roda na transação do job, por uma entrada interna do
    pipeline que se junta à transação ambiente, coberta por teste de
    arquitetura para não virar bypass público. Isso mantém a DA-013 e deixa a
    marca de "done" atômica com a escrita. Essa fase revalida o estado: se o
    Node sumiu, o job termina sem escrever.

  A opção em que o handler grava em transação própria deixaria a idempotência
  com cada handler. Um teste afirma que não há transação aberta durante o
  fetch. O E9 herda esse contrato.
- **DA-153 — Ator `System` na Provenance do fetch** (consenso).
  `GraphActor.System`, `ChangeActorKind.System` e um canal novo `System` (o
  `Import` já significa outra coisa para o envelope não confiável). O fetch
  gera um ChangeSet próprio e desfazível, que aparece em Recent Changes, mas
  não em Agent Changes nem na janela da DA-132. Só escritas de Human limpam a
  propriedade de agente da DA-133, então os metadados buscados continuam em
  envelope não confiável (DA-038/058). Desfazer o ChangeSet do fetch não
  dispara um novo fetch. Guardar os metadados fora do log de mudanças deixaria
  o dado sem explicação, contra a Proveniência obrigatória.
- **DA-154 — Unicidade por coluna, restauração de tombstone e resposta neutra
  para Node oculto** (consenso):
  - `nodes` ganha a coluna anulável `canonical_url`, com índice único parcial
    `(account_id, canonical_url)` sobre Nodes vivos. Uma tabela lateral seria
    um segundo lugar para manter em sincronia. A Property `canonical_url` é a
    projeção dessa coluna.
  - Capturar uma URL cujo Resource está em tombstone restaura o Node e o
    devolve à Inbox, num ChangeSet.
  - Quando a URL pertence a um Node oculto para agentes, a captura de um
    agente não cria nada e responde um sucesso neutro, do mesmo formato e sem
    `nodeId`. O id é opcional no contrato e documentado assim. Se veio
    `relatedNodeId`, a Relation não é criada.
  - Um duplicado só para agentes quebraria o índice e denunciaria o Node
    oculto pelo comportamento. Um teste de vazamento compara corpo e tempo de
    resposta com os de uma captura nova.
- **DA-155 — O Type de sistema aparece nas listagens com `isSystem`**
  (consenso). `list_types` e `GET /api/types` listam o Resource, porque agentes
  e humanos precisam do schema que leem nos Nodes. Esconder o Type deixaria a
  ontologia inconsistente. O teste
  `List_types_shows_the_visible_types_with_their_properties` muda (regra
  anterior: a Account só vê os Types que criou; regra nova: o Type de sistema
  também aparece). Ele passa a contar os Types do usuário e afirma o de sistema
  à parte.
- **DA-156 — Forma de `capture_resource`, recaptura e parser** (consenso):
  - **Assinatura:** `capture_resource(url, title?, source?, relatedNodeId?,
    relationKind?)`, num único ChangeSet, para que um Undo desfaça "salvar e
    relacionar" (critério de saída).
  - **Permissões:** um `relatedNodeId` oculto ou de outra Account responde
    NotFound. A ferramenta exige `lifegraph.write` (DA-122).
  - **Recaptura:** devolve o mesmo id. Se o Node saiu da Inbox, volta para ela
    num ChangeSet. Se ainda está na Inbox, nada muda e a resposta traz
    `alreadyExists` e `alreadyInInbox` (para a UI, "já está na sua Inbox"). Um
    `relatedNodeId` novo gera ChangeSet como antes. Só há novo fetch se
    `fetch_status=failed`.
  - **Aprovação ligada (DA-044):** a deduplicação também olha as propostas
    pendentes.
  - **Parser:** AngleSharp (MIT; GEN-023: a stdlib não tem parser de HTML
    seguro, e regex sobre HTML é superfície de ataque). Heurísticas próprias
    só para metadados (og/twitter, `title`, canonical, JSON-LD), com limite de
    tamanho, timeout, checagem de content-type e sem executar scripts. Sem
    SmartReader, porque o texto do artigo não é guardado.
  - **SSRF:** o guard do Resource aceita http e segue até 3 redirects. Ele
    revalida o DNS a cada salto e fixa o IP resolvido na conexão (contra DNS
    rebinding), e bloqueia faixas privadas, link-local e de metadata. Há
    testes de abuso próprios e rate limit de captura por Account. O CIMD
    continua só https.
- **DA-157 — Interface da captura por colagem no frontend** (consenso: o
  arquiteto escolheu, o analista validou com acréscimos):
  - **Módulo:** `web/src/features/resources/resourceCapture.ts`, com
    `useCaptureResource()` sobre um `useGraphWrite<TReceipt, TVariables>`
    genérico exportado de `graphData.ts`. A entrada é
    `{ url, title?, source? }` e o recibo é o da API, mantendo
    `changeSetId` para um futuro "desfazer esta captura".
  - **`outcomeOf`:** `created | already-in-inbox | reentered`. O tombstone
    restaurado conta como `reentered`.
  - **Formulário:** um `CaptureResourceForm` próprio na Inbox (também com a
    Inbox vazia), acima do `CreateNodeForm`, que não muda. Depois do sucesso o
    formulário fica na página para capturar vários links seguidos. Uma linha
    `role="status"` traz o link para o Node só quando há `nodeId`.
  - **Erros:** o 422 vira erro de campo, e o 429 do rate limit tem mensagem
    própria e mantém o valor.
  - Detectar URL dentro do campo de título mudaria em silêncio o que esse
    controle faz e misturaria dois estados de mutação num formulário.
- **DA-158 — A ontologia de sistema é criada no sign-in, não na CLI**
  (consenso; emenda a DA-149). A configuração é idempotente e roda a cada
  sign-in (`AccountSetup.EnsureAsync` no `OnSignedIn` do cookie, com advisory
  lock). A CLI `accounts create` **não** a cria e **não** recebe
  `ConnectionStrings__Default`, o que preserva a DA-107 (papéis separados no
  processo da CLI). Nenhum caminho alcança o grafo antes do primeiro sign-in,
  e o consentimento de agente exige um Human autenticado. As Accounts
  existentes ficam cobertas pelo backfill da migration. A configuração falha
  fechada: se ela falhar, o sign-in falha, e nenhuma Account fica
  provisionada pela metade. Testes cobrem o segundo sign-in sem efeito e
  sign-ins concorrentes.
- **DA-159 — Colisão de nomes reservados no backfill aborta a migration**
  (provisória, decidida pelo arquiteto; outra visão: adotar a linha do usuário
  como linha de sistema quando o tipo de valor é compatível e não há perda, e
  senão renomear com sufixo livre em loop, com `RAISE NOTICE`; para reverter:
  trocar o abort por essa adoção ou renomeação no SQL da migration). Antes de
  qualquer insert, a migration procura Types ou Properties do usuário cujo nome
  colide, sem diferenciar maiúsculas de minúsculas, com os nomes reservados.
  Se achar algum, aborta com um erro que lista o id da Account e o nome, e o
  dev renomeia à mão antes de rodar de novo. Adotar trava dado criado pelo
  usuário com uma semântica que ele não escolheu, e renomear altera dado do
  usuário sem ChangeSet nem Undo (DA-013). Só existe dado de dev até o E13, e
  a colisão é rara. Um índice único em `lower(name)` para os nomes reservados
  fecha o bypass por variação de caixa. Há teste com uma colisão semeada em
  caixa mista. Depois do E13, migrations desse tipo precisam de uma
  estratégia explícita (candidata a ADR).

## Checklist

- [x] Type de sistema Resource + propriedades travadas + nomes reservados
- [x] Classificador URL → (kind, provider), com tabela de testes incluindo URLs maliciosas ou estranhas
- [x] Worker de fetch protegido contra SSRF (usa a fila de jobs do E2)
- [x] Extratores do MVP: web_page/article (readability + OpenGraph) e video/youtube (oEmbed)
- [x] Captura por colagem na UI e via REST
- [x] Ferramenta MCP `capture_resource` (idempotente)
- [x] Permitir Properties do usuário no Type Resource

## Critérios de saída

"Guarde estes 3 links e relacione com Comprar casa", dito no Claude.ai, gera
3 Resources sem duplicatas, relacionados, na Inbox e visíveis em Agent Changes.

## Fora de escopo

Upload de arquivos, extração de texto de PDF/documentos, share target mobile.
