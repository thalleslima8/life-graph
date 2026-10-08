# E3 — Conexão de agentes (OAuth 2.1 + MCP walking skeleton)

- **Ordem:** 3
- **Depende de:** E1, E2
- **Última revisão:** 2026-10-07

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

- **DA-119 — As tabelas `oidc_*` do emissor ficam no diretório de credenciais,
  sem RLS, e a AgentIdentity fica numa tabela de Account com RLS**
  (consenso, Q19, no `/flow` de 2026-10-04; o E1 deixou essa decisão para o E3). O OpenIddict lê `oidc_applications`,
  `oidc_authorizations` e `oidc_tokens` antes de existir uma Account em
  contexto: no endpoint de token, na validação de cada token e no registro de
  cliente CIMD. Uma policy por `account_id` deixaria esses caminhos sem linhas,
  e `OR current_account_id() IS NULL` falha aberta (o mesmo motivo da DA-098).
  As condições da DA-098 passam a valer para elas: só o módulo Accounts
  referencia OpenIddict (teste de arquitetura existente), nenhuma resposta
  revela grant de outra Account e o purge da Account apaga os grants e tokens
  do usuário (`AgentIdentitiesAccountPurge`). O dado de produto da conexão
  (nome, cliente, scopes, último uso, revogação) fica em `agent_identities`,
  com `account_id`, FK e a policy de isolamento, e toda leitura dela passa pela
  RLS. Revogar muda a AgentIdentity e o grant na mesma transação.
  Condições (consenso, Q19): o purge apaga só as authorizations e os tokens
  do usuário, nunca a linha de `oidc_applications` (clientes são globais); o
  teste de arquitetura "só o Accounts referencia OpenIddict" cobre também SQL
  cru sobre `oidc_*`; nenhum endpoint lista authorizations por outra coisa que
  não o subject do principal atual; um teste prova que a falha de um
  participante posterior do purge desfaz também a remoção dos grants.
- **DA-120 — Login e consentimento renderizados no servidor, na origem do
  emissor** (consenso, Q20). O pedido de autorização chega de
  outro site (claude.ai, chatgpt.com), e o cookie de sessão `SameSite=Strict`
  (DA-010) não viaja nessa navegação. Por isso o emissor tem a própria página
  de login (`/connect/login`, Razor, com antiforgery e os mesmos limites do
  login da SPA) e volta só para `/connect/authorize` (sem open redirect). O
  custo é a pessoa digitar a senha a cada conexão nova, mesmo logada na SPA. Se
  o POST do login já chega com sessão (o cookie viaja porque o POST é do
  próprio site), a pessoa só continua. As páginas mandam `X-Frame-Options:
  DENY`, CSP `frame-ancestors 'none'` e `no-store`. O SameSite da sessão não
  muda.
  Condições (consenso, Q20): `/connect/login` consome o mesmo orçamento por
  e-mail do `CredentialAttemptLimiter` do login da SPA; nenhum `login_hint` ou
  preenchimento vem de parâmetros do cliente; o retorno é validado como caminho
  local igual a `/connect/authorize`, com testes para `//evil`, `/\evil` e
  variantes codificadas; a página mostra claramente o domínio do emissor e
  oferece "esqueci a senha". O aviso "o agente poderá alterar o grafo (com
  Undo)" aparece só quando `lifegraph.write` é concedido; toda escrita passa
  pelo pipeline com ChangeSet (DA-013). Se o atrito incomodar, o caminho
  reversível é um segundo cookie `SameSite=Lax` restrito a `Path=/connect`,
  sem tocar na DA-010.
