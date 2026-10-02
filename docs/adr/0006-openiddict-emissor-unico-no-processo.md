---
status: accepted
---

# OpenIddict + ASP.NET Core Identity no processo, um emissor para humanos e agentes

O login humano e o servidor OAuth 2.1 dos agentes (MCP) são o mesmo emissor:
OpenIddict + Identity rodando dentro do host. O custo é zero, funciona em dev, e
os dados de identidade ficam no nosso banco. Como OpenIddict não traz CIMD
nativo, ele é implementado por nós como handler próprio. DCR não é suportado.

## Considered Options

- **WorkOS AuthKit:** tem CIMD/DCR prontos e é grátis até 1M MAU, mas é
  suboperador nos EUA (PIA pela Lei 25) e traz lock-in no componente mais
  central. Fica como fallback se o spike de CIMD falhar, por decisão do
  usuário.
- **Auth0 / Entra External ID:** mesmas questões de residência e custo; o Entra
  ainda exige domínio verificado.

## Consequences

Somos donos da superfície de segurança do servidor de autorização. Rate limits,
o guard de SSRF no fetch de CIMD e uma revisão de segurança antes do beta são
obrigatórios.

Origem: DA-009 (`docs/epics/backlog/e01-contas-e-login.md`), DA-029 e DA-034
(`docs/epics/backlog/e03-conexao-de-agentes.md`).
