# E0 — Plataforma de desenvolvimento (walking skeleton)

- **Ordem:** 0
- **Depende de:** —
- **Última revisão:** 2026-10-04

## Contexto

O escopo v2 (`docs/global_v2.md`) transforma o Life Graph em uma camada de
contexto pessoal usada pelo usuário (UI 2.5D) e por agentes (API + MCP). Antes
de qualquer feature, o repositório precisa de um esqueleto que compile, teste e
rode localmente, já com as garantias de isolamento multi-tenant que não podem
ser adicionadas depois.

Este épico **não tem funcionalidade de produto** e **não faz deploy**. Tudo roda
em ambiente de desenvolvimento até o E13 (Infra & Deploy).

## Decisões

- **DA-001 — .NET 10 LTS no backend** (usuário + consenso). O .NET 9 sai de
  suporte em 2026-11-10. Começar um produto num runtime prestes a perder suporte
  não faz sentido. O .NET 10 tem suporte até nov/2028.
- **DA-002 — Monólito modular com vertical slices, num único deployable**
  (consenso). Um só host ASP.NET Core contém a API REST, o MCP, o servidor
  OpenIddict e os workers. Módulos: Accounts/Identity, Graph, Changes, Agents,
  Resources, Semantic, Sharing, Collections. Para um dev solo, um deployable
  evita escritas duplicadas e custo de infraestrutura (BE-006).
- **DA-003 — PostgreSQL + pgvector em adjacência relacional** (consenso,
  ADR: [docs/adr/0001-postgres-pgvector-adjacencia-relacional.md](../../adr/0001-postgres-pgvector-adjacencia-relacional.md)). Tabelas de nodes, relations e definições, travessia por
  CTE recursiva com limites, FTS com unaccent e vetores no mesmo banco. Neo4j
  foi rejeitado porque exigiria um segundo armazenamento para vetores e contas,
  custa mais caro e tem multi-tenancy fraca. Os grafos são pequenos (até ~10k
  Nodes), então profundidade 2 é trivial em SQL.
- **DA-004 — Isolamento por `account_id` + filtro central + RLS** (consenso).
  O contexto da conta é aplicado com `SET LOCAL` dentro da transação em toda
  requisição, inclusive leituras. Papel de migração separado do papel da
  aplicação, que não é dono das tabelas e não tem BYPASSRLS (DB-060). Acesso a
  outra conta retorna 404 (API-031). Vazar dados entre contas num grafo com
  saúde e família seria catastrófico, e retrofit de isolamento reescreve todas
  as queries.
- **DA-005 — Reuso parcial do limaj-framework** (consenso). Reaproveitamos
  `Abstractions` (Result/Error, exceções de domínio, `IUserIdentityGateway`) e
  `Web` (ResultExtensions, RequestRunner), consumidos como pacotes versionados.
  **Não** usamos `Persistence.EFCore`/`BaseEntity`/`BaseRepository` nos
  agregados do grafo: o soft delete por `IsActive` conflita com
  Delete/Purge/ChangeSet, o pacote referencia SqlServer e lê
  `local.settings.json` (Azure Functions), e o UnitOfWork não aplica
  `SET LOCAL`. Regra BOLA: acesso a outra conta aparece como `NotFound`, nunca
  403. Pré-requisito externo: migrar o limaj-framework para .NET 10, no
  repositório dele. *Revista (2026-10-04):* o `IUserIdentityGateway` saiu pela
  DA-094 (E1). O consumo do Result/Error e do `Web` foi redefinido pela DA-099
  (E2): `Abstractions` 2.0.0 já, e `Web` a partir da 3.0.0, com o formato `V3`
  e um `IErrorHttpMapper` próprio. O pré-requisito do .NET 10 foi cumprido na
  2.0.0.
- **DA-006 — Só ambiente de dev até o E13** (usuário). Hospedagem, backups e
  região ficam para o épico de Infra & Deploy. Para não forçar retrabalho, os
  **requisitos mínimos do host** ficam registrados já: Postgres ≥16,
  pgvector ≥0.7, papéis não-superusuário com RLS, papel da aplicação sem
  BYPASSRLS e pgvector disponível na região escolhida.
- **DA-007 — Stack de testes** (consenso). xUnit; Testcontainers.PostgreSql
  com a imagem `pgvector/pgvector:pg17` fixada por digest, mais Respawn;
  WebApplicationFactory; ArchUnitNET para fronteiras de módulos e para garantir
  que Domain/Application não usem AspNetCore, HttpContext nem MCP. No frontend:
  Vitest, Testing Library, vitest-axe, MSW e Playwright para poucos E2E.
- **DA-008 — Frontend** (usuário + consenso). React 19 + TypeScript strict +
  Vite (SPA, sem SSR), TanStack Query, React Router, Radix/shadcn-ui + Tailwind
  (acessibilidade, FE-030), React Hook Form + Zod e cliente tipado gerado do
  OpenAPI (openapi-typescript).