- **DA-121 — A AgentIdentity mora no módulo Accounts; o adapter MCP mora no
  Agents e depende só de `LifeGraph.Accounts.Contracts`** (consenso, Q21). A AgentIdentity é a concessão OAuth (DA-030), e o OpenIddict
  só pode ser tocado pelo Accounts (DA-098). Com ela no Agents, consentir
  (Accounts → Agents) e revogar (Agents → Accounts) formariam um ciclo de
  projetos. O Agents usa `AgentAccess` (caminho do MCP, esquema do token,
  scopes), `IAgentIssuer` (emissor e recurso canônico) e `IAgentIdentities`
  (último uso e consulta). O token de agente autentica só no `/mcp`, por um
  esquema próprio que valida o token do OpenIddict (assinatura, validade,
  audience = MCP) e exige a AgentIdentity ativa; uma conexão revogada responde
  401, não 403. O cookie da pessoa nunca abre o `/mcp`, e o token do agente
  nunca abre o `/api` (DA-109). O MCP usa o SDK `ModelContextProtocol.AspNetCore`
  na linha 1.x (1.4.1, a DA-032 fixou a 1.0) em modo stateless. O
  `AuthenticatedPrincipal` ganha `AgentIdentityId`, e a cota do DA-116 de um
  agente passa a ser por AgentIdentity, não pela Account.
  Condições (consenso, Q21): a verificação de AgentIdentity ativa roda em toda
  requisição ao `/mcp`, sem cache (revogação imediata, DA-031); a conexão
  revogada responde 401 com `WWW-Authenticate: Bearer error="invalid_token"`.
  Além da cota por AgentIdentity, há um teto agregado por Account, para que
  várias conexões não multipliquem a cota. Basta registrar a linha 1.x do SDK;
  a 2.x só com tarefa explícita de upgrade. O modo stateless abre mão de
  notificações do servidor e de sampling, e deve ser revisto antes de qualquer
  funcionalidade que precise de push. `GLOSSARY.md` e a lista de módulos do
  CLAUDE.md passam a dizer que o Agents tem o adapter, não a identidade.
- **DA-122 — Scopes concedidos e acesso offline** (consenso, Q22, revisada abaixo). O consentimento concede os scopes pedidos que o produto
  conhece (`lifegraph.read`, `lifegraph.write`); se o cliente não pede nenhum
  deles, concede os dois. Scopes desconhecidos (`openid`, `profile`) são
  ignorados, e não derrubam a conexão de um cliente real. `offline_access` vai
  sempre junto, porque o refresh rotativo é parte da DA-031. A tela mostra
  exatamente o que será concedido.
  **Revisão (consenso, Q22, depois de um contra-argumento):** a tela de
  consentimento lista leitura e escrita como itens separados. Leitura é
  obrigatória; escrita vem marcada e pode ser desmarcada. O que é oferecido são
  os scopes conhecidos pedidos pelo cliente, ou {read, write} se ele não pediu
  nenhum; o servidor valida que o conjunto postado cabe no oferecido, sempre
  inclui read e nunca aceita um scope que não foi oferecido (antiforgery, com
  teste). A concessão é exatamente o que foi marcado. `scopes_supported` no
  Protected Resource Metadata é exatamente `lifegraph.read lifegraph.write`;
  `.resources`, `.calendar`, `.share` e `.delete` ainda não existem, são
  descartados e nunca emitidos nem mostrados. O `scope` do token contém só o
  concedido. Reautorizar o mesmo cliente **substitui** os scopes (não une):
  atualiza a AgentIdentity e revoga a authorization anterior e seus refresh
  tokens na mesma transação (teste: consentir com write, reconsentir sem, o
  refresh antigo é recusado e uma ferramenta de escrita responde 403
  `insufficient_scope`). Sem write, ferramentas de escrita respondem 403 com
  `WWW-Authenticate: Bearer error="insufficient_scope", scope="lifegraph.write"`
  (step-up da spec). `offline_access` continua sempre concedido, com uma linha
  em linguagem simples ("fica conectado até você revogar").
