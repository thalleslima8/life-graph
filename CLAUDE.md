# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Estado atual

A stack é **.NET 10 + React**. O E0 (`docs/epics/done/e00-plataforma-dev.md`)
criou a plataforma: solução com Host, Infrastructure e oito módulos vazios,
migrations, papéis e harness de RLS, casca React, devcontainer e CI. Ainda **não
há funcionalidade de produto**. Também existem:

- a especificação do produto (`docs/global_v2.md`);
- os padrões de desenvolvimento (`docs/standards/`);
- o glossário de domínio (`GLOSSARY.md`);
- o roadmap do MVP em 15 épicos (`docs/epics/README.md`);
- as decisões de arquitetura duradouras em `docs/adr/` (ADRs 0001–0013, cada
  uma ligada ao seu `DA-###`). Leia antes de "corrigir" algo que parece
  estranho: provavelmente foi deliberado;
- a configuração do Claude Code (`.claude/settings.json`, que habilita os plugins
  `workflow@my-skills` e `writing@my-skills`).

O projeto foi criado a partir do ai-starter-kit (remote `template`).

## Propósito do projeto

**Life Graph**: um *Personal Context Graph*, uma camada pessoal de contexto
estruturado (assuntos, Resources, pessoas, eventos, projetos e suas relações)
usada de duas formas complementares: **pelo usuário**, numa interface visual
2.5D, e **por agentes de IA autorizados** (ChatGPT, Claude, Codex...) via API e
MCP. O contexto persistente pertence ao usuário; os agentes são clientes dele.

A especificação vigente, com conceitos, MVP, fases e modelo Free/Premium, está
em **`docs/global_v2.md`**. Consulte-a antes de decidir escopo ou modelagem.
`docs/global.md` é a versão anterior (app pessoal sem agentes), mantida só para
comparação. Ela **não** é fonte de verdade.

### Invariantes de arquitetura (de `docs/global_v2.md`)

Estas regras atravessam várias partes do sistema e devem guiar qualquer
implementação:

- **O grafo é o produto.** A UI 2.5D é a representação humana, a API é a
  interface programática e o MCP é a interface para agentes. A API é a porta
  principal do domínio: o MCP é um adapter que fala com a API, e **o domínio
  nunca depende do MCP**.
- **AI-assisted, not AI-dependent.** Criar/editar Nodes e Relations, filtrar,
  buscar por propriedades, navegar e renderizar o grafo funcionam sem LLM.
  Chamadas de modelo só em eventos pontuais (novo conteúdo, pergunta complexa,
  reorganização solicitada). Nunca ao abrir tela, navegar, mover Node, filtrar
  ou em agente de background constante. A IA interna é opcional: o raciocínio
  pode vir do agente do próprio usuário (BYO Agent).
- **Hard Graph vs Soft Graph.** Relações confirmadas e relações inferidas
  (embeddings, IA, coocorrência) são distintas no modelo. Toda Relation carrega
  `origin`, `confidence` e `strength`. Sugestões podem ser confirmadas,
  ignoradas, removidas ou promovidas a Hard.
- **Proveniência obrigatória.** Todo objeto criado ou alterado registra quem
  (`AgentIdentity`/usuário), por qual via (UI, API, MCP, import) e de qual
  fonte. Toda relação automática precisa ser explicável.
- **Alterações de agente são agrupadas e reversíveis.** Uma interação de agente
  gera um `GraphChangeSet` inspecionável e desfazível (Undo). O resultado
  sempre fica visível para o usuário no grafo.
- **Agentes operam sob permissões.** Scopes (`lifegraph.read`, `.write`,
  `.resources`, `.calendar`, `.share`, `.delete`) e níveis de ação (Safe /
  Write / Sensitive). Ações sensíveis podem exigir confirmação. `get_context`
  devolve só o subgrafo relevante, nunca o banco inteiro.
