# E14 — Prontidão para beta

- **Ordem:** 14
- **Depende de:** E13
- **Última revisão:** 2026-10-02

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
- [ ] Evidência de um teste de restauração com o ledger reaplicado
- [ ] Runbooks (incidente, restauração, revogação em massa de shares e agentes)
- [ ] Smoke test de carga
- [ ] Fluxo de convite e controles anti-abuso

## Critérios de saída

Primeiro convidado entra no ambiente de produção com os termos aceitos e a
política publicada.
