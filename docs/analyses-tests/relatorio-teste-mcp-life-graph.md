# Relatório de teste: MCP life-graph

**Data:** 2026-10-21 · **Cliente:** Claude Code, no host de dev (sem túnel) · **Identidade:** "Claude Code" (`01a15b9a-448c-70ed-8246-412e2666cf15`) · **Escopos:** `lifegraph.read`, `lifegraph.write`

Este relatório é a evidência dos aceites manuais do E3, do E4, do E5 e do E6. A prova com Claude.ai e ChatGPT depende de domínio e foi para o E14 (DA-147).

## Resumo
Funcionaram sem erros:
- conexão via CIMD e revogação;
- leitura com filtro de Nodes ocultos;
- escrita por proposta e por escrita direta;
- aprovação humana e Undo;
- destaque de mudança não revisada no grafo local.

O único erro foi o esperado: `apply_changes` é recusado quando a proposta exige aprovação da pessoa.

## Conexão e revogação (E3)
- O Claude Code conectou via CIMD, sem cliente pré-registrado no host, depois da correção do CSP do consentimento (DA-144).
- O `whoami` devolveu a identidade e os escopos.
- Depois de revogar a conexão em "Agentes conectados", a chamada seguinte ao `whoami` foi recusada, e o cliente pediu nova autenticação ("needs you to sign in again").

## Ferramentas testadas

| Ferramenta | Resultado |
|---|---|
| `whoami` | OK. Retornou a identidade e os escopos. Depois da revogação, foi recusado. |
| `list_types` | OK. Retorna `[]`, porque o grafo não tem Types. |
| `search_graph` | OK. Vazio antes da aprovação; depois achou "Transport Fever 3". Não acha Nodes ocultos para agentes nem Nodes desfeitos. |
| `suggest_changes` | OK. Proposta `01a15ee2-f1c8-71c9-a63e-b409d6692326` com 7 Nodes e 9 Relations, ligando os Nodes novos por `ref`. Voltou `pending_approval`. |
| `get_proposal` | OK. Passa de `proposed` para `applied`. Depois de um Undo, continua `applied` (veja Observações). |
| `apply_changes` | Recusado, como esperado: "Only the person can approve this proposal, in Agent Changes." |
| `get_context` (`depth: 2`) | OK. Traz proveniência (ator, canal `mcp`, data) e `trust: "untrusted"` nos textos escritos por agente. |
| `get_node` | OK. Um Node oculto para agentes responde "The node was not found." |
| `create_node` / `create_relation` | OK. Com a aprovação exigida, viram proposta. Sem ela, são aplicados direto (`applied`, com `changeSetId`). |

## Nodes ocultos para agentes (E4)
- O Node "Rodovias" (`01a15ee4-83ef-7396-b157-71afb663e7df`) foi marcado como oculto para agentes na UI.
- O `get_node` dele respondeu "not found", e o `search_graph("Rodovias")` voltou vazio.
- O `get_context` do Node central passou de 7 Nodes e 9 edges para 6 e 7. As duas Relations de Rodovias sumiram, e nenhuma aresta ficou apontando para ele.
- O agente concluiu que o Node tinha sido apagado. Ou seja, não conseguiu distinguir oculto de inexistente, que é o comportamento exigido.

## Escrita, aprovação e Undo (E5)
1. Pedido: "em uma única proposta, crie o Node Linha Norte e uma Relation dele com o Node existente Ferrovias". A proposta `01a15f21-6ab8-7c0e-a985-cf22768dab93` saiu com 2 operações: o Node, com `ref`, e a Relation `related_to` com o id de Ferrovias.
2. A proposta ficou aguardando em Agent Changes, com o motivo "Você exige aprovação deste agente".
3. A pessoa aprovou (ChangeSet `01a15f21-ddc8-7abc-9f93-68919f50f49c`) e desfez pela tela do GraphChangeSet.
4. Depois disso, `search_graph("Linha Norte")` voltou vazio, e Ferrovias ficou só com as 2 Relations de antes.
5. Uma tentativa anterior, só com `create_node`, gerou outra proposta. A pessoa a recusou, e não houve duplicado.

## Destaque de mudança não revisada (E6)
- Com "Exigir aprovação para escritas" desligado, o agente criou direto o Node "Linha Oeste" (`01a15f66-b079-7e3e-bac9-0e5515663fef`) e uma Relation com Ferrovias.
- No grafo local de Ferrovias, os dois apareceram como "mudança de agente não revisada".
- O Inspector mostrou o aviso e o link "Revisar em Agent Changes". Depois de "Marcar revisado", o destaque sumiu.
- Com a aprovação ligada, o destaque não aparece. Isso é esperado: o ChangeSet que nasce de uma aprovação já sai revisado, porque aprovar já é revisar.

## Observações
- **Por que a proposta pedia aprovação:** a causa era a configuração "Exigir aprovação para escritas" do Claude Code. A Agent Changes mostra o motivo "Você exige aprovação deste agente". A hipótese inicial, de que vinha da falta de Types, estava errada. A mesma configuração explica por que o `create_node` da Linha Norte virou proposta e o da Linha Oeste foi aplicado: entre as duas chamadas, a configuração foi desligada. A política não depende do histórico.
- **`get_proposal` depois do Undo:** continua `applied`. Isso é esperado (DA-136): a proposta foi aplicada, e o desfazer fica no GraphChangeSet, que passa a Reverted. Para saber o estado do grafo, o agente precisa ler os Nodes.
- **Grafo sem Types:** `list_types` está vazio, e os Nodes foram criados sem Type. Não deu para confirmar se o `suggestedType` foi guardado.
- **Servidor do GitHub:** o `plugin:github:github` falhou ao conectar (400, "Authorization header is badly formatted"). É outro servidor e não afetou o life-graph.

## Não testado
`update_node`, `remove_relation`, `withdraw_proposal`, `delete_node` e `create_type`, se existir.

## Dados de teste no grafo
- Nodes de exemplo ligados a Transport Fever 3 (`01a15ee4-83ef-7e79-a70f-7cb2e0ee4675`).
- "Rodovias", oculto para agentes.
- "Linha Sul" e "Linha Sudoeste", criados por propostas aprovadas.
- "Linha Oeste", criado direto e já revisado.
- A proposta da "Linha Sudeste" foi recusada, e "Exigir aprovação para escritas" foi religado depois dos testes.

## Conclusão
Funcionam: conexão via CIMD, revogação, leitura com filtro de ocultos, escrita por proposta e direta, aprovação humana, Undo e destaque de mudança não revisada. Faltam as ferramentas de edição e de remoção, e a mesma prova com Claude.ai e ChatGPT pelo domínio (E14).
