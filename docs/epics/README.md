# Roadmap do MVP

A ordem dos épicos do MVP do Life Graph (escopo em `docs/global_v2.md`). O
**estado** de cada épico é a pasta onde o arquivo está (`backlog/`,
`in-progress/`, `done/`); esta tabela guarda só a ordem e as dependências.
Ao mover um épico de pasta, não há nada para atualizar aqui.

**Regra de dependência:** um épico só pode depender de épicos com número
menor. Ao adicionar ou alterar um épico, verifique esta tabela.

| # | Épico | Objetivo | Depende de |
|---|---|---|---|
| E0 | [Plataforma de dev](backlog/e00-plataforma-dev.md) | Solução .NET 10 + React, devcontainer, migrations, RLS, CI com scans | — |
| E1 | [Contas e login](backlog/e01-contas-e-login.md) | Identity + OpenIddict como emissor, cookie BFF | E0 |
| E2 | [Núcleo de escrita](backlog/e02-nucleo-de-escrita.md) | Nodes/Relations/Types, ChangeSet + Provenance, Undo, Delete/Purge, Inbox, UI mínima | E1 |
| E3 | [Conexão de agentes](backlog/e03-conexao-de-agentes.md) | OAuth 2.1, consentimento, túnel de dev, primeira conexão com ChatGPT/Claude.ai | E1, E2 |
| E4 | [MCP leitura](backlog/e04-mcp-leitura.md) | `get_context`, busca, oculto para agentes, auditoria | E3 |
| E5 | [MCP escrita + Agent Changes](backlog/e05-mcp-escrita-agent-changes.md) | Escrita de agentes com proteções; **fecha o fluxo da §63** | E4 |
| E6 | [Grafo 2.5D](backlog/e06-grafo-2-5d.md) | Grafo local, Semantic Zoom básico | E2 |
| E7 | [Resources e captura](backlog/e07-resources-e-captura.md) | Resource como Node, fetch protegido contra SSRF, `capture_resource` | E2, E5 |
| E8 | [Tags, Collections, editor](backlog/e08-tags-collections-editor.md) | Tags, Collections como queries, editor completo de Properties | E2 |
| E9 | [Camada semântica](backlog/e09-camada-semantica.md) | Embeddings, Soft Relations, busca semântica | E2, E7 |
| E10 | [SharedGraphView](backlog/e10-shared-graph-view.md) | Link compartilhado no Free | E2, E6, E8 |
| E11 | [Export e exclusão de conta](backlog/e11-export-e-exclusao-de-conta.md) | Portabilidade, purge imediato, ledger | E2–E10 |
| E12 | [Entitlements](backlog/e12-entitlements-free-premium.md) | Limites Free/Premium (discussão da Q3) | E5, E10 |
| E13 | [Infra & Deploy](backlog/e13-infra-e-deploy.md) | Hospedagem no Canadá, backups, domínios | E0–E12 |
| E14 | [Prontidão para beta](backlog/e14-prontidao-beta.md) | Portão legal e operacional do beta por convite | E13 |

Paralelismo possível: E6 e E8 só dependem do E2 e podem andar em paralelo com
E3–E5.

## Regras transversais (valem para todo épico)

- Todo épico que adiciona um tipo de dado estende, no próprio Definition of
  Done, o **filtro central de leitura** (contas e oculto para agentes) e o
  **purge** (Delete, exclusão de conta). Assim o E11 não vira retrofit.
- Todo épico que adiciona um suboperador (provedor de IA, e-mail, rastreio de
  erros) acrescenta uma linha ao registro contínuo de **suboperadores e fluxos
  de dados**, que alimenta a PIA do E14.
- Toda exceção a uma regra **DEVE** de `docs/standards/` vira um `DA-###` no
  épico, citando o ID da regra.
- **Numeração de decisões:** os `DA-###` são sequenciais e **globais** entre
  os épicos (DA-001 a DA-092 nesta versão do roadmap). O próximo é o DA-093.
