# E1 — Contas e login humano

- **Ordem:** 1
- **Depende de:** E0
- **Última revisão:** 2026-10-04

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
- **DA-094 — `ICurrentPrincipal` lido direto do ClaimsPrincipal, sem
  `IUserIdentityGateway`** (consenso, decidido no `/flow` de 2026-10-04). Revê
  a DA-005 neste ponto. A interface do limaj só devolve um user id `string?`,
  sem account_id nem tipo de principal, então montar por cima dela obrigaria a
  reler os claims de qualquer forma. O limaj também não está publicado (repo
  privado, net9.0, sem feed, sem pacote `Web`). O principal falha fechado:
  claim ausente, `account_id` que não é Guid ou tipo desconhecido contam como
  não autenticado, nunca como conta padrão. O tipo já prevê ShareVisitor (filtro
  central de leitura), embora o E1 só produza Human. `ICurrentPrincipal`
  substitui o `AnonymousAccountContext` como fonte do `IAccountContext`.
  **Em aberto, fora do E1:** o resto da DA-005 (Result/Error, `ResultExtensions`,
  `RequestRunner` como pacotes) depende de um pacote que não existe. Publicar,
  copiar ou dispensar o limaj é decisão do usuário antes do primeiro épico que
  precisar deles.
- **DA-095 — Conta criada por comando de CLI do owner; o convidado define a
  própria senha pelo link** (consenso). `dotnet run --project
  src/LifeGraph.Host -- accounts create --email ...` roda com o papel da app,
  sem superfície HTTP antes do túnel do E3 (DA-011/028). A CLI não recebe senha,
  para ela não parar no histórico do shell e para o owner nunca conhecer a
  credencial de outra Account. A conta nasce sem senha e pendente. O link do
  e-mail leva um token de uso único e com validade, que define a senha e
  confirma o e-mail na mesma ação. A CLI também reenvia o link, e criar de novo
  um e-mail já pendente não duplica a conta. A primeira conta do owner passa
  pelo mesmo comando. Fica para o E14 uma regra de expiração de contas pendentes
  nunca ativadas, que guardam o e-mail de um terceiro sem consentimento.
- **DA-096 — Páginas de confirmação/definição de senha, "esqueci a senha" e
  redefinição de senha entram no E1** (consenso). Os links dos e-mails do E1
  já apontam para elas, e sem a página de definir senha nenhum convidado
  consegue entrar. "Esqueci a senha" responde sempre igual, exista ou não o
  e-mail.
- **DA-097 — Interface do cliente HTTP e da sessão na SPA** (consenso: o
  arquiteto escolheu, o analista validou após um contra-argumento).
  `openapi-fetch` é o cliente único (`api`), com middleware de CSRF (token
  buscado sob demanda, uma requisição em voo por vez, uma nova tentativa só em
  `csrf_token_invalid` e só em métodos que alteram dados). `unwrap` lança
  `ApiError {status, code, fieldErrors}`, `isApiError` faz o narrowing e
  `resetCsrfToken` existe. A sessão (`features/session`) tem:
  - `sessionQuery` (401 vira `null`) e `useSession`;
  - os loaders `requireSession`, `redirectIfAuthenticated` (login e esqueci a
    senha) e `blockIfAuthenticated` (links de confirmação e redefinição: mostra
    "você está logado como X" com botão de sair, sem consumir o token), lendo o
    QueryClient pelo contexto do React Router, de modo que `routes` continua
    sendo dado;
  - um loader que lê o token do fragmento uma vez e o apaga com
    `history.replaceState`;
  - `safeReturnTo`, que só aceita caminho relativo.

  Login, logout e o handler global de 401 fazem `cancelQueries`, `clear`,
  `resetCsrfToken` e trocam a sessão. O cache nunca sobrevive a uma troca de
  identidade, o que é o isolamento entre contas no cliente. O handler de 401 só
  age quando havia sessão, e endpoints anônimos nunca respondem 401 (usam
  400/422 com `code`). Depois de definir ou redefinir a senha não há login
  automático: o usuário vai para `/login?notice=...`, e a redefinição revoga
  todas as sessões da Account (security stamp). 429 `too_many_attempts` (com
  `Retry-After`) é igual para e-mail existente e inexistente, com texto genérico
  na UI. Por isso o lockout do Identity fica desligado e o limitador por cliente
  e por e-mail é o único freio: o lockout durava mais que a janela e revelava
  quais e-mails têm conta (achado do review). **Exceção a API-030 (DEVE):**
  credenciais erradas no login respondem 400 `invalid_credentials`, e não 401,
  porque um 401 num endpoint anônimo dispararia o handler de sessão expirada da
  SPA. O link usado ou expirado segue API-030 e responde 422
  `invalid_or_expired_token`.
- **DA-098 — Exceção a BE-022 e à regra de RLS do E0 para o diretório de
  credenciais** (consenso no review do `/flow`). `users`, `user_claims`,
  `user_logins` e `user_tokens` ficam sem policy de RLS. Login, "esqueci a
  senha" e o resgate de links precisam achar o usuário antes de existir uma
  Account no contexto, e o e-mail é único entre todas as Accounts. A tabela é um
  diretório global que aponta para uma Account, e não dado do grafo. Uma policy
  `OR current_account_id() IS NULL` foi rejeitada porque falha aberta (contradiz
  a DA-094). Um papel com BYPASSRLS também foi rejeitado, porque contradiz o E0.
  Condições:
  1. Teste de arquitetura: só o módulo Accounts referencia os tipos do Identity
     e do OpenIddict.
  2. Teste de integração no catálogo do Postgres: toda tabela com `account_id`
     tem RLS ligada, salvo a lista desta DA.
  3. `users` mantém `account_id` com FK.
  4. Nenhuma resposta revela outro usuário: neutra em "esqueci a senha",
     NotFound/422 para id alheio ou inválido.
  5. `users` guarda só o mínimo de dado pessoal; perfil vai para tabelas da
     Account com RLS.
  6. A PIA do E14 lista essa tabela como dado pessoal isolado por código da
     aplicação e decide, antes do E13, se endurece com uma função
     `SECURITY DEFINER` que devolve só (user_id, account_id).

  As tabelas `oidc_*` não têm `account_id` hoje e não estão nesta exceção. O E3
  decide o isolamento delas quando passarem a guardar grants de agentes por
  Account.

## Checklist

- [x] Identity (e-mail + senha) com e-mail de confirmação via Mailpit
- [x] Provisionamento de Account ao criar usuário (1 usuário = 1 Account no MVP)
- [x] OpenIddict configurado como emissor (sem clientes de agente ainda)
- [x] Cookie BFF HttpOnly, SameSite e proteção CSRF para a SPA
- [x] `ICurrentPrincipal` a partir do ClaimsPrincipal (DA-094): account_id + tipo (Human | AgentIdentity | ShareVisitor)
- [x] Rate limiting em login e recuperação de senha
- [x] Cadastro aberto desligado; criação de conta por convite/owner (CLI, DA-095)
- [x] Tela de login/logout na SPA, com as páginas de definir senha, esqueci a senha e redefinir senha (DA-096, DA-097)
- [x] Testes: login, sessão, isolamento entre duas contas

## Critérios de saída

- Dois usuários em contas diferentes não enxergam nada um do outro (teste automatizado).
- O emissor OpenIddict está pronto para receber clientes no E3.

## Fora de escopo

Clientes OAuth de agentes (E3), exclusão de conta (E11), e-mail real (E13),
passkeys.