- **Views e Collections são queries**, não donas dos dados.
- **Ontologia flexível.** Types e Properties definidos pelo usuário. Tags
  complementam, não substituem Relations.
- **Compartilhamento por subgrafo (`SharedGraphView`).** A filtragem do
  subgrafo autorizado acontece no **backend**: quem recebe acesso nunca
  atravessa relações nem APIs para Nodes não autorizados.
- **SaaS desde o MVP (Free/Premium).** Limites são configuráveis e nunca
  bloqueiam o acesso a dados existentes. Exportar, visualizar, editar, excluir,
  consultar proveniência, desfazer alterações recentes de agentes e controlar
  permissões **nunca** viram Premium.
- **Camadas conceituais:** User Graph → Semantic Layer (embeddings,
  similaridade, clustering, ranking) → AI Layer (classificação, extração,
  sugestões, linguagem natural).

### Escopo do MVP

Core: Account, Nodes, Relations, Types, Properties, Tags, Collections,
Resources, Inbox, grafo local 2.5D (Semantic Zoom básico, Node Inspector),
busca semântica básica, embeddings, sugestões semânticas, GraphChangeSet,
Provenance, API básica, MCP Server básico, autenticação de agentes, permissões
básicas, Export, telas Recent/Agent Changes e **SharedGraphView (link
compartilhado, disponível no Free)**. O fluxo **agente conecta → busca →
recebe contexto → cria/atualiza → ChangeSet → UI atualiza → usuário inspeciona
ou desfaz** é a validação central do MVP.

Fora do MVP (não implementar sem decisão explícita): 3D completo, rede social,
marketplaces (plugins/agentes), colaboração multiusuário em tempo real, agentes
autônomos em background, gestão complexa de calendário, ingestão de e-mail,
sync de arquivos, Constellations avançadas, workflow builder. As fases 2 e 3
estão em `docs/global_v2.md` §68–69.

## Padrões de desenvolvimento — OBRIGATÓRIO

Todo código deste repositório segue as regras em **`docs/standards/`**. Leia o
arquivo da área antes de implementar ou revisar:

| Arquivo | Prefixo | Escopo |
| --- | --- | --- |
| `docs/standards/general.md` | `GEN` | Todo código: nomes, funções, erros, logs, segurança, testes/TDD, vertical slicing, feature flags |
| `docs/standards/backend.md` | `BE` | Serviços: camadas/slices, domínio, I/O, resiliência |
| `docs/standards/frontend.md` | `FE` | UI: componentes, estado, a11y, performance |
| `docs/standards/api-rest.md` | `API` | Contratos HTTP/REST |
| `docs/standards/database.md` | `DB` | Modelagem, migrations, consultas, transações |

- Níveis: **DEVE** (obrigatória), **DEVERIA** (desvio com motivo no PR),
  **PODE** (recomendação). Regras e precedência estão em
  `docs/standards/README.md`.
- Exceção a uma regra **DEVE** é registrada como `DA-###` no épico
  correspondente, citando o ID da regra e a justificativa.
- Reviews citam o ID da regra (ex.: `API-030: ...`).
- Os padrões são agnósticos de stack. As escolhas concretas (framework, linter,
  bibliotecas) ficam em "Convenções de código" e "Stack e camadas" abaixo.

## Convenções de código

- Use os termos de `GLOSSARY.md` no código, nos docs e nas conversas (ex.:
  GraphChangeSet, AgentIdentity, Outras propriedades, Purge).
- **Toda escrita no grafo passa pelo pipeline único** que gera GraphChangeSet +
  Provenance na mesma transação (DA-013). Nunca escreva direto no repositório
  por fora dele, nem use deletes genéricos.
- **Toda leitura passa pelo filtro central**, com principal Human,
  AgentIdentity ou ShareVisitor. Acesso a outra conta e a Node oculto para
  agentes respondem `NotFound`, nunca 403.