- **DA-123 — Superfície do túnel também no host** (consenso, Q23; complementa a DA-028). O emissor é configurado
  (`Accounts:Issuer:Issuer`), nunca inferido do `Host`, e dele saem o recurso
  canônico `/mcp` e a URL do Protected Resource Metadata. Sob o hostname do
  túnel (`Host:PublicExposure:Hosts`), o host só responde `/mcp`,
  `/.well-known/*` e `/connect/*`; o resto dá 404, mesmo que as regras de
  ingress do túnel falhem. `X-Forwarded-For`/`-Proto` só valem vindos da rede
  do `cloudflared` (`Host:PublicExposure:KnownNetworks`). Os limites de
  `/connect/authorize`, `/connect/token` e `/connect/login` são por cliente e
  rodam antes da autenticação, onde o OpenIddict responde. As categorias do
  OpenIddict nunca logam abaixo de Warning, porque a partir de Information ele
  registra o `code_verifier` e o redirect em claro (GEN-043; há teste com tudo
  em Trace). Chaves persistentes ficam num diretório fora do repo
  (`Accounts:Issuer:KeysDirectory`, permissão 600). Clientes pré-registrados
  vêm da configuração e são sincronizados na partida. Um cliente que sai da
  configuração não é apagado: suas conexões se revogam em Agentes conectados.

  Condições (consenso, Q23): um cliente pré-registrado que sai da configuração
  fica **descontinuado**: `/connect/authorize`, `/connect/token` e o `/mcp`
  recusam seus pedidos e tokens; o registro fica (a proveniência precisa da
  AgentIdentity) e aparece como "Cliente descontinuado" em Agentes conectados,
  onde pode ser revogado. O limite por cliente é por IP real
  (`CF-Connecting-IP`/`X-Forwarded-For` só da rede do `cloudflared`), com teste
  de que um cabeçalho forjado de fora de `KnownNetworks` não muda a chave; a
  chave e os contadores em memória precisam ser revistos antes do E13
  (multiusuário público, IPs de saída compartilhados de Anthropic/OpenAI). Além
  das categorias do OpenIddict, nenhum log de HTTP ou de caminho registra a
  query string de `/connect/*` (`code`, `state`, `code_verifier`, redirect), e o
  teste em Trace cobre o fluxo `/connect` inteiro. A varredura de segredos
  cobre o padrão de caminho das chaves. A DA-116 passa a citar esta DA para os
  limites anteriores a qualquer principal.
- **DA-124 — O teste de participantes do purge da Account afirma o conjunto
  por tipo e o purge ganha um teste guiado pelo schema** (consenso, Q18;
  test-failure-triage categoria B). `GraphPurgeTests` deixa de contar
  participantes (`2`) e passa a afirmar a presença de Graph, Jobs e
  AgentIdentities por tipo. Um teste novo roda todos os
  `IAccountPurgeParticipant`, lista em `information_schema` toda tabela com
  `account_id` e afirma que nenhuma tem linhas da Account purgada, enquanto as
  da outra Account sobrevivem. As tabelas `oidc_*` (sem `account_id`) ficam no
  teste próprio do `AgentIdentitiesAccountPurge`, pelo subject do usuário. A
  contagem era só um alarme grosseiro; o teste por schema verifica a regra
  transversal "todo épico que adiciona um tipo de dado estende o purge".
