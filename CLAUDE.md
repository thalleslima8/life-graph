# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Estado atual

A stack está **decidida (.NET 10 + React)**, mas ainda **não há código**. O
primeiro épico (`docs/epics/backlog/e00-plataforma-dev.md`) cria a solução. Hoje
existem:

- a especificação do produto (`docs/global_v2.md`);
- os padrões de desenvolvimento (`docs/standards/`);
- o glossário de domínio (`GLOSSARY.md`);
- o roadmap do MVP em 15 épicos (`docs/epics/README.md`);
- as decisões de arquitetura duradouras em `docs/adr/` (ADRs 0001–0013, cada
  uma ligada ao seu `DA-###`). Leia antes de "corrigir" algo que parece
  estranho: provavelmente foi deliberado;
- a configuração do Claude Code (`.claude/settings.json`, que habilita o plugin
  `workflow@my-skills`).

O projeto foi criado a partir do ai-starter-kit (remote `template`).

Os comandos em "Stack e camadas" são os **planejados**. Confirme-os ao concluir
o E0.

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

Decisões completas: `docs/epics/backlog/e00-plataforma-dev.md` (DA-001 a
DA-008) e `e01-contas-e-login.md` (DA-009 a DA-012).

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

**limaj-framework:** reutilize apenas `Abstractions` (Result/Error, exceções,
`IUserIdentityGateway`) e `Web` (ResultExtensions, RequestRunner), como
pacotes. **Não** use `Persistence.EFCore`/`BaseEntity`/`BaseRepository` nos
agregados do grafo: o soft delete por `IsActive` conflita com Delete/Purge e
ChangeSet (DA-005). Erros de ownership mapeiam para `NotFound`.

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

**Comandos (planejados; confirmar no E0):**

```bash
dotnet build LifeGraph.sln
dotnet test LifeGraph.sln
dotnet test tests/<Projeto> --filter "FullyQualifiedName~<Namespace>.<Classe>.<Metodo>"   # um único teste
dotnet ef migrations add <Nome> --project src/LifeGraph.Infrastructure --startup-project src/LifeGraph.Host
dotnet ef database update --project src/LifeGraph.Infrastructure --startup-project src/LifeGraph.Host

cd web
npm ci && npm run dev
npm run lint && npm run typecheck && npm test
npx vitest run <arquivo> -t "<nome>"   # um único teste
```
