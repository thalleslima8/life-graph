# E7 — Resources e captura

- **Ordem:** 7
- **Depende de:** E2, E5 (ferramenta MCP)
- **Última revisão:** 2026-10-02

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

## Checklist

- [ ] Type de sistema Resource + propriedades travadas + nomes reservados
- [ ] Classificador URL → (kind, provider), com tabela de testes incluindo URLs maliciosas ou estranhas
- [ ] Worker de fetch protegido contra SSRF (usa a fila de jobs do E2)
- [ ] Extratores do MVP: web_page/article (readability + OpenGraph) e video/youtube (oEmbed)
- [ ] Captura por colagem na UI e via REST
- [ ] Ferramenta MCP `capture_resource` (idempotente)
- [ ] Permitir Properties do usuário no Type Resource

## Critérios de saída

"Guarde estes 3 links e relacione com Comprar casa", dito no Claude.ai, gera
3 Resources sem duplicatas, relacionados, na Inbox e visíveis em Agent Changes.

## Fora de escopo

Upload de arquivos, extração de texto de PDF/documentos, share target mobile.
