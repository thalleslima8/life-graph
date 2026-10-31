# E12 — Export e exclusão de conta

- **Ordem:** 12
- **Depende de:** E2–E11 (precisa cobrir todas as tabelas que guardam dados)
- **Última revisão:** 2026-10-31

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
  futuro (E13), registros fiscais têm retenção legal, um conflito a resolver
  lá. A política de privacidade declara isso.
- **DA-084 — O registro de purges (ledger) é gravado neste épico, na mesma
  transação do purge** (consenso após contra-argumento).
  - O ledger guarda só IDs e timestamps de contas e Nodes purgados, sem e-mail,
    nome ou conteúdo. É o mesmo registro do recibo do DA-083.
  - Purges feitos antes do ledger existir não podem ser registrados depois,
    por isso ele não espera o E14.
  - O E14 só **consome** o ledger: reaplica após restaurar um backup e define
    a retenção.
  - ⚖️ Os IDs são dados pseudonimizados, e a retenção do ledger deve casar com
    a janela de backup, com validação jurídica.
  ADR: [docs/adr/0005-delete-separado-de-purge.md](../../adr/0005-delete-separado-de-purge.md)
- **DA-085 — Export em JSON (e Markdown) com Provenance** (consenso). Inclui
  Outras propriedades e definições de Type. Dados derivados (embeddings, Soft
  Relations) ficam excluídos ou marcados como derivados. O export está sempre
  disponível, em qualquer plano.

### Decisões da execução (/flow, 2026-10-30)

- **DA-247 — Cobertura do export** (consenso). Entram Nodes vivos, Hard
  Relations, Types, definições, Tags, Collections, Resources (com metadados de
  fetch), GraphChangeSets, Provenance, o e-mail da Account e, só como
  metadados e sem segredo, AgentIdentities, SharedGraphViews, ChangeProposals e
  a auditoria de leitura de agentes. Ficam de fora tombstones (o usuário
  apagou; a janela de 30 dias serve só ao Undo, e importar de volta
  ressuscitaria o que ele apagou), embeddings e Soft Relations (inferidos e
  regeneráveis; DA-085 permite; mantém o teste "leituras só veem Hard" da
  DA-178 intacto). Um teste guiado pelo schema exige que toda tabela com
  `account_id` esteja exportada ou numa lista de exclusão com motivo. Se o
  jurídico exigir tombstones, entram com `deleted: true`, de forma aditiva.
- **DA-248 — Export é um job com 202** (provisional — decidido pelo
  architect; outra visão: zip síncrono em streaming com exceção a
  API-060/DB-023, sem cópia de PII em repouso; para reverter: remover o job, a
  tabela e o worker de expiração, e streamar o zip do mesmo snapshot,
  registrando a exceção). `POST` devolve 202; o worker monta um zip
  (`export.json` + um `.md` por Node, nomeado pelo ID) dentro de uma transação
  read-only `REPEATABLE READ`, guardado em tabela Postgres com `account_id` e
  RLS, teto de tamanho configurável (~50 MB, falha com erro acionável, nunca
  trunca), TTL ≤ 24 h, um export pendente por Account, `no-store`, rate limit,
  removido junto com a Account. ⚠️ Pendente: o teto não pode bloquear um
  export legítimo (nunca vira Premium e antecede a exclusão); o remédio
  funcional para `size_cap` e o texto são do `/analyst` e do usuário.
- **DA-249 — Reautenticação é a senha no corpo do pedido de exclusão**
  (consenso). Esclarece a DA-082: "recente" é satisfeito pela prova dentro da
  própria requisição, mais estrita que ≤5 min, sem claim nem estado. Kind novo
  no `CredentialAttemptLimiter`, erro genérico, endpoint só para principal
  Human por cookie, com CSRF. Gatilho de revisão: login sem senha.
- **DA-250 — E-mail é aviso de conclusão, enviado depois do commit**
  (consenso). Lê o endereço antes da transação, envia depois (DB-040), texto
  "foi excluída", falha só em log sem o endereço (GEN-043) e sem fila de
  retry que guarde o endereço. "Antes de apagar o endereço" da DA-082 significa
  "enquanto ainda o temos".
- **DA-251 — Confirmação digitada é o e-mail da Account** (consenso).
  Conferida no servidor, normalizada (trim, case-insensitive); divergência
  responde 422 com código de catálogo (DA-101).
