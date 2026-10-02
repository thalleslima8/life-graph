# E13 — Infra & Deploy

- **Ordem:** 13
- **Depende de:** E0–E12
- **Última revisão:** 2026-10-02

## Contexto

Por decisão do usuário, tudo roda em ambiente de desenvolvimento até aqui
(DA-006). Este épico escolhe e monta o ambiente real: hospedagem no Canadá,
banco, backups, domínios e observabilidade. Vem antes da prontidão para beta,
que ensaia o ambiente montado aqui.

## Decisões

- **DA-088 — Infra no fim do roadmap, separada da prontidão para beta**
  (usuário + consenso). Infra constrói; o E14 ensaia e valida.
- **DA-089 — Requisitos do host já fixados** (consenso, ver DA-006): Postgres
  ≥16, pgvector ≥0.7, RLS com papéis sem superusuário e sem BYPASSRLS, e
  pgvector disponível na região escolhida. Manter a stack portável (Postgres
  puro + RLS + pgvector, nada exclusivo de um provedor no domínio).

## Insumos da discussão (preços e regiões verificados em 2026-10; reverificar)

- O usuário escolheu **Canadá** e quer free tier até onde der, aceitando até
  **US$ 20/mês**.
- **Lei 25 (Quebec):**
  - hospedar fora de Quebec (ex.: Toronto) exige PIA (art. 17);
  - Montreal (AWS ca-central-1) ou Azure Canada East evitam isso **para o
    núcleo**;
  - suboperadores dos EUA (embeddings, e-mail, rastreio de erros) exigem PIA
    de qualquer forma;
  - pendente: o usuário mora em Quebec, e o beta terá pessoas de Quebec?
- **Fly.io** não tem mais Montreal (yul foi descontinuado). **Neon**, **Render**
  e **Railway** não têm região no Canadá.
- **Supabase** ca-central-1 (Montreal): o Free pausa após 1 semana sem uso e
  não tem backup; o Pro custa US$ 25+.
- Opções levantadas:
  - (A) para uso solo: Supabase Free + Lightsail US$ 5 + `pg_dump` próprio,
    ~US$ 5/mês;
  - (B) banco gerenciado: RDS t4g.micro + Lightsail, ~US$ 19–20/mês, sem folga
    (preferido pelos dois especialistas);
  - (C) Postgres autogerido numa VM Lightsail, ~US$ 10–12/mês, só com
    condições: banco nunca exposto, volume e backups criptografados,
    atualizações automáticas, teste de restauração bem-sucedido, registro de
    incidentes e PIA;
  - (D) Azure Canada East, ~US$ 28 depois do período grátis.

## Pendências (decisão do usuário)

- [ ] Onde o usuário mora e se o beta inclui pessoas de Quebec
- [ ] Banco gerenciado (~US$ 20) ou autogerido (~US$ 10–12, com risco operacional aceito explicitamente)
- [ ] Provedor de e-mail transacional (suboperador)

## Checklist

- [ ] Hospedagem do app e do banco na região escolhida
- [ ] Backups automáticos criptografados + janela de retenção documentada
- [ ] Reaplicar o ledger de purge (DA-084) após restauração + teste de restauração
- [ ] Secrets em um cofre (ex.: SSM Parameter Store), nunca em arquivos versionados
- [ ] Domínios/TLS: app, MCP/OAuth (emissor de produção) e origem dos shares
- [ ] Reconectar os agentes ao emissor de produção (o emissor do túnel de dev muda)
- [ ] Exporters de observabilidade + alertas
- [ ] E-mail real substituindo o Mailpit
- [ ] WAF/rate limit na borda
- [ ] Atualizar o registro de suboperadores e fluxos de dados
