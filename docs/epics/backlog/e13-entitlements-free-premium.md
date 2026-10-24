# E12 — Entitlements Free/Premium

- **Ordem:** 12
- **Depende de:** E5, E10
- **Última revisão:** 2026-10-02

## Contexto

A v2 (§52–61) pede uma arquitetura preparada para Free/Premium desde o MVP, com
os limites da §55. O modelo comercial ainda não está claro para o usuário, que
pediu para discutir a **Q3** neste épico. Até lá, os limites de segurança
(E5) e os limites de share (E10) funcionam como constantes no código.

## Decisões

- **DA-086 — Limites nunca bloqueiam acesso a dados existentes** (invariante,
  §56 e §61). Ao atingir um limite, só a criação nova é impedida. Visualizar,
  editar, exportar, excluir, consultar proveniência, desfazer alterações de
  agentes e controlar permissões **nunca** são bloqueados nem viram Premium.
  Testes cobrem isso.
- **DA-087 — Limites de segurança ≠ limites de plano** (consenso). Rate limits,
  o teto de 50 operações, o bulk de 20 e a janela deslizante ficam no E5 e não
  dependem de plano.

## Pendências (decisão do usuário neste épico)

- [ ] **Q3:** só o mecanismo de limites (plano atribuído manualmente, sem cobrança) ou já com cobrança? Os especialistas recomendam começar sem cobrança.
- [ ] Unidade da quota de escrita MCP: por operação ou por ChangeSet? Undo nunca consome quota (consenso).
- [ ] O que conta como "Agent Connection" para o limite do Free (1)
- [ ] O que acontece com dados acima do limite num downgrade de Premium para Free (devem continuar acessíveis)

## Checklist

- [ ] Modelo de plano + limites lidos de configuração + contadores de uso
- [ ] Aplicação só nos comandos de criação e na quota de escrita de agentes
- [ ] Converter as constantes de share (E10) em limites por plano
- [ ] Mensagens de limite atingido sem bloquear o acesso

## Fora de escopo

Gateway de pagamento, a menos que a Q3 decida o contrário.