- **DA-125 — Interface do frontend de Agentes conectados** (consenso, Q17, via
  `design-an-interface`: quatro desenhos, o arquiteto escolheu um híbrido e a
  analista validou depois de um contra-argumento). Em
  `web/src/features/agents/agentIdentities.ts`, no formato da DA-118:
  `AgentIdentityView` (`{id, name, clientName, clientId, scopes, status:
  "active" | "discontinued", connectedAt, lastUsedAt: string | null}`),
  `AGENT_NAME_MAX_LENGTH = 100`, `agentNameSchema` (trim, 1..100),
  `SCOPE_LABELS` (mesmo texto da tela de consentimento, com teste que fixa as
  strings; scopes desconhecidos nunca aparecem crus), `agentIdentityKeys`,
  `agentIdentitiesQuery` (infinita por cursor, `select` achata),
  `isAgentIdentityGone` (404 `accounts.agent_identity_not_found`),
  `useRenameAgentIdentity(id)` (`{name}`) e `useRevokeAgentIdentity(id)`. As
  mutações não fazem retry em 404/422, invalidam a lista no sucesso e no
  "gone", e não são otimistas. Não há hook de conveniência: o E5 monta o mapa
  id→nome com `select` sobre `agentIdentitiesQuery`. O backend tira
  `offline_access` da view, calcula `status` na leitura e o GET devolve só
  conexões não revogadas (ativas e descontinuadas), com teste; id revogado em
  PATCH/DELETE responde 404. Uma escrita concorrente na linha (o carimbo de
  último uso, outro rename) faz PATCH, DELETE e o consentimento relerem a linha
  e aplicarem a mudança de novo; só se os outros escritores vencerem todas as
  tentativas a resposta é 409 `accounts.agent_identity_conflict` (API-030), e
  dois primeiros consentimentos paralelos do mesmo cliente reutilizam a mesma
  AgentIdentity. `AgentsPage` (sem props, rota lazy `agentes`, link
  "Agentes conectados" no `AppLayout`) é dona de todos os estados: carregando,
  vazio (como conectar um agente), erro com "Tentar de novo", "Carregar mais",
  422 do nome sob o campo, 404 "já não está conectado" com atualização da
  lista. Cada linha mostra nome, `clientName` só como texto rotulado como
  informado pelo cliente (DA-030), scopes pelos rótulos, conectado em, último
  uso ("Nunca usado" quando nulo) e o selo em texto "Cliente descontinuado";
  botões "Renomear {nome}" e "Revogar {nome}" em toda linha, inclusive
  descontinuada. A revogação usa a confirmação inline já usada no delete de
  Node (sem `AlertDialog` nem dependência nova), dizendo que é irreversível e
  corta o acesso na hora; o foco vai para "Confirmar revogação", Cancelar
  devolve o foco ao botão, e depois de revogar o foco vai ao próximo item ou ao
  título da lista. A página tem a própria região `role="status"`; o 401 fica
  com a casca da app.

- **DA-126 — Consentimento que falha não deixa grant órfão** (arquiteto, Q24/Q25,
  rodada extra autorizada pelo usuário depois do terceiro gate; DB-041 e
  GEN-070). `ConnectAsync` passa a usar um helper novo,
  `InAccountResultTransactionAsync`, que faz commit quando o `Result` dá certo
  e rollback quando falha. O `InAccountTransactionAsync` compartilhado não
  muda, porque tem 11 chamadores (alguns não devolvem `Result`) e uma sobrecarga
  com o mesmo nome mudaria o comportamento deles sem ninguém ver; se todo
  `Result` com falha deve desfazer a transação é uma auditoria à parte. A
  compensação (revogar o grant recém-criado) foi descartada: é mais uma
  escrita que pode falhar e deixa uma linha revogada sem dono. Testes de
  integração: reconsentimento que perde a corrida 3 vezes deixa a conexão
  existente intacta e nenhuma authorization nova; primeiro consentimento que
  perde sempre não cria nada; o helper desfaz `Fail`, confirma `Ok` e mantém o
  trabalho depois de um `SaveChanges` que falhou e foi tratado (savepoint).
  Candidata a ADR mais tarde: rollback por padrão em `Result` com falha entre
  módulos.

## Pré-requisitos do usuário

- [ ] Comprar um domínio e apontar o DNS para o Cloudflare
- [ ] Confirmar que os planos de ChatGPT (modo desenvolvedor) e Claude.ai permitem conectores MCP personalizados com ferramentas de escrita

## Checklist

