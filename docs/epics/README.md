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
| E9 | [Camada semântica](done/e09-camada-semantica.md) | Embeddings, Soft Relations, busca semântica | E2, E7 |
| E10 | [Raiz da Conta e ancoragem](done/e10-raiz-e-ancoragem.md) | Raiz por conta, agentes sempre ancoram, view Desancorados | E2, E5, E6, E9 |
| E11 | [SharedGraphView](done/e11-shared-graph-view.md) | Link compartilhado no Free | E2, E6, E8, E10 |
| E12 | [Export e exclusão de conta](backlog/e12-export-e-exclusao-de-conta.md) | Portabilidade, purge imediato, ledger | E2–E11 |
| E13 | [Entitlements](backlog/e13-entitlements-free-premium.md) | Limites Free/Premium (discussão da Q3) | E5, E10, E11 |
| E14 | [Infra & Deploy](backlog/e14-infra-e-deploy.md) | Hospedagem no Canadá, backups, domínios | E0–E13 |
| E15 | [Prontidão para beta](backlog/e15-prontidao-beta.md) | Portão legal e operacional do beta por convite | E14 |
| E16 | [Fundação visual e acabamento](backlog/e16-fundacao-visual-e-acabamento.md) | Tokens, tema claro/escuro, gate de contraste, componentes transversais, brief para o Claude Design. Executa depois do E23, antes do E14 | E2, E6, E8, E10 |
| E17 | [Integração da marca](backlog/e17-integracao-da-marca.md) | Logo, paleta, fontes e ícones do Claude Design sobre o contrato do E16. Só entra após a entrega do usuário | E16 |
| E18 | [Denúncia de share por formulário](backlog/e18-denuncia-de-share.md) | Formulário anônimo em rota não listada no lugar do `mailto:`, via fila Postgres. Pós-MVP, depois do primeiro deploy | E11, E14, E15 |
| E19 | [Home com o grafo](done/e19-home-com-o-grafo.md) | A Home mostra o grafo local da Raiz da Conta com painel de entrada e estado vazio. **Executa logo depois do E21** | E5, E6, E10 |
| E20 | [Agent Changes: fila de revisão e lote](backlog/e20-agent-changes-fila-de-revisao-e-lote.md) | Fila "Aguardando você" separada do histórico e decisão em lote de ChangeProposals e marcador "revisado por você". **Executa depois do E19** | E5, E13 |
| E21 | [Busca honesta, leitura barata e guia de uso](done/e21-busca-honesta-e-leitura-barata.md) | Busca degradada avisa que pode estar incompleta, `get_context` com `detail=summary`, guia de uso e roteiro de segundo cliente MCP. **Executa logo depois do E13** | E4, E5, E9 |
| E22 | [Guia para agentes: Node de entrada](backlog/e22-guia-para-agentes-node-de-entrada.md) | A pessoa designa um Node de entrada que o `get_context` devolve, com testes adversariais de injeção. **Executa depois do E20** | E4, E10, E21 |
| E23 | [Tags e Types criados por agente](backlog/e23-tags-e-types-criados-por-agente.md) | O agente propõe Tag ou Type, com portão de similaridade no servidor, e a pessoa decide. **Executa depois do E22** | E5, E20, E21, E22 |

**Ordem de execução (DA-305):** E13 → E21 → E19 → E20 → E22 → E23 → E16 → E14 →
E15. O número do épico e a ordem de execução diferem: a regra de dependência vale
só para dependências, que apontam para número menor. O E14 não depende de E19 a
E23, e o E21 não depende do E13.

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
  os épicos (DA-001 a DA-306 nesta versão do roadmap). O próximo é o DA-307.
  Antes de numerar, confira o maior `DA-###` com `grep` em `docs/epics/`: o E12
  (DA-247 a DA-256) e o E13 (DA-260 em diante) já ocupam faixas.