- **DA-093 — No devcontainer, os testes de integração usam o Postgres do
  compose, não Testcontainers** (usuário). O template do limaj-framework não
  monta o socket do Docker no devcontainer, e é isso que torna seguro rodar o
  Claude lá dentro sem prompts. Testcontainers precisa de um daemon Docker, então
  havia três saídas: montar o socket (dá ao container controle do Docker do host,
  na prática root), Docker-in-Docker (exige `privileged: true`) ou um fallback. A
  fixture usa Testcontainers com a imagem fixada por digest quando
  `LIFEGRAPH_TEST_DB_ADMIN` não está definida (CI, host). Dentro do devcontainer
  a variável está definida, e a fixture cria um banco `lifegraph_test_*`
  descartável no serviço `postgres`, aplica o mesmo bootstrap e as mesmas
  migrations, e o remove no fim. É o mesmo motor e a mesma imagem (DB-064). O
  isolamento do template fica intacto.

## Estrutura proposta

- **Repositório:** `LifeGraph.sln`, `src/LifeGraph.Host`, módulos em
  `src/LifeGraph.<Modulo>`, `src/LifeGraph.Infrastructure`, `tests/*`, `web/`.
- **Ambiente de dev:** devcontainer derivado do template do limaj-framework,
  com `compose.postgres` trocado pela imagem pgvector, `USE_FRONTEND=true`,
  `INSTALL_FUNC=false` e um serviço Mailpit. Sem socket do Docker: os testes de
  integração rodam contra o Postgres do compose (DA-093). O perfil `public`
  (cloudflared) entra no E3.
- **Banco:** `db/bootstrap/` cria os papéis `lifegraph_migrator` (dono) e
  `lifegraph_app` (sem BYPASSRLS), o banco e as extensões. A migration inicial
  concede privilégios ao papel da app e cria `app.current_account_id()`, que
  toda policy de RLS compara com `account_id`. O `AccountRlsInterceptor` publica
  a Account com `set_config(..., true)` (equivale a `SET LOCAL`, mas aceita
  parâmetro) no início de cada transação. Sem Account, nenhuma linha aparece.
- **Observabilidade:** logs estruturados com redação de PII e segredos
  (GEN-043) e instrumentação OpenTelemetry, com exporters ligados só no E13.

## Checklist

### Fase 1 — Solução e ambiente
- [x] Criar solução, Host, projeto de Infrastructure, módulos vazios e `.config/dotnet-tools.json`
- [x] Devcontainer/compose com Postgres+pgvector e Mailpit
- [x] Casca React (Vite + TS strict + Router + Query + Tailwind/shadcn)
- [x] Geração do cliente OpenAPI no build do frontend

### Fase 2 — Banco e isolamento
- [x] Pipeline de migrations versionadas (DB-010), naming snake_case
- [x] Papéis: owner de migração e papel da app sem BYPASSRLS
- [x] Harness de RLS: `SET LOCAL` do account na transação, aplicado por interceptor
- [x] Teste de integração: acesso cross-account retorna 404, e a RLS sozinha bloqueia quando o filtro da app é contornado

### Fase 3 — Qualidade
- [x] Testes de arquitetura (fronteiras de módulos; Domain sem AspNetCore/MCP)
- [x] Fixtures Testcontainers + WebApplicationFactory
- [x] CI (GitHub Actions): build, testes, `dotnet format --verify-no-changes`, `npm run lint/typecheck/test`
- [x] CI: `dotnet list package --vulnerable --include-transitive` e `npm audit --audit-level=high` falhando em high (GEN-063)
- [x] CI: gitleaks, Dependabot, SAST (CodeQL se o repo for público, senão Semgrep CE) — repo público, CodeQL
- [x] Logging estruturado com redação de PII; instrumentação OpenTelemetry sem exporter
- [x] Registrar stack e comandos no `CLAUDE.md`

> **Validação (2026-10-02):** dentro do devcontainer real (sem socket do Docker),
> `dotnet build`, `dotnet test` (26 testes) e `npm run lint/typecheck/test` (6
> testes) passaram; o mesmo vale pelo caminho do Testcontainers. CI e CodeQL
> verdes no GitHub no push para `master`. O Dependabot de npm não sobe o
> TypeScript para 6+ (ignorado em `dependabot.yml`; ver nota no CLAUDE.md).

## Critérios de saída

- `dotnet build`, `dotnet test` e `npm test` verdes no CI e no devcontainer.
- O teste de isolamento cross-account (filtro da app e RLS) passa.
- Nenhuma funcionalidade de produto neste épico.

## Fora de escopo

Login, entidades de domínio, deploy, hospedagem e backups (E13).
