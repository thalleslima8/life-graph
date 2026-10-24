# Roadmap do MVP

A ordem dos épicos do MVP do Life Graph (escopo em `docs/global_v2.md`). O
**estado** de cada épico é a pasta onde o arquivo está (`backlog/`,
`in-progress/`, `done/`); esta tabela guarda só a ordem e as dependências.
Ao mover um épico de pasta, não há nada para atualizar aqui.

**Regra de dependência:** um épico só pode depender de épicos com número
menor. Ao adicionar ou alterar um épico, verifique esta tabela.

| # | Épico | Objetivo | Depende de |
|---|---|---|---|
| E0 | [Plataforma de dev](done/e00-plataforma-dev.md) | Solução .NET 10 + React, devcontainer, migrations, RLS, CI com scans | — |
| E1 | [Contas e login](done/e01-contas-e-login.md) | Identity + OpenIddict como emissor, cookie BFF | E0 |
| E2 | [Núcleo de escrita](done/e02-nucleo-de-escrita.md) | Nodes/Relations/Types, ChangeSet + Provenance, Undo, Delete/Purge, Inbox, UI mínima | E1 |
| E3 | [Conexão de agentes](done/e03-conexao-de-agentes.md) | OAuth 2.1, consentimento, túnel de dev, primeira conexão com ChatGPT/Claude.ai | E1, E2 |
| E4 | [MCP leitura](done/e04-mcp-leitura.md) | `get_context`, busca, oculto para agentes, auditoria | E3 |
| E5 | [MCP escrita + Agent Changes](done/e05-mcp-escrita-agent-changes.md) | Escrita de agentes com proteções; **fecha o fluxo da §63** | E4 |
| E6 | [Grafo 2.5D](done/e06-grafo-2-5d.md) | Grafo local, Semantic Zoom básico | E2 |
| E7 | [Resources e captura](done/e07-resources-e-captura.md) | Resource como Node, fetch protegido contra SSRF, `capture_resource` | E2, E5 |
| E8 | [Tags, Collections, editor](done/e08-tags-collections-editor.md) | Tags, Collections como queries, editor completo de Properties | E2 |
| E9 | [Camada semântica](backlog/e09-camada-semantica.md) | Embeddings, Soft Relations, busca semântica | E2, E7 |
| E10 | [Raiz da Conta e ancoragem](backlog/e10-raiz-e-ancoragem.md) | Raiz por conta, agentes sempre ancoram, view Desancorados | E2, E5, E6, E9 |
| E11 | [SharedGraphView](backlog/e11-shared-graph-view.md) | Link compartilhado no Free | E2, E6, E8, E10 |
| E12 | [Export e exclusão de conta](backlog/e12-export-e-exclusao-de-conta.md) | Portabilidade, purge imediato, ledger | E2–E11 |
| E13 | [Entitlements](backlog/e13-entitlements-free-premium.md) | Limites Free/Premium (discussão da Q3) | E5, E10, E11 |
| E14 | [Infra & Deploy](backlog/e14-infra-e-deploy.md) | Hospedagem no Canadá, backups, domínios | E0–E13 |
| E15 | [Prontidão para beta](backlog/e15-prontidao-beta.md) | Portão legal e operacional do beta por convite | E14 |

Paralelismo possível: E6 e E8 só dependem do E2 e podem andar em paralelo com
E3–E5.

## Regras transversais (valem para todo épico)

- Todo épico que adiciona um tipo de dado estende, no próprio Definition of
  Done, o **filtro central de leitura** (contas e oculto para agentes) e o
  **purge** (Delete, exclusão de conta). Assim o E12 não vira retrofit.
- Todo épico que adiciona um suboperador (provedor de IA, e-mail, rastreio de
  erros) acrescenta uma linha ao registro contínuo de **suboperadores e fluxos
  de dados**, que alimenta a PIA do E15.
- Toda exceção a uma regra **DEVE** de `docs/standards/` vira um `DA-###` no
  épico, citando o ID da regra.
- **Numeração de decisões:** os `DA-###` são sequenciais e **globais** entre
  os épicos (DA-001 a DA-147 nesta versão do roadmap). O próximo é o DA-148.
