# E3 — Conexão de agentes (OAuth 2.1 + MCP walking skeleton)

- **Ordem:** 3
- **Depende de:** E1, E2
- **Última revisão:** 2026-10-02

## Contexto

O fluxo central do MVP (v2 §63) começa com "usuário conecta ChatGPT/Claude".
Os conectores do ChatGPT e do Claude.ai só falam com MCP remoto via OAuth, e
fazem essas chamadas **da nuvem deles**. Este épico entrega a conexão de ponta
a ponta com uma única ferramenta de leitura, isolando o risco de autenticação
e de compatibilidade de clientes antes das ferramentas de verdade (E4/E5).

## Decisões

- **DA-025 — OAuth 2.1 desde o primeiro slice de agente** (usuário). O usuário
  reverteu o "PAT primeiro" para validar a §63 com ChatGPT/Claude.ai
  diretamente. Um PAT validaria só clientes de desenvolvedor.
- **DA-026 — Sem PATs no MVP** (consenso). O Claude Code também usa OAuth
  (CIMD com redirect em loopback). Cada credencial a mais é mais uma superfície
  para listar, revogar, purgar e mapear. PATs ficam registrados como futuro.
- **DA-027 — Exposição de dev por Cloudflare named tunnel, num domínio do
  usuário** (consenso, ADR: [docs/adr/0007-tunel-de-dev-em-vez-de-staging.md](../../adr/0007-tunel-de-dev-em-vez-de-staging.md)). Isso é ferramenta de dev, não
  deploy: não contradiz DA-006. O túnel é grátis; o domínio custa ~US$ 10–15
  por ano e será usado pelo produto de qualquer forma. Alternativas
  rejeitadas:
  - Quick Tunnel: URL aleatória e sem SSE.
  - ngrok free: interstitial e cotas.
  - Dev Tunnels: URL muda.
  - Staging antecipado: contradiz o usuário.
- **DA-028 — Proteções obrigatórias do túnel** (consenso):
  - ingress por caminho expõe só `/mcp`, `/.well-known/*`, os endpoints OAuth e
    as páginas de login/consentimento; todo o resto dá 404;
  - autenticação em toda rota exposta, exceto discovery;
  - túnel só ligado durante sessões de aceite;
  - banco `dev-public` com apenas os dados do usuário;
  - chaves de assinatura de dev persistentes e fora do repo;
  - token do túnel em `.env` não versionado;
  - opcional: Cloudflare Access nas rotas de navegador (nunca em token ou
    discovery).
- **DA-029 — Clientes:** o slice 1 usa um **cliente pré-registrado**. O
  slice 2 implementa **CIMD** (Client ID Metadata Documents) como handler
  próprio sobre o OpenIddict. **Sem DCR** (consenso).
  - O OpenIddict não tem DCR nem CIMD nativos.
  - A DCR é MAY na spec 2025-11-25 e abre registro ilimitado de clientes.
  - O CIMD reusa o mesmo guard de SSRF do E7, exige que o `client_id` seja
    exatamente a URL, faz match exato de redirect (loopback independente de
    porta) e anuncia `client_id_metadata_document_supported`, `none` em
    `token_endpoint_auth_methods_supported` e S256.
  ADR: [docs/adr/0006-openiddict-emissor-unico-no-processo.md](../../adr/0006-openiddict-emissor-unico-no-processo.md)
- **DA-030 — AgentIdentity = a concessão OAuth do usuário** (consenso). O
  usuário pode renomeá-la e revogá-la. O nome e o provedor do cliente servem só
  para exibição e não são confiáveis. Reautorizar a mesma URL de CIMD reaproveita
  a identidade. As regras de E5 (mesmo agente, toggle, janela) dependem desta
  definição.
- **DA-031 — Tokens:** access tokens com audience fixada na URI canônica do MCP
  (RFC 8707, `resource`), JWT de vida curta, refresh rotativo para clientes
  públicos, `invalid_grant` em refresh morto e sem token passthrough
  (consenso). Revogar mata os refresh tokens na hora.
- **DA-032 — Adapter MCP no mesmo processo, chamando os casos de uso
  diretamente**, não a API por loopback HTTP (consenso, ADR: [docs/adr/0008-adapter-mcp-no-processo.md](../../adr/0008-adapter-mcp-no-processo.md),
  diverge do diagrama da §31). O domínio continua sem depender do MCP. Usa o
  SDK oficial `ModelContextProtocol.AspNetCore` (v1.0, spec 2025-11-25).
- **DA-033 — Tokens de teste pelo fluxo real** (consenso). Os testes de "mesmo
  principal" e os de contrato MCP obtêm tokens pelo fluxo authorization code +
  PKCE dentro do processo (WebApplicationFactory), com consentimento automático
  **apenas no ambiente Testing**. A app se recusa a subir se essa opção estiver
  ligada em outro ambiente, e um teste prova que fora do Testing o
  consentimento é exigido. Nenhum grant type de teste existe no servidor.
- **DA-034 — Fallback para WorkOS AuthKit** se o spike de CIMD/audience falhar
  no slice 1. **Decisão do usuário** na hora, porque implica PIA e um
  suboperador fora do Canadá.

## Pré-requisitos do usuário

- [ ] Comprar um domínio e apontar o DNS para o Cloudflare
- [ ] Confirmar que os planos de ChatGPT (modo desenvolvedor) e Claude.ai permitem conectores MCP personalizados com ferramentas de escrita

## Checklist

### Slice 1 — Cliente pré-registrado
- [ ] Spike: OpenIddict + `resource`/audience + Protected Resource Metadata contra Claude.ai e ChatGPT
- [ ] Protected Resource Metadata, 401 com `resource_metadata`, discovery do servidor de autorização
- [ ] Tela de consentimento (Razor no servidor): hostname do redirect, scopes em linguagem simples, aviso de que o agente poderá alterar o grafo (com Undo), aviso para cliente só-loopback
- [ ] AgentIdentity por concessão + tela "Agentes conectados" (último uso, renomear, revogar)
- [ ] Endpoint MCP com uma ferramenta Safe (`whoami`/`ping`)
- [ ] Perfil `public` do compose com cloudflared + runbook do túnel
- [ ] Rate limits em `/authorize`, `/token` e login
- [ ] Aceite manual: conectar Claude.ai e ChatGPT e chamar a ferramenta

### Slice 2 — CIMD
- [ ] Handler de CIMD com fetch protegido contra SSRF, cache, match exato de redirect e loopback
- [ ] Metadata anunciando suporte a CIMD
- [ ] Aceite manual: Claude Code via CIMD

### Testes obrigatórios
- [ ] Fluxo OAuth + PKCE completo dentro do processo
- [ ] Token com audience errada é rejeitado
- [ ] Revogação invalida o refresh imediatamente
- [ ] O switch de auto-consent só funciona no ambiente Testing

## Critérios de saída

Claude.ai e ChatGPT conectam pelo túnel, passam pelo consentimento e chamam a
ferramenta MCP. O usuário vê e revoga a conexão.

## Fora de escopo

Ferramentas de leitura e escrita do grafo (E4/E5), PATs, DCR, deploy (E13).
