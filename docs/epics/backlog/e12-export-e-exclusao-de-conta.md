# E11 — Export e exclusão de conta

- **Ordem:** 11
- **Depende de:** E2–E10 (precisa cobrir todas as tabelas que guardam dados)
- **Última revisão:** 2026-10-02

## Contexto

Exportar e excluir são direitos básicos que nunca viram Premium (v2 §61, §77) e
obrigações legais (portabilidade e eliminação na LGPD, PIPEDA e Lei 25). O
épico vem depois de todos os que geram dados, para o export e o purge cobrirem
tudo.

## Decisões

- **DA-082 — Exclusão de conta é purge imediato, "deletou perdeu"**
  (usuário). Não há período de carência. Salvaguardas (consenso):
  - **Antes:** oferecer o export, sem obrigar; exigir reautenticação recente
    (≤5 min) e confirmação digitada, com aviso de irreversível; enviar um
    e-mail de confirmação antes de apagar o endereço.
  - **Revogar na hora:** sessões (security stamp), autorizações e tokens
    OpenIddict, inclusive refresh, AgentIdentities, shares e jobs pendentes;
    cancelar propostas.
  - **Purgar** tudo que tem `account_id`: Nodes (inclusive tombstones),
    Relations, Types, definições, Resources e metadados, embeddings, Soft
    Relations e supressões, ChangeSets e Provenance, auditoria de leitura,
    cache de fetch. Depois, o usuário do Identity, cujo e-mail volta a ficar
    disponível.
  - Nenhuma ferramenta MCP exclui conta.
  - O acesso morre de forma síncrona. Os dados podem ser apagados por um job
    em minutos, já com a conta inutilizável, e esse prazo é declarado.
  - O que já foi enviado a ChatGPT ou Claude está fora do nosso controle, e
    isso é informado ao usuário.
  ADR: [docs/adr/0005-delete-separado-de-purge.md](../../adr/0005-delete-separado-de-purge.md)
- **DA-083 — "Deletou perdeu" não é absoluto** (consenso). Fica um **recibo de
  exclusão sem PII** (id da conta com hash + timestamp), como evidência de
  conformidade. Logs de segurança têm retenção curta. Se houver cobrança no
  futuro (E12), registros fiscais têm retenção legal, um conflito a resolver
  lá. A política de privacidade declara isso.
- **DA-084 — O registro de purges (ledger) é gravado neste épico, na mesma
  transação do purge** (consenso após contra-argumento).
  - O ledger guarda só IDs e timestamps de contas e Nodes purgados, sem e-mail,
    nome ou conteúdo. É o mesmo registro do recibo do DA-083.
  - Purges feitos antes do ledger existir não podem ser registrados depois,
    por isso ele não espera o E13.
  - O E13 só **consome** o ledger: reaplica após restaurar um backup e define
    a retenção.
  - ⚖️ Os IDs são dados pseudonimizados, e a retenção do ledger deve casar com
    a janela de backup, com validação jurídica.
  ADR: [docs/adr/0005-delete-separado-de-purge.md](../../adr/0005-delete-separado-de-purge.md)
- **DA-085 — Export em JSON (e Markdown) com Provenance** (consenso). Inclui
  Outras propriedades e definições de Type. Dados derivados (embeddings, Soft
  Relations) ficam excluídos ou marcados como derivados. O export está sempre
  disponível, em qualquer plano.

## Checklist

- [ ] Export JSON completo (Nodes, Relations, Types, definições, Tags, Collections, Resources, ChangeSets, Provenance), mais Markdown
- [ ] Fluxo de exclusão de conta: oferta de export, reautenticação, confirmação digitada, e-mail
- [ ] Revogação síncrona e purge por `account_id`
- [ ] Recibo/ledger sem PII na mesma transação
- [ ] **Teste guiado pelo schema:** toda tabela com `account_id` é coberta pelo purge da conta
- [ ] Texto de aviso sobre dados já enviados a agentes externos

## Critérios de saída

Depois de excluir uma conta, nenhuma linha com aquele `account_id` sobra (teste
automatizado), e o export de outra conta inclui todos os tipos de dado.

## Fora de escopo

Reaplicar o ledger após restaurar um backup (E13), importação.
