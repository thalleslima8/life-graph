# E18 — Denúncia de share por formulário

- **Ordem:** 18 (pós-MVP, depois do primeiro deploy)
- **Depende de:** E11, E14, E15
- **Última revisão:** 2026-10-30

## Contexto

Hoje o "Denunciar esta página" do link compartilhado é um `mailto:` (DA-218,
E11). Ele depende de o visitante ter um cliente de e-mail configurado, expõe o
endereço da caixa do operador no bundle público e não tem rate limit nem
deduplicação. Este épico o troca por um formulário próprio, anônimo, numa rota
não listada, que entrega a denúncia por e-mail a uma caixa pré-definida
(`Sharing:AbuseReportAddress`).

Só faz sentido depois do E14 (SMTP real, WAF e domínio público) e do E15
(política de privacidade, suboperadores e processo de operador). Até lá o
`mailto:` da DA-218 continua valendo. Não há efeito automático sobre o share:
só o operador age, pelo kill-switch (DA-217).

## Decisões

- **DA-241 — O formulário substitui o `mailto:` e supersede a DA-218; fica num
  épico próprio, depois do primeiro deploy** (usuário).
  - Por quê: o gatilho de reavaliação da DA-218 era volume, e hoje não há
    usuários nem SMTP real. O usuário decidiu postergar o formulário para o fim
    do roadmap, onde o risco que a DA-218 evitava (escrita pública anônima) já
    tem WAF, e-mail real e política publicada. O arquiteto e o analista
    recomendaram manter o `mailto:` até o E14.
- **DA-242 — A denúncia entra na fila Postgres e um worker envia o e-mail; o
  payload é apagado após o envio, ou em até 72 h se a entrega falhar**
  (consenso; prazo ajustado pelo subsídio de LGPD).
  - O POST responde 202 sem depender do SMTP. Retry no worker. Não existe
    entidade `AbuseReport` com status nem triagem: sem painel de operador, o
    status não teria consumidor.
  - O registro duradouro guarda só `public_ref`, motivo e data, por 90 dias
    com purge automático testado.
  - Por quê: SMTP direto no request deixa o endpoint anônimo refém da latência
    e permite amplificação; "entregar e descartar" sem estado perde a denúncia
    quando o SMTP falha. O texto livre é a parte de maior risco de dado pessoal
    e por isso vive o mínimo.
- **DA-243 — Formato: motivo em enum obrigatório e texto livre opcional de até
  500 caracteres; sem e-mail do denunciante** (consenso).
  - A tela diz que a denúncia é anônima e não gera resposta individual, e
    avisa para não incluir dados pessoais, de saúde ou íntimos, nem de
    terceiros. Texto é só texto puro: sem anexo, sem URL renderizada, sem
    interpolar em cabeçalho de e-mail, e nunca em log (GEN-043).
  - Por quê: sem fluxo de resposta o e-mail seria dado pessoal sem finalidade
    (minimização), e abriria o formulário para enviar e-mail a terceiros.
    Reabrir só se o E15 definir um processo de operador que responda.
- **DA-244 — Dedup e rate limit usam um hash do IP com sal rotativo; o hash é
  apagado junto com o sal, em até 7 dias** (usuário: seguir o arquiteto;
  prazo do hash ajustado pelo subsídio de LGPD).
  - Dedup por `public_ref` + motivo + janela, que o operador recebe como
    contagem. O IP bruto nunca vai a log nem a linha persistida.
  - Por quê: enquanto o sal existe o hash é dado pseudonimizado, e um hash de
    90 dias com sal rotativo se contradiz. Os metadados de 90 dias (DA-242)
    ficam sem o hash.
- **DA-245 — Rota não listada no bundle de visitante, POST sem cookie nem CSRF,
  e resposta uniforme** (consenso).
  - Rota `/denunciar/:publicRef` com `noindex` e `Referrer-Policy`, com fetch
    mínimo próprio: a regra do bundle (DA-216) continua, sem importar
    `api/client`, csrf ou auth. O formulário traz só o `public_ref`, visível e
    somente leitura; nunca token, título nem conteúdo de Node.
  - O backend ignora tudo além de enum, tamanhos e `public_ref`. Valida
    `Content-Type` JSON e `Origin` da origem do visitante; a CSP libera
    `connect-src` para o endpoint e `form-action 'none'`. `public_ref`
    inexistente ou share revogado devolve o mesmo 202, no mesmo tempo.
  - Anti-abuso: rate limit por IP e por `public_ref` (reaproveita o do
    visitante), corpo pequeno, honeypot, sem CAPTCHA de terceiro. Nenhuma
    quantidade de denúncias revoga ou bloqueia um share.
  - Por quê: o endpoint não usa credencial ambiente, então CSRF não se aplica, e
    um limiar automático daria a qualquer pessoa um jeito de derrubar um share
    legítimo.
