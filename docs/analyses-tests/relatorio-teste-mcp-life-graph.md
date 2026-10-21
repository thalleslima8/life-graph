# Relatório de teste: MCP life-graph

**Data:** 2026-10-21 · **Cliente:** Claude Code · **Identidade:** "Claude Code" (`01a15b9a-448c-70ed-8246-412e2666cf15`) · **Escopos:** `lifegraph.read`, `lifegraph.write`

## Resumo
Os fluxos de leitura, proposta, aprovação humana e leitura posterior funcionaram sem erros. O erro que apareceu foi o esperado: `apply_changes` recusado quando a proposta exige aprovação do usuário.

## Ferramentas testadas

| Ferramenta | Resultado |
|---|---|
| `whoami` | OK. Retornou identidade e escopos. |
| `list_types` | OK. Retorna `[]`, porque o graph não tem Types. |
| `search_graph` | OK. Vazio antes da aprovação. Depois da aprovação achou "Transport Fever 3". |
| `suggest_changes` | OK. Proposta `01a15ee2-f1c8-71c9-a63e-b409d6692326` com 7 nodes e 9 relações, usando `ref` para ligar nodes criados na mesma proposta. Voltou `pending_approval` e `requiresUser: true`. |
| `get_proposal` | OK. Foi de `proposed` para `applied` (`changeSetId` `01a15ee4-83ef-7952-9f9b-65893bbf7674`). |
| `apply_changes` | Recusada, como esperado. Erro: "Only the person can approve this proposal, in Agent Changes." Isso é regra de aprovação, não bug. |
| `get_context` (`depth: 2`) | OK. Voltou 7 nodes e 9 edges, sem truncamento, com proveniência (ator, canal `mcp`, data) e `trust: "untrusted"` nos textos escritos por agente. |

## Fluxo validado de ponta a ponta
1. O agente propôs as mudanças com `suggest_changes`.
2. A proposta ficou aguardando em Agent Changes.
3. O usuário aprovou pelo browser.
4. O agente confirmou com `get_proposal` e leu o resultado com `search_graph` e `get_context`.

## Observações
- **Graph sem Types:** `list_types` está vazio. Os nodes foram criados sem tipo, e os `properties` voltaram vazios. Não foi possível confirmar se o `suggestedType` foi guardado.
- **Aprovação exigida:** a proposta pediu aprovação (`suggested` e `approval_required`). A causa não está explícita na resposta; a hipótese é a falta de Types.
- **Servidor do GitHub:** o `plugin:github:github` falhou na conexão (400, "Authorization header is badly formatted"). É um servidor diferente e não afetou o life-graph.

## Não testado
`create_node` e `create_relation` diretos, `update_node`, `remove_relation`, `withdraw_proposal`, `get_node`, `delete_node` e `create_type`, se existir.

## Dados de teste no graph
Sete nodes de exemplo (Transport Fever 3, Ferrovias, Rodovias, Cadeias de carga, Transporte de passageiros, Primeira campanha: plano de partida e Dica: rotas curtas primeiro), com 9 relações. Id do nó central: `01a15ee4-83ef-7e79-a70f-7cb2e0ee4675`.

## Conclusão
Leitura, escrita via proposta, aprovação humana e consulta de contexto funcionam. Falta cobrir as ferramentas de edição e remoção.