### Slice 1 — Cliente pré-registrado
- [ ] Spike: OpenIddict + `resource`/audience + Protected Resource Metadata contra Claude.ai e ChatGPT
  - Parte em processo concluída: `resource` (RFC 8707) com audience fixada no
    MCP, PRM e discovery validados pelo cliente MCP do SDK. Falta a validação
    contra Claude.ai e ChatGPT (manual, junto com o aceite abaixo).
- [x] Protected Resource Metadata, 401 com `resource_metadata`, discovery do servidor de autorização
- [x] Tela de consentimento (Razor no servidor): hostname do redirect, scopes em linguagem simples, aviso de que o agente poderá alterar o grafo (com Undo), aviso para cliente só-loopback
- [x] AgentIdentity por concessão + tela "Agentes conectados" (último uso, renomear, revogar)
  - Tabela `agent_identities` com RLS, `GET/PATCH/DELETE /api/agent-identities`
    (só conexões não revogadas, `status` ativo/descontinuado) e a tela
    `/agentes` na SPA, na interface da DA-125.
- [x] Endpoint MCP com uma ferramenta Safe (`whoami`/`ping`)
- [x] Perfil `public` do compose com cloudflared + runbook do túnel
- [x] Rate limits em `/authorize`, `/token` e login
- [ ] Aceite manual: conectar Claude.ai e ChatGPT e chamar a ferramenta

### Slice 2 — CIMD
- [x] Handler de CIMD com fetch protegido contra SSRF, cache, match exato de redirect e loopback
- [x] Metadata anunciando suporte a CIMD
- [ ] Aceite manual: Claude Code via CIMD

### Condições das DA-119 a DA-125
- [x] DA-119: purge só das authorizations e tokens do usuário (os clientes ficam), teste de arquitetura contra SQL cru sobre `oidc_*` e contra listar grants por outra coisa que não o subject ou o id, e teste de que a falha de um participante posterior desfaz a remoção dos grants
- [x] DA-120: `/connect/login` no mesmo orçamento por e-mail do login da SPA, sem `login_hint`, retorno validado (com `//evil`, `/\evil` e variantes codificadas), domínio do emissor e "Esqueci a senha" na página, aviso de escrita só quando ela é oferecida
- [x] DA-121: AgentIdentity ativa verificada em toda requisição ao `/mcp` sem cache (e o token precisa ser da concessão atual), 401 com `error="invalid_token"`, cota por AgentIdentity com teto agregado por Account, GLOSSARY e CLAUDE.md atualizados
- [x] DA-122: leitura obrigatória e escrita desmarcável no consentimento, conjunto postado validado contra o oferecido, `scopes_supported` exato, reautorizar substitui os scopes e encerra a concessão anterior, 403 `insufficient_scope` para ferramenta de escrita sem `lifegraph.write` (testado com uma ferramenta de escrita só do teste)
- [x] DA-123: cliente pré-registrado fora da configuração fica descontinuado (recusado em `/connect/authorize`, `/connect/token` e `/mcp`, listado e revogável), chave de limite pelo IP real (`CF-Connecting-IP` só da rede do conector, com teste), nenhum log de requisição ou redirect com a query de `/connect/*` (teste em Trace do fluxo inteiro), regra de caminho das chaves na varredura de segredos, DA-116 cita a DA-123
- [x] DA-124: `GraphPurgeTests` afirma os participantes por tipo; `AccountPurgeTests` verifica pelo schema toda tabela com `account_id`

### Testes obrigatórios
- [x] Fluxo OAuth + PKCE completo dentro do processo
- [x] Token com audience errada é rejeitado
- [x] Revogação invalida o refresh imediatamente
- [x] O switch de auto-consent só funciona no ambiente Testing

## Critérios de saída

Claude.ai e ChatGPT conectam pelo túnel, passam pelo consentimento e chamam a
ferramenta MCP. O usuário vê e revoga a conexão.

## Fora de escopo

Ferramentas de leitura e escrita do grafo (E4/E5), PATs, DCR, deploy (E13).
