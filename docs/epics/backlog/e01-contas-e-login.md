# E1 — Contas e login humano

- **Ordem:** 1
- **Depende de:** E0
- **Última revisão:** 2026-10-02

## Contexto

Tudo no Life Graph pertence a uma **Account** (ver `GLOSSARY.md`). O login
humano vem antes do núcleo de escrita, para que a regra de ownership (BOLA)
valha desde o primeiro endpoint que escreve. O mesmo servidor que autentica o
humano será o emissor OAuth dos agentes no E3.

## Decisões

- **DA-009 — OpenIddict + ASP.NET Core Identity no mesmo processo, com um
  único emissor** para login humano e OAuth de agentes (consenso). Custo zero, roda em dev, e os dados de identidade não saem do nosso
  banco. Um IdP gerenciado (WorkOS/Auth0) guardaria e-mail e eventos de login
  fora do Canadá, o que exigiria PIA (Lei 25, art. 17), contrato e o tornaria
  um suboperador. A contrapartida é que somos donos da superfície de segurança.
  ADR: [docs/adr/0006-openiddict-emissor-unico-no-processo.md](../../adr/0006-openiddict-emissor-unico-no-processo.md)
- **DA-010 — SPA autenticada por cookie BFF HttpOnly** (consenso). Nenhum
  token fica no browser.
- **DA-011 — Cadastro aberto desligado no MVP** (consenso). Enquanto o servidor
  estiver exposto pelo túnel de dev (E3), qualquer pessoa que achasse o hostname
  poderia criar conta na máquina do usuário. Novas contas só por convite/owner
  até o E14.
- **DA-012 — Gancho de exclusão de conta já previsto** (consenso). O e-mail é
  dado pessoal. A exclusão completa vem no E11, mas o modelo de Account já nasce
  com o ponto de extensão do purge.

## Checklist

- [ ] Identity (e-mail + senha) com e-mail de confirmação via Mailpit
- [ ] Provisionamento de Account ao criar usuário (1 usuário = 1 Account no MVP)
- [ ] OpenIddict configurado como emissor (sem clientes de agente ainda)
- [ ] Cookie BFF HttpOnly, SameSite e proteção CSRF para a SPA
- [ ] `ICurrentPrincipal` sobre `IUserIdentityGateway`: account_id + tipo (Human | AgentIdentity)
- [ ] Rate limiting em login e recuperação de senha
- [ ] Cadastro aberto desligado; criação de conta por convite/owner
- [ ] Tela de login/logout na SPA
- [ ] Testes: login, sessão, isolamento entre duas contas

## Critérios de saída

- Dois usuários em contas diferentes não enxergam nada um do outro (teste automatizado).
- O emissor OpenIddict está pronto para receber clientes no E3.

## Fora de escopo

Clientes OAuth de agentes (E3), exclusão de conta (E11), e-mail real (E13),
passkeys.
