# E5 — Escrita via MCP + Agent Changes

- **Ordem:** 5
- **Depende de:** E4
- **Última revisão:** 2026-10-02

## Contexto

Com este épico, o fluxo da v2 §63 funciona de ponta a ponta: o agente cria ou
atualiza Nodes, gera um GraphChangeSet, a UI atualiza e o usuário inspeciona ou
desfaz. A v2 troca o "IA propõe, usuário decide" do v1 por "resultado visível
e automação reversível".

## Decisões

- **DA-040 — Agentes escrevem direto no nível Write** (usuário). Criar, editar
  (inclusive conteúdo escrito pelo usuário) e remover relações, inclusive as
  criadas pelo usuário, são aplicados na hora como ChangeSet que pode ser
  desfeito. Remover uma relação é menos destrutivo que sobrescrever texto, por
  isso não faz sentido protegê-la mais.
  ADR: [docs/adr/0009-agentes-escrevem-direto-e-aplicam-propostas.md](../../adr/0009-agentes-escrevem-direto-e-aplicam-propostas.md)
- **DA-041 — O nível Sensitive é sempre Proposed e só é aprovado pelo usuário
  na UI** (consenso). O nível cobre delete, bulk, share e permissões. Defende
  contra prompt injection que leve um agente a apagar ou compartilhar dados.
- **DA-042 — Mudar o Type de um Node do usuário é Proposed** (consenso). Isso
  pode inverter a herança de "oculto para agentes", o que na prática é mudar
  permissões, e mexe nos valores. Um agente pode definir o Type de um Node
  criado por ele mesmo. Ele nunca atribui um Type oculto, nunca edita
  definições de Type, nunca mexe na flag de oculto, nunca mexe na flag da Inbox
  e nunca cria shares.
- **DA-043 — `apply_changes` é uma ferramenta MCP** (usuário), com estas
  proteções, todas checadas de novo no momento de aplicar (consenso):
  1. a proposta é da mesma AgentIdentity (de outra identidade, 404);
  2. o status é Proposed e não expirou;
  3. não contém nenhuma operação Sensitive;
  4. uma proposta criada com o toggle ligado fica marcada como `requires_user`
     para sempre;
  5. a versão base confere (senão, conflito);
  6. autorização, scopes e filtro de ocultos rodam de novo;
  7. chave de idempotência;
  8. conteúdo idêntico a uma proposta rejeitada não pode ser reenviado.
  ADR: [docs/adr/0009-agentes-escrevem-direto-e-aplicam-propostas.md](../../adr/0009-agentes-escrevem-direto-e-aplicam-propostas.md)
- **DA-044 — Toggle "exigir aprovação para escritas" por AgentIdentity, padrão
  desligado** (usuário). Mantém o caminho simples da §63: se o usuário deixa o
  agente sobrescrever, a escolha é dele. O toggle aparece na tela de
  consentimento.
- **DA-045 — Limite rígido de 50 operações por ChangeSet**, recusado com erro
  de validação acima disso (usuário, seguindo o Arquiteto). Subir o limite
  depois não quebra nenhum agente; baixar quebraria. Aumentar se a telemetria
  mostrar divisões frequentes. A definição de "operação" (criar, atualizar ou
  apagar Node, Relation, vínculo de Tag ou valor) fica neste épico.
- **DA-046 — Bulk: mais de 20 operações que alteram ou removem entidades do
  usuário num ChangeSet exigem aprovação** (consenso). Criações do próprio
  agente não contam, para não travar capturas legítimas.
- **DA-047 — Janela deslizante contra divisão de lotes: obrigatória no MVP**
  (consenso). Um agente com defeito, ou manipulado por conteúdo capturado,
  poderia contornar o DA-046 mandando vários ChangeSets de 19 operações.
  Exemplo: no máximo 20 alterações sobre dados do usuário a cada 10 minutos;
  acima disso, os ChangeSets viram Proposed. **Pendente:** números finais e se
  a contagem é por AgentIdentity ou pela Account (somando todos os agentes).
  Decidir ao iniciar este épico.
- **DA-048 — Propostas expiram em 7 dias** (consenso). O usuário costuma revisar
  no dia seguinte ou no fim de semana. Propostas expiradas ganham o status
  `Expired`, continuam visíveis e ficam sujeitas à redação no purge.
- **DA-049 — Node sem Type criado por agente vai para a Inbox, com uma dica
  `suggested_type`** (usuário + consenso). Nenhum Type é criado. Node sem Type
  aceita título, corpo e relações, mas não propriedades. Types propostos por
  agentes ficam para depois do MVP.
- **DA-050 — `update_node` exige `expected_version`** (consenso). Sem isso, o
  agente sobrescreveria uma edição concorrente do usuário.
- **DA-051 — Rate limits de segurança por AgentIdentity**, por exemplo 60
  escritas/min mais um teto diário (consenso). São limites de segurança, não
  de plano, e não esperam o E12.

## Checklist

- [ ] Ferramentas: `create_node`, `update_node`, `create_relation`, `remove_relation`, `suggest_changes`, `apply_changes`, retirar a própria proposta
- [ ] Política central de escrita de agentes (Write imediato, Sensitive/requires_user/bulk viram Proposed)
- [ ] Proteções do `apply_changes` (DA-043)
- [ ] Toggle por AgentIdentity (padrão off) na tela de consentimento e em Agentes conectados
- [ ] Limite de 50, bulk de 20, janela deslizante (definir parâmetros), rate limits
- [ ] Job de expiração de propostas (7 dias)
- [ ] Tela **Agent Changes**: feed por agente, "não revisado", diff campo a campo com aviso "sobrescreveu um valor seu", aprovar/rejeitar total ou parcial, Undo
- [ ] Tela de GraphChangeSet (v2 §66)
- [ ] Ligação com a Inbox para Nodes sem Type de agente

### Testes obrigatórios
- [ ] Um teste por proteção do `apply_changes`
- [ ] Limites 50/51 e 20/21; janela no limite, expirando, com dois agentes
- [ ] Proposta criada com o toggle ligado não destrava ao desligá-lo
- [ ] Token de agente chamando a aprovação de Sensitive é recusado

## Critérios de saída

A partir do Claude.ai ou do ChatGPT, "guarde isso e relacione com X" gera Nodes
e Relations visíveis em Agent Changes, e o usuário consegue desfazer.

## Fora de escopo

`capture_resource` (E7), ferramentas de Tag (E8), criação de shares por agente.
