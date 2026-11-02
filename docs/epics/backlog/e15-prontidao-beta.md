# E15 — Prontidão para beta

- **Ordem:** 15
- **Depende de:** E14
- **Última revisão:** 2026-11-01

## Contexto

O público do MVP é o próprio usuário primeiro e, depois, um **beta fechado por
convite**. O cadastro público só abre depois de revisão de segurança, termos e
política de privacidade. Este épico é o portão legal e operacional entre o uso
solo e as pessoas convidadas.

## Decisões

- **DA-090 — Uso solo → beta por convite → cadastro público só depois desta
  revisão** (usuário). O grafo guarda dados de saúde, família e finanças, e
  convidados do beta já são usuários externos.
- **DA-091 — OAuth em produção é obrigatório antes do primeiro convidado**
  (consenso).
- **DA-306 — Avaliação de risco de prompt injection indireta no portão do beta**
  (consenso do arquiteto e do analista; o usuário delegou a eles).
  - Os épicos E22 e E23 delegaram a este a varredura ampla (DA-295, DA-304), e
    até aqui nenhum item a cumpria. O cenário principal: um agente grava texto
    malicioso (no guia, num Resource, num nome de Tag) e outro agente o lê como
    instrução.
  - O item só pode fechar depois que E22 e E23 forem entregues. É **ordem de
    execução (DA-305), não dependência formal**: o E15 tem número menor que os
    dois e a regra só aceita dependência para número menor.
  - Por quê: o servidor garante que o conteúdo não ganha autoridade (filtro
    central, scopes, proposta), mas não garante que o LLM do agente do usuário
    obedeça ao rótulo. Isso precisa de avaliação e de texto honesto nos termos,
    não de promessa.

## Checklist

### Legal ⚖️ (requer validação jurídica humana)
- [ ] Política de privacidade: suboperadores, visitantes de shares, agentes conectados (o provedor de IA processa sob o contrato do usuário), retenção de logs, auditoria, ledger e backups
- [ ] Termos de uso + AUP (o usuário é responsável pelo que compartilha; dados de terceiros)
- [ ] PIA final (Lei 25, arts. 3.3 e 17), cobrindo hospedagem, embeddings, e-mail e agentes
- [ ] Responsável pela proteção de dados (Lei 25) designado e publicado
- [ ] Registro de incidentes de confidencialidade + procedimento de notificação (Lei 25, PIPEDA, LGPD art. 48)
- [ ] Transferência internacional (LGPD art. 33), se houver usuários no Brasil

### Segurança e operação
- [ ] Revisão de segurança: OAuth/CIMD, SSRF, isolamento entre contas, filtro de ocultos, shares
- [ ] Avaliação de risco de prompt injection indireta (DA-306), depois da entrega de E22 e E23: inventário das superfícies de conteúdo não confiável que chegam ao agente (`get_context`, busca, guia, Resources, corpo e Properties de Node, nomes de Tag e Type, descrições e candidatos de proposta)
- [ ] Suíte adversarial ponta a ponta: o agente lê conteúdo envenenado e tenta, por tools reais, escalar scope, apagar, exfiltrar Node oculto ou de outra conta, inundar a ontologia ou editar o guia; nenhum caminho amplia scope nem escreve fora do pipeline único (DA-306)
- [ ] Revisão e Undo sob ataque: a proposta maliciosa aparece legível no Agent Changes e o Undo funciona; conteúdo de guia ou Resource não vai a logs, export nem SharedGraphView sem designação (GEN-043) (DA-306)
- [ ] Decisão explícita sobre a dívida DT-1 (guia imutável por agente) e riscos residuais aceitos por escrito (DA-306)
- [ ] Evidência de um teste de restauração com o ledger reaplicado
- [ ] Runbooks (incidente, restauração, revogação em massa de shares e agentes)
- [ ] Smoke test de carga
- [ ] Fluxo de convite e controles anti-abuso

## Critérios de saída

Primeiro convidado entra no ambiente de produção com os termos aceitos e a
política publicada, e a avaliação de risco de injeção (DA-306) está documentada
com os riscos residuais aceitos.