- **DA-252 — Purge síncrono numa única transação** (consenso). Participantes,
  usuário do Identity, Account e ledger na mesma transação; o acesso morre
  atomicamente com o commit; sem prazo a declarar. Exceção a BE-042 (job para
  trabalho longo), justificada pela escala de uma Account no MVP e por dados só
  de dev até o E14. Teste com Account grande semeada mede a duração. Passar
  para job (marca "Account em exclusão" + `accounts.purge`) quando a medição
  pedir.
- **DA-253 — Formato do ledger** (consenso; confirmar com o desenho de restore
  do E14, porque o formato é irreversível depois que os IDs somem).
  `SHA-256(kind || id)` com separação de domínio; `kind`, `purged_at` e
  `hash_version` em claro (`purged_at` fora do hash, para o E14 conseguir
  recomputar sobre IDs restaurados). Sem HMAC (a chave teria de sobreviver aos
  backups). Tabela sem `account_id` e sem RLS, papel da app só com INSERT,
  exceção a BE-022 no estilo da DA-098 e entrada na lista de exceções do teste
  de catálogo de RLS. O recibo mostrado ao usuário é uma referência aleatória
  gravada na linha do ledger (não o hash do ID da Account) e data, na tela
  final e no e-mail, sem PII.
- **DA-254 — Purge de Node grava no ledger** (consenso). `GraphPurge.RunAsync`
  grava uma linha por Node purgado na mesma transação; a exclusão de conta
  grava uma linha da Account, não uma por Node. Relations purgadas sozinhas
  ficam fora (a DA-084 não as nomeia; as removidas com o Node cobertas pela
  linha dele).
- **DA-255 — Interface do frontend da conta** (consenso). Rota `/conta`, só
  principal Human, com guard de UX. `features/account/accountData.ts` no
  padrão de `sharesData.ts`: `accountKeys`, `exportStatusOptions()`
  (`none | pending | ready | failed{size_cap|internal} | expired`, polling que
  para nos estados terminais), `useStartExport()`, `useDownloadExport()` (blob
  pelo cliente tipado único, por causa de CSRF), `useDeleteAccount()` (limpa a
  sessão e redireciona **só no sucesso**, nunca em erro). `AccountPage()` sem
  props; `DeleteAccountDialog({open, onOpenChange, email, onExportFirst?})` novo
  componente sobre `Dialog`, com formulário próprio (senha só em estado
  não controlado, limpa ao fechar), erros 422/429/5xx em `role=alert` com
  mensagens de um mapa local, submit desabilitado enquanto pendente. O login lê
  `notice` e `receipt` do estado do roteador uma vez, sem persistir (perder no
  reload é aceitável: o recibo também vai por e-mail). Com export `ready`, o
  "Exportar antes" oferece o download. `size_cap` mostra texto acionável sem
  retry cego (ver DA-248).
- **DA-256 — O reader do export lê em lotes por keyset, sem exceção a DB-023**
  (consenso após contra-argumento). Lotes de tamanho fixo ordenados por id,
  dentro da mesma transação de snapshot (nunca uma transação por lote), via
  `InAccountTransactionAsync`, alimentando o builder em streaming. O teto de
  50 MB é o único limite funcional: não há limite de linhas por tabela (seria
  arbitrário e poderia bloquear um usuário legítimo com muitos Nodes pequenos).
  Um único contador de orçamento de bytes, cobrado pelo reader a cada lote,
  garante que o erro `size_cap` seja o mesmo na leitura e na montagem, nomeie
  o tipo de dado que estourou o teto e diga o que fazer; o job termina como
  `failed`, libera o snapshot e nunca entrega nem persiste arquivo parcial. O
  builder é a autoridade sobre o tamanho do zip final; a estimativa do reader
  é só um limite de falha antecipada.

## Checklist

- [x] Export JSON completo (Nodes, Relations, Types, definições, Tags, Collections, Resources, ChangeSets, Provenance), mais Markdown
- [x] Fluxo de exclusão de conta: oferta de export, reautenticação, confirmação digitada, e-mail
- [x] Revogação síncrona e purge por `account_id`
- [x] Recibo/ledger sem PII na mesma transação
- [x] **Teste guiado pelo schema:** toda tabela com `account_id` é coberta pelo purge da conta
- [x] Texto de aviso sobre dados já enviados a agentes externos

## Critérios de saída

Depois de excluir uma conta, nenhuma linha com aquele `account_id` sobra (teste
automatizado), e o export de outra conta inclui todos os tipos de dado.

## Fora de escopo

Reaplicar o ledger após restaurar um backup (E14), importação.