- Ferramentas MCP são adapters finos sobre os mesmos casos de uso do REST. O
  domínio nunca referencia MCP nem AspNetCore (há testes de arquitetura).
- TDD (GEN): regra de domínio com teste unitário; RLS, travessia, Undo e
  isolamento com teste de integração em Postgres real (Testcontainers).
- Nada de conteúdo de Node, valor de Property, URL ou token em logs (GEN-043).
- Exceção a uma regra **DEVE** dos padrões vira `DA-###` no épico (numeração
  global; veja `docs/epics/README.md`).

## Task management

Trabalho planejado vive em `docs/epics/`, em três pastas que representam o
estado do épico:

```
docs/epics/
├── backlog/         # planejado, ainda não iniciado
├── in-progress/     # em execução
└── done/            # concluído
```

- Um épico avança movendo o arquivo: `backlog/` → `in-progress/` →
  `done/`.
- Tarefas são checkboxes: `- [ ]` pendente, `- [x]` concluída.
- Todo épico tem o campo `Última revisão: YYYY-MM-DD`, atualizado sempre que
  o arquivo for alterado.
- Decisões são registradas no próprio épico como `DA-###` (numeração
  sequencial **global** entre os épicos), sempre com a justificativa — o
  porquê, não só o quê.
- A ordem e as dependências dos épicos ficam em `docs/epics/README.md`. Um
  épico nunca depende de outro com número maior.

## Convenções de commit

Conventional Commits (`feat`, `fix`, `chore`, `docs`, `refactor`, `test`, ...),
com mensagens em inglês.

```
<type>(<scope opcional>): <descrição no imperativo, minúscula>
```

## Stack e camadas específicas do projeto

Decisões completas: `docs/epics/done/e00-plataforma-dev.md` (DA-001 a
DA-008 e DA-093) e `e01-contas-e-login.md` (DA-009 a DA-012).

**Backend:** .NET 10 LTS, ASP.NET Core, como monólito modular com vertical
slices num único deployable. O deployable contém:
- REST (Minimal APIs);
- MCP via `ModelContextProtocol.AspNetCore`;
- OpenIddict + ASP.NET Core Identity (um único emissor para humanos e
  agentes);
- workers `BackgroundService` sobre uma fila em Postgres.

Módulos: Accounts/Identity, Graph, Changes, Agents, Resources, Semantic,
Sharing, Collections.

**Dados:**
- PostgreSQL + pgvector;
- EF Core 10 + Npgsql + Pgvector.EntityFrameworkCore + EFCore.NamingConventions
  (snake_case);
- valores de Property em JSONB com chave `property_id` (exceção a DB-008,
  DA-015);
- SQL parametrizado com CTE recursiva para `get_context`;
- RLS com `SET LOCAL` por transação; o papel da app não tem BYPASSRLS;
- IDs UUIDv7.

**limaj-framework:** o objetivo é padronizar no framework (DA-099). Quando
faltar algo, o caminho é pedir ao owner, não fazer um substituto local.
**Antes de qualquer mudança que toque o limaj, valide o estado do pacote** no
CHANGELOG do repositório `limajsolutions/limaj-framework` e no nuget.org. Siga
SemVer: subir de major é uma tarefa explícita. Use só `Limaj.Framework.Core` e
`Limaj.Framework.Web` 3.0.0, na mesma versão (DA-100). `Abstractions`,
`Application` e `Persistence.EFCore` não entram. O `Core` (`Result`/`Error`)
vale em qualquer camada; o `Web` só em `LifeGraph.Http`, Host e `*.Http`, pelo
`IHttpResultResponder` injetado, nunca pelas fachadas estáticas. Configure
`Format = V3`, `IncludeExceptionDetails = false` e
`IncludeDetailsOutsideValidation = false` explícitos, porque os padrões da
3.0.0 são outros (DA-102). Não use `Error.HttpStatusCode` (obsoleto) nem
`switch` exaustivo sobre `ErrorType`. Regra de negócio é
`ErrorType.Validation` com o 422 vindo do catálogo de códigos (DA-101). Falha
esperada é sempre `Result`, e os módulos não lançam as exceções do limaj
(DA-104). Código de erro de módulo leva prefixo (`accounts.*`, `graph.*`), e
código comum não (DA-105). Não use `IUserIdentityGateway` (DA-094, DA-106) nem
`BaseEntity`/`BaseRepository` nos agregados do grafo: o soft delete por
`IsActive` conflita com Delete/Purge e ChangeSet (DA-005). Erros de ownership
mapeiam para `NotFound`. Detalhes no E2, seção "Contrato de resultado e
erro".