- **DA-246 — Entrega por porta própria do Sharing, e a denúncia fica fora do
  pipeline único de escrita** (consenso).
  - O Sharing define a própria porta de notificação (não reaproveita o
    `IAccountMailer`) e o transporte SMTP genérico é extraído para a
    Infrastructure, sem dependência entre módulos.
  - A denúncia não é escrita no grafo, então GraphChangeSet e Provenance (DA-013)
    não se aplicam. A tabela da fila é do Sharing e a escrita anônima passa por
    um caminho controlado com policy própria; citar a regra de DB exata ao
    implementar.
  - Por quê: um módulo só depende de `<Outro>.Contracts`, e declarar a exceção
    evita que pareça violação da DA-013.

## Decisões pendentes

Ligadas à validação jurídica humana (E15); não têm DA até alguém decidir.

- **Canal identificado ao lado do anônimo.** O subsídio de LGPD aponta que uma
  denúncia anônima enfraquece a notificação de nudez ou ato sexual sem
  consentimento (Marco Civil, art. 21), que exige vítima identificável. Manter
  um `mailto:` ou formulário identificado em paralelo precisa de decisão.
- **Notificar o dono do share.** Recomendação: nunca a cada denúncia; só quando
  o operador agir, com categoria, data e canal de contestação, sem dado da
  denúncia. Não decidido.
- **Preservação de prova.** Depois do kill-switch o dono ainda pode fazer Purge;
  avaliar retenção legal (legal hold) para conteúdo grave.
- **Retenção da caixa de e-mail.** Apagar o payload no Postgres não apaga a
  cópia no provedor SMTP nem na caixa do operador. Definir esse prazo, ou
  enviar só `public_ref` e motivo e manter o texto num lugar com TTL.

## Checklist

### Legal ⚖️ (requer validação jurídica humana)
- [ ] Advogado confirma se LGPD e Marco Civil se aplicam a operador hospedado no Canadá, e se é preciso representante no Brasil
- [ ] Base legal do texto livre que possa conter dado sensível (LGPD art. 11) validada
- [ ] LIA (legítimo interesse) escrita e arquivada
- [ ] Política de privacidade cobre a denúncia: categorias, finalidade, base legal, prazos, suboperador de e-mail, transferência, canal do titular
- [ ] Art. 15 do Marco Civil e o conflito com a DA-078 (sem IP vinculado ao link) avaliados
- [ ] Dispensa do encarregado (Res. CD/ANPD 2/2022) avaliada diante de tratamento de alto risco
- [ ] Runbook de abuso infantil/NCII, com prazo de resposta e preservação de prova
- [ ] Linha do suboperador de e-mail no registro de suboperadores e fluxos de dados, com cláusulas-padrão da ANPD (Res. 19/2024) se o provedor for estrangeiro

### Backend
- [ ] Tabela e fila de denúncias no Sharing, com policy própria e purge automático de 90 dias dos metadados
- [ ] Endpoint anônimo `POST` de denúncia: enum, tamanhos, `Content-Type`, `Origin`, 202 uniforme em conteúdo e tempo
- [ ] Hash de IP com sal rotativo (≤ 7 dias), apagado junto com o sal; IP bruto fora de log e de linha
- [ ] Dedup por `public_ref` + motivo + janela e rate limit por IP e por `public_ref`
- [ ] Porta de notificação própria no Sharing; transporte SMTP genérico extraído para a Infrastructure
- [ ] Worker com retry; payload apagado após o envio ou em 72 h
- [ ] Corpo do e-mail em texto puro, sem texto do denunciante em cabeçalho
- [ ] Remoção do `mailto:` e do endereço da caixa do bundle público

### Frontend
- [ ] Rota `/denunciar/:publicRef` no bundle de visitante, `noindex`, `Referrer-Policy` e CSP ajustada
- [ ] Formulário com `public_ref` visível e somente leitura, motivo, texto (contador de 500), honeypot
- [ ] Aviso de anonimato, de proibição de dados sensíveis ou de terceiros e link para a política
- [ ] Estados de envio, sucesso e erro acessíveis (FE de a11y)
- [ ] `ReportAbuseLink` leva à rota em vez de abrir o `mailto:`

### Testes obrigatórios
- [ ] Unitários: validação do enum e dos tamanhos, deduplicação, janela do hash
- [ ] Integração em Postgres real: RLS e caminho anônimo, rate limit, retenção e purge de 90 dias, worker com retry e TTL de 72 h
- [ ] 202 idêntico para `public_ref` inexistente, revogado e válido
- [ ] Texto e IP bruto ausentes de log (GEN-043)
- [ ] Teste de arquitetura: Sharing não depende de Accounts
- [ ] Guarda do bundle de visitante continua verde (sem `api/client`, csrf ou auth)
- [ ] axe e E2E de fumaça do formulário

## Critérios de saída

Um visitante anônimo envia uma denúncia pela rota não listada, o operador a
recebe na caixa pré-definida com `public_ref` e motivo, nada do texto sobrevive
além do prazo, e nenhuma denúncia altera o share sozinha. As decisões pendentes
têm resposta registrada e a validação jurídica está feita.

## Fora de escopo

Painel de operador, status e triagem de denúncias. Resposta ao denunciante.
Anexos. Notificação automática ao dono. CAPTCHA de terceiro. Efeito automático
por quantidade de denúncias.