**Frontend:** React 19 + TypeScript strict + Vite (SPA, autenticada por cookie
BFF), TanStack Query, React Router, Radix/shadcn-ui + Tailwind, React Hook
Form + Zod e cliente gerado do OpenAPI. A biblioteca do grafo 2.5D sai de um
spike no E6.

**Testes:**
- backend: xUnit, Testcontainers (`pgvector/pgvector:pg17`) + Respawn,
  WebApplicationFactory, cliente MCP do SDK, ArchUnitNET;
- frontend: Vitest + Testing Library + vitest-axe + MSW, e Playwright para
  poucos E2E.

**Ambiente:** só dev até o E13. Usa devcontainer/compose com Postgres+pgvector e
Mailpit. O perfil `public` (Cloudflare Tunnel) expõe apenas MCP/OAuth/login
para testar com ChatGPT/Claude.ai (DA-027/028).

**Comandos (confirmados no E0; rodam dentro do devcontainer):**

```bash
dotnet build LifeGraph.sln            # também regenera openapi/lifegraph.json (commitar se mudar)
dotnet test --solution LifeGraph.sln  # xUnit v3 sobre Microsoft.Testing.Platform (global.json)
dotnet test --project tests/<Projeto> --filter-method "<Namespace>.<Classe>.<Metodo>"   # um único teste
dotnet format LifeGraph.sln --verify-no-changes
dotnet ef migrations add <Nome> --project src/LifeGraph.Infrastructure --startup-project src/LifeGraph.Host --output-dir Persistence/Migrations
dotnet ef database update --project src/LifeGraph.Infrastructure --startup-project src/LifeGraph.Host
dotnet run --project src/LifeGraph.Host   # http://localhost:5000

cd web
npm ci && npm run dev                      # http://localhost:5173, proxy de /api e /health
npm run lint && npm run typecheck && npm test
npx vitest run <arquivo> -t "<nome>"   # um único teste
```

**Notas do E0:**
- `dotnet ef` lê `ConnectionStrings__Migrations` (papel `lifegraph_migrator`); a
  API usa `ConnectionStrings__Default` (papel `lifegraph_app`, sem BYPASSRLS).
  Nunca rode migrations com o papel da app.
- Toda tabela de Account tem `account_id` e uma policy contra
  `app.current_account_id()`. O valor vem do `AccountRlsInterceptor`, só dentro
  de transação: use `InAccountTransactionAsync` também em leituras, ou a RLS
  devolve zero linhas. Exceção: as tabelas do diretório de credenciais (`users`,
  `user_claims`, `user_logins`, `user_tokens`), pela DA-098.
- Testes de integração: Testcontainers no CI e no host; no devcontainer, banco
  descartável no serviço `postgres` via `LIFEGRAPH_TEST_DB_ADMIN` (DA-093).
- Parâmetros de log com dado pessoal ou segredo levam `[PersonalData]` ou
  `[SecretData]` num `[LoggerMessage]`; o redator apaga o valor (GEN-043).
- O cliente do frontend é `web/src/api/schema.gen.ts`, gerado de
  `openapi/lifegraph.json` antes de dev/build/typecheck/test (não versionado).
- TypeScript fica em 5.9 até typescript-eslint e openapi-typescript suportarem a 7.
