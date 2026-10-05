# E2 — Núcleo de escrita do grafo

- **Ordem:** 2
- **Depende de:** E1
- **Última revisão:** 2026-10-05

## Pendências herdadas do E1 (discutir no próximo `/flow` antes de implementar)

O review do E1 (commit `4aebdff`, branch `flow/2026-10-04`) deixou três
achados de spec sem decisão registrada (itens 1–3), e o run revelou uma
pendência de dependência (item 4, decidida pelas DA-100 a DA-106). Os achados não bloquearam o E1, mas tocam
regras que o E2 vai estender (RLS em toda tabela nova, o filtro central de
leitura e o ownership). Cada um precisa virar `DA-###` (aceitar como está ou
corrigir) antes do primeiro endpoint de escrita do grafo.

1. **Policy `accounts_provisioning` sem DA.** A migration
   `20261004023509_AccountsAndIdentity` cria em `accounts` uma policy
   `FOR INSERT WITH CHECK (true)`: qualquer código com o papel da app insere
   linhas em `accounts` sem Account no contexto. Ela existe porque o
   provisionamento (CLI `accounts create`, DA-095) cria a Account antes de haver
   contexto. Só permite insert, então não expõe dados. Ainda assim é uma exceção
   de RLS que nenhuma DA registra, e a DA-098 rejeitou uma policy parecida
   ("falha aberta") para `users`. Decidir: registrar como exceção (estendendo a
   DA-098 ou numa DA nova) ou restringir o insert (por exemplo com uma função
   `SECURITY DEFINER` de provisionamento).
2. **`accounts resend` não invalida os links enviados antes.** A DA-095 pede
   "token de uso único e com validade". Hoje o reenvio não troca o security
   stamp (`AccountProvisioner.cs`), então todos os links anteriores continuam
   válidos até um deles ser usado ou passarem 2h. Usar qualquer um invalida os
   outros. O risco aparece quando o primeiro e-mail foi para um endereço
   digitado errado. Decidir: aceitar e registrar, ou trocar o security stamp no
   reenvio.
3. **A policy padrão "autenticado" mudou o comportamento do E0 sem registro.**
   O fallback de autorização do E1 (`Program.cs`, API-080) passou leituras
   anônimas que antes respondiam 404 a responder 401
   (`AccountIsolationTests.cs`). Isso conversa com a regra do CLAUDE.md
   ("acesso a outra conta responde NotFound, nunca 403") e com o futuro
   `ShareVisitor` (E10), que lê sem sessão de Human. Decidir e registrar qual é o
   contrato para requisição sem sessão: 401 em tudo, ou 404 em rotas de
   recurso.

4. **Contrato de resultado e erro (limaj-framework).** Decidido no `/flow` de
   2026-10-04 com o limaj 3.0.0 publicado (DA-100 a DA-106). O que a 3.0.0
   entregou e os pedidos ao owner estão na seção
   [Contrato de resultado e erro](#contrato-de-resultado-e-erro-limaj-300)
   logo abaixo.

## Contrato de resultado e erro (limaj 3.0.0)

O `/flow` de 2026-10-04 discutiu primeiro o consumo do limaj-framework
([relatório](../../reports/2026-10-04-limaj-consumo-nuget.md)) e deixou
pré-decisões contra o plano do owner. Depois da publicação da 3.0.0, um segundo
`/flow` no mesmo dia revalidou essas pré-decisões contra o pacote real. O
resultado são as DA-100 a DA-106, na seção [Decisões](#decisões).

### O que a 3.0.0 entregou, comparado com o plano

Conferido no tag `v3.0.0` (código e CHANGELOG) em 2026-10-04. Os cinco pacotes
foram enviados ao nuget.org nesse dia e estavam em validação. Antes de
referenciar, confira se `https://api.nuget.org/v3-flatcontainer/limaj.framework.core/index.json`
lista a `3.0.0`.

| Item do plano | O que saiu na 3.0.0 | Consequência |
|---|---|---|
| `ErrorType.BusinessRule` → 422 e `Result.BusinessRule(...)` | **Não saiu.** `ErrorType` = Validation, NotFound, Conflict, Forbidden, Unauthorized, Unexpected e `TooManyRequests` (novo) | O 422 sai do nosso `IErrorHttpMapper`, escolhido pelo catálogo de códigos (DA-101). Pedido ao owner para a 4.0.0 |
| `Error.HttpStatusCode` removido | `[Obsolete]`; o mapper padrão ainda respeita. Sai na 4.0.0 | Barrado pelo build (CS0618 como erro) e por teste (DA-102) |
| `V3` como padrão | O padrão é **`V2`** | `Format = V3` explícito (DA-102) |
| `IncludeExceptionDetails` `bool`, padrão `false` | `bool?`, padrão `null`, que **lê `ASPNETCORE_ENVIRONMENT`** | `false` explícito; o perfil `public` roda como Development (DA-102) |
| Separar o núcleo de resultado | Saiu: pacote novo `Limaj.Framework.Core` (namespace `Limaj.Framework.Core`; exceções e `IExceptionToErrorMapper` em `Limaj.Framework.Core.Errors`). O `Web` depende só do `Core`. Todos os pacotes `Limaj.*` precisam estar na mesma versão (senão `TypeLoadException`) | Só `Core` + `Web` (DA-100) |
| Principal tipado | Saiu: `UserPrincipal` extensível e `GetCurrentPrincipalAsync`, mas no `Abstractions` | DA-094 mantida (DA-106) |
| `IErrorHttpMapper`, `DefaultErrorHttpMapper` público, `AddLimajHttpErrors`, `IHttpResultResponder` (`ToHttpResult`, `RunAsync`), `Error.RetryAfter`, correções de segurança | Saíram como planejado. `MapWithStatusCode(error, status)` é o ponto para status fora da tabela | Usados como estão |
| `BuiltInExceptionToErrorMapper` | Mapeia **só** as exceções do limaj (`DomainValidationException`, `NotFoundException`, `ConflictException`). Ele roda antes do nosso mapper. Exceção genérica do .NET vira 500 `unexpected_error` com mensagem genérica | Seguro, desde que os módulos não lancem as exceções do limaj (DA-104) |

### Pedidos ao owner do limaj

- `ErrorType.BusinessRule` (422) na 4.0.0. Um membro novo no enum quebra
  `switch` exaustivo, por isso fica para uma major. Não bloqueia o E2 (DA-101).
- Tirar o `IUserIdentityGateway`/`UserPrincipal` do `Abstractions` para um
  pacote no nível do `Core`, e aceitar um principal sem usuário (por exemplo o
  ShareVisitor). Não bloqueia o E2 (DA-106).

Os dois estão redigidos, prontos para enviar, em
[`docs/reports/2026-10-05-limaj-pedidos-ao-owner.md`](../../reports/2026-10-05-limaj-pedidos-ao-owner.md).
O envio é do usuário (DA-110).

A Q9 (ação Sensitive de agente responde como sucesso pendente) e a Q4b (em que
ordem checar scope e visibilidade) estão no início do
[E5](../backlog/e05-mcp-escrita-agent-changes.md).

### Candidatas a ADR

- Contrato de erro HTTP e MCP: `Result`/`Error` do limaj `Core`, nosso
  `IErrorHttpMapper` no formato `V3`, 422 escolhido pelo catálogo de códigos
  (DA-100, DA-101).
- DA-094 mantida diante do principal tipado da 3.0.0 (DA-106).
- Ação Sensitive de agente responde como sucesso pendente (`202`), não como
  erro (ver E5).

## Contexto

O núcleo do produto: Nodes, Relations, Types e Properties, gravados por um
pipeline único que gera **GraphChangeSet + Provenance** desde a primeira
escrita. Proveniência e ChangeSet não podem ser adicionados depois, ou o
histórico e o Undo ficam com buracos. Este épico também traz a UI humana
mínima para inspecionar e desfazer, que precisa existir antes de qualquer
agente escrever (princípios "Human-visible result" e "Reversible automation" da
v2).

## Decisões

- **DA-013 — Pipeline único de escrita** (consenso, ADR: [docs/adr/0002-pipeline-unico-de-escrita-com-changeset.md](../../adr/0002-pipeline-unico-de-escrita-com-changeset.md)). Toda
  escrita, de qualquer ator (UI, API, MCP, import), passa por um único comando
  da camada de aplicação. Ele grava o `GraphChangeSet` + Provenance **na mesma
  transação** da mudança no grafo. Um comando que falha não persiste nada. Sem
  event sourcing completo: `changesets`/`change_entries` guardam o antes e o
  depois.
- **DA-014 — `assertion` (hard|soft) separado de `origin`
  (user|agent|system|import)** (consenso). A spec misturava os dois num só enum.
  Uma relação afirmada por agente é Hard com origin=agent. Soft Relations só
  vêm do sistema (E9) e ficam **fora** do log de ChangeSet (DA-070).
- **DA-015 — Exceção a DB-008: valores de Property em JSONB, com chave
  `property_id`** (consenso). A ontologia flexível da v2 (§7–8) não cabe num
  esquema fixo. Mitigações:
  - validação por Type na camada de aplicação;
  - chave por id (renomear não toca nos dados);
  - índice GIN `jsonb_path_ops` para igualdade;
  - filtros de intervalo varrem a partição da conta, o que é aceitável na
    escala de um grafo pessoal. Índices de expressão só se uma medição
    justificar.
  ADR: [docs/adr/0003-properties-em-jsonb-e-definicoes-da-account.md](../../adr/0003-properties-em-jsonb-e-definicoes-da-account.md)
- **DA-016 — Property Definitions pertencem à Account e são anexadas a Types
  (N:N)**, com **Outras propriedades** (consenso + usuário Q11=B). Ao mudar o Type de um Node, os valores cuja definição não está no
  novo Type continuam guardados e visíveis numa seção "Outras propriedades".
  Eles voltam sozinhos, pelo `property_id`, quando o Node retorna a um Type
  compatível. Entram no export, no purge, no FTS e no `get_context`. Isso evita
  perder dados ao reclassificar itens, o que é comum ao processar a Inbox.
  ADR: [docs/adr/0003-properties-em-jsonb-e-definicoes-da-account.md](../../adr/0003-properties-em-jsonb-e-definicoes-da-account.md)
- **DA-017 — NodeReference não é Property, é sempre Relation** (consenso,
  ADR: [docs/adr/0004-nodereference-sempre-como-relation.md](../../adr/0004-nodereference-sempre-como-relation.md)). Dois jeitos de ligar Nodes quebrariam a travessia, o
  filtro de ocultos (vazaria por valor de propriedade), o Undo e a
  explicabilidade.
- **DA-018 — Type é opcional no Node** (consenso + usuário). Todo Node tem
  `title` e `body` (markdown). Um Type padrão "Note" poluiria os filtros. A UI
  oferece um filtro "Sem Type".
- **DA-019 — Inbox é um estado explícito, com saída explícita** (consenso). O
  campo `inbox_entered_at` não é uma query derivada: uma regra como "sem Type e
  sem Relations" faria o item sair da Inbox como efeito colateral. "Arquivar" é
  um comando que gera ChangeSet e pode ser desfeito.
- **DA-020 — Undo é um ChangeSet compensatório** (consenso). Ele é recusado,
  mostrando os conflitos, quando uma alteração posterior tocou as mesmas
  entidades. Nunca sobrescreve às cegas.
- **DA-021 — Delete ≠ Purge** (consenso + usuário, ADR: [docs/adr/0005-delete-separado-de-purge.md](../../adr/0005-delete-separado-de-purge.md)).
  - Delete é um tombstone que pode ser desfeito por **30 dias** (usuário) em
    todos os planos. Essa janela é separada da retenção do histórico.
  - Ao fim da janela, o job de purge remove o Node e **apaga o conteúdo dos
    ChangeSets** que o tocaram, deixando só o esqueleto. Senão o histórico do
    Premium (1 ano) guardaria o conteúdo apagado.
- **DA-022 — Concorrência otimista (`version` por Node)** (consenso, BE-041).
  A UI e os agentes editam ao mesmo tempo.
- **DA-023 — 8 tipos de Property:** Text, Number, Boolean, Date, DateTime,
  URL, Select, MultiSelect (consenso). Status vira Select; Rating vira Number;
  Location vira Text ou Relation; TemporalAnchor vira propriedades de data.
  Até o editor completo (E8), **mudar o tipo de uma propriedade que já tem
  valores fica bloqueado**.
- **DA-024 — UI atualiza por polling/refetch + feed de ChangeSets com cursor
  (`GET changesets?since=`)** (consenso). Sem websocket, é o mesmo contrato que
  um SSE entregaria depois.
- **DA-099 — Padronizar no limaj-framework** (usuário, `/flow` de
  2026-10-04). Revê a DA-005. O consumo concreto da 3.0.0 está nas DA-100 a
  DA-106.
  - **Por quê.** O usuário quer padronizar seus projetos no limaj: "se a cada
    projeto eu decidir não usar o framework, isso mostra que não preciso dele".
    Quando falta algo no limaj, o caminho é pedir ao owner, não fazer um
    substituto local.
  - **O que resolveu os bloqueios.** O limaj está no nuget.org, com leitura
    anônima, target `net10.0` e licença MIT. Isso resolveu o feed autenticado e
    a licença. O owner aceitou os pedidos de evolução e publicou a 3.0.0 em
    2026-10-04.
  - **Validar antes de mudar.** Antes de qualquer mudança que toque o limaj,
    confira o CHANGELOG e as versões publicadas. O plano do owner não é o que
    foi entregue (ver a tabela na seção "Contrato de resultado e erro").
  - **Sem ponte HTTP local.** Ela seria código descartável e criaria um
    contrato provisório.
  - **Continua valendo da DA-005.** Nada de `Persistence.EFCore`, `BaseEntity`
    nem `BaseRepository` nos agregados. O `IUserIdentityGateway` também não é
    usado (DA-094, DA-106).
  - **Versões.** O limaj segue SemVer, porque outros projetos usam os pacotes
    como estão. Subir de major é uma tarefa explícita no épico, nunca um bump
    automático do Dependabot sem revisão.
- **DA-100 — Só `Limaj.Framework.Core` e `Limaj.Framework.Web` 3.0.0**
  (consenso, `/flow` de 2026-10-04, Q11). Revê a parte da DA-099 que começava
  pelo `Abstractions` 2.0.0.
  - Os dois pacotes ficam fixados numa única propriedade de versão no
    `Directory.Packages.props`. Todos os `Limaj.*` precisam estar na mesma
    versão, ou a aplicação quebra em tempo de execução com `TypeLoadException`.
  - O `Core` pode ser usado em qualquer lugar, inclusive no domínio, porque não
    tem dependências.
  - O `Web` só entra no `LifeGraph.Http`, no Host e nos namespaces `*.Http` dos
    módulos, que o recebem pelo `LifeGraph.Http`. O `LifeGraph.Http` é uma
    camada fina: o `IErrorHttpMapper` do produto, o catálogo de códigos e o
    `202`. O MCP tem o próprio mapper de `Error` para erro de ferramenta, lendo
    o mesmo catálogo. REST e MCP devolvem o mesmo `code` para a mesma falha.
  - `Abstractions`, `Application` e `Persistence.EFCore` não são referenciados.
    O `Web` 3.0.0 só depende do `Core`, então o `Abstractions` não tem por que
    estar no grafo de dependências, e a DA-005 passa a valer pela própria
    estrutura de dependências, não só pelo review.
  - **Como se garante:** testes de arquitetura (nada fora de `*.Http`,
    `LifeGraph.Http` e Host depende do `Web`; nada depende de `Abstractions`,
    `Application` ou `Persistence`); um teste de que nenhum assembly
    `Limaj.Framework.Abstractions` está nas dependências do Host (o ArchUnit não
    enxerga pacote referenciado e não usado); o Dependabot agrupa os `Limaj.*`
    e ignora versões major.
- **DA-101 — Falha de regra de negócio é `ErrorType.Validation` e vira 422
  pelo catálogo de códigos** (consenso, Q12). A 3.0.0 não trouxe o
  `ErrorType.BusinessRule`.
  - Cada código de regra fica registrado no catálogo de erros com status 422.
    O nosso `IErrorHttpMapper` herda o `DefaultErrorHttpMapper` e chama
    `MapWithStatusCode(error, status)` quando o código está no catálogo. Fora
    dele, segue o padrão.
  - O status depende do código, não do `ErrorType`. Se o owner criar o
    `BusinessRule` depois, o cliente não percebe a troca.
  - `Validation` é o sentido mais próximo entre os tipos que existem ("entrada
    bem formada, regra não cumprida"). `Conflict` (409) fica só para
    concorrência e unicidade, para não confundir uma regra com uma colisão.
  - **Risco:** um código de regra esquecido fora do catálogo sai como 400 sem
    ninguém perceber. Proteções: um teste que exige que todo código criado
    pelas factories `*Errors` esteja no catálogo, e um helper único no shared
    kernel que cria o `Error` já declarado como regra (uma convenção fina sobre
    o `Core`, não um substituto).
  - "Limite do plano atingido" (Free/Premium) também é um código de regra com
    422, nunca 403 (`Forbidden` é só para scope). Ele nunca dispara em leitura,
    export, exclusão ou Undo.
  - Vai ao owner o pedido de `ErrorType.BusinessRule` para a 4.0.0, sem
    bloquear o E2.
- **DA-102 — Opções do limaj explícitas e proteções contra regressão**
  (consenso, Q13).
  - `AddLimajHttpErrors` com `Format = V3`, `IncludeExceptionDetails = false` e
    `IncludeDetailsOutsideValidation = false`, sempre explícitos. Os padrões da
    3.0.0 são `V2` e "ler o ambiente". O perfil `public` (DA-027/028) expõe
    login, OAuth e MCP pelo túnel com o app em Development, então com o padrão
    um 500 mostraria detalhes da exceção na internet. Um teste de integração
    força `ASPNETCORE_ENVIRONMENT=Development` e confere que o 500 tem corpo
    genérico e `code`.
  - `Error.HttpStatusCode`: o build já falha com CS0618, porque o
    `Directory.Build.props` tem `TreatWarningsAsErrors`. Um teste falha se
    alguém suprimir o CS0618 (`NoWarn`, `WarningsNotAsErrors` ou `#pragma`), e
    uma regra de arquitetura barra o acesso ao membro.
  - As fachadas estáticas do `Web` (`ResultExtensions`, `RequestRunner`,
    `ExceptionExtensions`) não estão obsoletas, então uma regra de arquitetura
    as barra. Os endpoints usam o `IHttpResultResponder` injetado.
  - O limitador de tentativas de credencial do E1 passa a usar
    `ErrorType.TooManyRequests` com `Error.RetryAfter`. O 429 e o
    `Retry-After` dependem só da janela, nunca de o e-mail existir, para não
    revelar quais contas existem. Um teste confere que o valor do cabeçalho,
    arredondamento incluído, é o de hoje. O middleware de rate limit do ASP.NET
    continua no `OnRejected`, com o mesmo `code` e o mesmo formato de corpo.
  - Testes de integração provam que toda resposta de erro tem `code` e que
    outra conta e Node oculto dão 404.
- **DA-103 — As respostas de erro do E1 mudam de formato uma vez, na primeira
  fatia da Fase 0** (usuário, recomendação dos dois especialistas, Q14). Revoga
  a pré-decisão "respostas do E1 idênticas".
  - Continuam iguais o status, o `code` e o `Retry-After`. Mudam o `title`, que
    passa a ser o do status, e o `detail`, que passa a ser o `Error.Message`
    (formato `V3`).
  - Ainda não há consumidor externo. Sobrescrever títulos no mapper só para
    manter o corpo do E1 seria um desvio local do `V3`, contra a DA-099.
  - Toda mensagem do E1 é revista como **texto público**: nada de e-mail, token
    ou algo que diferencie "usuário não existe" de "senha errada".
  - A SPA depende só do `code`, nunca de `title`/`detail`. Testes de contrato
    comparam status, `code` e cabeçalhos, nunca o `title`.
  - Migram juntos o `ApiProblems`, o `CredentialProblems`, o CSRF
    (`CsrfProtection`) e o limitador de tentativas. Os endpoints do E1 viram a
    suíte de regressão do mapper, do catálogo e do `V3`.
  - A fatia vem antes de qualquer endpoint do grafo, para existir um único
    contrato. A renomeação dos códigos (DA-105) entra nela.
- **DA-104 — Falha esperada é sempre `Result`; exceções do limaj não são
  lançadas pelos módulos** (consenso, Q15).
  - Exceção é só para bug e infraestrutura. Lote é tudo-ou-nada por
    GraphChangeSet e precisa juntar várias falhas, com `Details` por item. Uma
    exceção para na primeira. O MCP precisa do mesmo `code` sem passar pelo
    pipeline HTTP, e o Undo precisa de um `Result` para decidir.
  - A conversão de violação de concorrência ou unicidade em `Conflict` fica
    dentro do pipeline de escrita, sem deixar ChangeSet parcial.
  - O nosso `IExceptionToErrorMapper` é só a rede de segurança HTTP: exceção
    desconhecida vira `Unexpected`, que vira 500 genérico.
  - Os módulos não dependem de `Limaj.Framework.Core.Errors` (regra de
    arquitetura). Assim o `BuiltInExceptionToErrorMapper`, que roda antes do
    nosso e só conhece as exceções do limaj, nunca entra em ação, e bugs nunca
    saem como 4xx com a mensagem da exceção.
  - Um único `Error` por resultado, com `Details` por campo ou item. Outra conta
    e Node oculto respondem sempre `NotFound`. `Forbidden` é só para scope do
    principal.
- **DA-105 — Códigos de erro com prefixo de módulo** (usuário, seguindo o
  arquiteto, Q6). Revê a Q10.
  - Código de um módulo leva o prefixo dele: `graph.node_not_found`,
    `accounts.invalid_credentials`. Código comum a todos os módulos fica sem
    prefixo: `validation_failed`, `scope_missing`, `unexpected_error`.
  - Os códigos do E1 `invalid_credentials`, `password_rejected` e
    `invalid_or_expired_token` passam a `accounts.*` na mesma fatia da DA-103.
    `csrf_token_invalid`, `too_many_attempts` e `EmailTooLong` (este fora do
    snake_case) são classificados pela mesma regra nessa fatia.
  - Os códigos são definidos por módulo como factories estáticas, com teste de
    unicidade. Código publicado nunca muda de nome nem é reaproveitado.
  - O catálogo é uma fonte única, publicada no OpenAPI e como resource do MCP.
    Por código, ele traz o status e a classe de recuperação (`fix_input`,
    `retry_after`, `ask_user`, `not_recoverable`), que não ficam no `Error`. A
    resposta leva `traceId`. O `Retry-After` também vai no erro do MCP.
- **DA-106 — DA-094 mantida diante do principal tipado da 3.0.0** (usuário,
  recomendação dos dois especialistas, Q16). O `ICurrentPrincipal` continua
  sendo a porta do produto, e o `IUserIdentityGateway` não é usado.
  - O motivo original (a interface só devolvia `string?`) caiu. Os motivos que
    valem agora:
    - o gateway fica no `Abstractions`, que traria `BaseEntity`,
      `IRepository` e `IUnitOfWork` de volta e desfaria a DA-100;
    - o ShareVisitor não é um usuário (não tem user id e vem de um token de
      link), e a AgentIdentity age em nome de uma Account;
    - o filtro central de leitura e a RLS precisam de `account_id` e do tipo de
      principal de forma síncrona, dentro da transação e falhando fechado;
    - nada no E2 precisa dele, que só produz Human.
  - Reavaliar no E3/E5, quando AgentIdentity e ShareVisitor existirem. Vão ao
    owner os pedidos de tirar o gateway do `Abstractions` e de aceitar um
    principal sem usuário.
- **DA-107 — Provisionamento de Account por um papel próprio do banco**
  (**provisória** — decidida pelo arquiteto no `/flow` de 2026-10-04, item 1
  do preâmbulo, Q1). A policy `accounts_provisioning` passa a valer só
  `TO lifegraph_provisioner`, um papel de login novo sem BYPASSRLS, com
  `INSERT` em `accounts` e o mínimo do diretório de credenciais que o
  provisionamento usa, sem grant em tabelas do grafo. A CLI do owner
  (`accounts create|resend`) usa `ConnectionStrings__Provisioning`, e o
  processo web nunca recebe essa connection string. O provisionamento roda numa
  única transação, então uma falha no meio não deixa Account órfã. Testes de
  guarda: insert em `accounts` com `lifegraph_app` falha com 42501; o fluxo real
  da CLI provisiona com `lifegraph_provisioner`, e esse papel não lê outras
  Accounts nem tabelas do grafo; um teste de catálogo falha se alguma policy de
  tabela de Account tiver `WITH CHECK (true)` para `PUBLIC` ou `lifegraph_app`.
  Por quê: com `WITH CHECK (true)` no papel da app, qualquer requisição web pode
  criar Accounts, e isso iria junto para a exposição pública do E13. O limite
  fica no banco, e não numa revisão futura que alguém precisa lembrar. A DA-098
  continua deixando `users` gravável pelo papel da app, e isso não muda aqui.
  Outra visão (analista): registrar a policy atual como exceção, porque uma
  linha de `accounts` sozinha não dá acesso a nada, com gatilho de revisão em
  qualquer épico que crie um segundo caminho de criação de Account. Para
  reverter: remover o papel, os grants e `ConnectionStrings__Provisioning`,
  devolver a policy ao papel da app e registrar a exceção com o gatilho. O
  teste do fluxo real da CLI e a atomicidade ficam. Candidata a ADR.
- **DA-108 — `accounts resend` invalida os links anteriores** (consenso, item 2
  do preâmbulo, Q2). O reenvio troca o security stamp antes de mandar o e-mail,
  então só o link mais novo funciona. Isso cumpre o "uso único" da DA-095: no
  caso do endereço digitado errado, quem recebeu o primeiro link não consegue
  definir a senha. Uma Account pendente não tem sessão, então a troca não tem
  outro efeito. Teste: depois do reenvio, o link antigo falha e o novo funciona.
  Fica registrada, para depois, uma lacuna levantada pela analista: o owner não
  tem como corrigir o e-mail ou cancelar uma Account pendente antes da ativação.
- **DA-109 — Requisição sem sessão responde 401 em todas as rotas** (consenso,
  item 3 do preâmbulo, Q3). A regra "NotFound, nunca 403" vale para um principal
  autenticado chegando a dados de outra Account ou a um Node oculto. Sem
  sessão, a resposta é 401 com `WWW-Authenticate`, que o fluxo OAuth/MCP do E3
  usa para descoberta. O 401 sai antes de qualquer busca e é igual para IDs que
  existem e que não existem, então não revela nada. As rotas do ShareVisitor
  (E10) aderem explicitamente, com esquema e policy próprios. Dentro de um
  share, Node fora do subgrafo autorizado e token inválido ou expirado
  respondem 404. Testes: `AccountIsolationTests` separa os dois casos (anônimo →
  401, outra Account → 404) e confere que o 401 é igual para ID existente e
  inexistente.
- **DA-110 — Os pedidos ao owner do limaj são redigidos no repositório e
  enviados pelo usuário** (consenso, Q4). Este run escreve os dois pedidos
  (`ErrorType.BusinessRule` na 4.0.0; tirar o principal do `Abstractions` e
  aceitar um principal sem usuário) como texto pronto em `docs/reports/`. Abrir
  issue em outro repositório, com a identidade do usuário, é ação externa que
  só ele autoriza. A aceitação registrada na DA-099 cobria os pedidos que
  levaram à 3.0.0, e não estes. Nenhum dos dois bloqueia o E2.
- **DA-111 — Um único `LifeGraphDbContext`, com o modelo contribuído pelos
  módulos, e migrations num projeto `LifeGraph.Migrations`** (consenso, Q5). As
  entidades do grafo ficam em `LifeGraph.Graph` (domínio sem framework, regra
  `DomainIsFrameworkFree`), e o mapeamento fluente fica na camada de
  persistência do módulo, como `IEntityTypeConfiguration` registrados por um
  `IModelContributor` em DI. A factory de design-time e todas as migrations,
  incluindo as já existentes, vão para `LifeGraph.Migrations`, que referencia
  Infrastructure e os módulos e é o `MigrationsAssembly`. A factory lista os
  contribuidores explicitamente, sem varrer assemblies, e um teste no CI prova
  que o modelo de runtime e o de design-time não têm diferença pendente. Por quê:
  a DA-013 exige a mudança, o ChangeSet e a Provenance numa transação, o que só
  fica simples com um contexto numa conexão. Um contexto por módulo (a) obrigaria
  a compartilhar conexão e transação e a escrever FK para `accounts` em SQL cru;
  pôr o grafo na Infrastructure (c) tiraria o domínio do módulo (BE-001). O
  histórico de migrations guarda o ID e não o namespace, então a mudança é
  segura enquanto só há dev. Os comandos `dotnet ef` do CLAUDE.md passam a usar
  `--project src/LifeGraph.Migrations`. Candidata a ADR.
- **DA-112 — O módulo Changes é incorporado ao Graph; módulos só dependem de
  `*.Contracts` de outro módulo** (consenso, Q6; revê a DA-002). O Graph é dono
  de GraphChangeSet, ChangeEntry, do pipeline único (DA-013), do Undo (DA-020),
  do Delete/Purge (DA-021), do feed de Recent Changes e, no E5, do feed de Agent
  Changes, que é o mesmo feed filtrado por AgentIdentity. O módulo Changes sai
  da solução e da lista de módulos dos testes de arquitetura. Por quê: o
  ChangeSet é a unidade de mutação do grafo; o Undo lê `change_entries`,
  detecta conflito contra o estado atual e escreve pelo mesmo pipeline, e o
  Purge apaga dos dois lados, então separá-los recria o ciclo em cada fluxo. A
  DA-002 também proibia qualquer referência entre módulos, o que não convive com
  a DA-013, já que Resources (E7), Collections (E8) e Agents (E5) escrevem pelo
  pipeline. Agora um módulo pode depender só de `LifeGraph.<Módulo>.Contracts`
  de outro, nunca do resto; um teste canário prova que nada fora de `Contracts`
  é alcançável. A porta de escrita fica em `LifeGraph.Graph.Contracts` e carrega
  na assinatura o principal (Human ou AgentIdentity), o canal (UI, API, MCP,
  import) e a fonte, para que nenhum módulo crie ChangeSet sem Provenance.
  Candidata a ADR.
- **DA-113 — A fila de jobs tem RLS, e o worker reivindica jobs por uma função
  `SECURITY DEFINER`** (consenso, Q7). A tabela `jobs` tem `account_id` e a
  policy de isolamento, como toda tabela de Account. `app.claim_due_jobs(limit)`
  pertence ao `lifegraph_migrator`, roda um único `UPDATE … FOR UPDATE SKIP
  LOCKED` que marca os jobs devidos como `running` com lease (e recupera leases
  vencidos) e devolve só `(job_id, account_id, kind)`, nunca o payload (na
  revisão, `app.claim_due_jobs(limit, max_attempts, lease)` passou a devolver
  também o `status`, para o runner registrar o job que ela manda para `dead` por
  lease vencido, BE-036; tentativas e lease vêm de `Job`). Tem
  `search_path` fixo, `REVOKE ALL FROM PUBLIC` e `EXECUTE` só para
  `lifegraph_app`. Cada job roda em `InAccountTransactionAsync(account_id)`, e
  marcar `done` ou `dead` acontece nessa mesma transação. Regras do purge: no
  momento de rodar, ele confere de novo que a entidade ainda é tombstone e que o
  `deleted_at` bate com o do job; se não, termina sem efeito (um restore antes do
  prazo cancela o purge sem passo extra). Apagar de novo cria um job novo, com
  prazo novo. A exclusão da Account (E11) descarta os jobs pendentes dela, sem
  mandá-los para `dead`. Por quê: sem RLS (c), um bug da app poderia enfileirar
  um job com o `account_id` de outra Account e o worker apagaria dados dela para
  sempre; um papel de worker (b) põe outra credencial no mesmo processo sem
  isolar mais. A fila também mostra quando e quanto cada Account apaga, que é
  metadado de comportamento. Testes: o papel da app não vê jobs de outra
  Account; a reivindicação devolve jobs de duas Accounts; dois workers nunca
  pegam o mesmo job; lease vencido é reivindicado de novo; insert com
  `account_id` diferente do atual é recusado. O E7 e o E9 reusam o mecanismo.
  Candidata a ADR.
- **DA-114 — O Purge limpa `changesets.source` só quando todas as entradas do
  ChangeSet foram purgadas** (consenso, Q8). Quando a última entrada de um
  GraphChangeSet perde o conteúdo, o `source` dele é limpo na mesma transação do
  purge. Vale também para ChangeSets de Undo, e a exclusão da Account limpa
  tudo. Por quê: limpar ao purgar qualquer entidade (a) apagaria a Provenance
  obrigatória de entidades ainda vivas, e nunca limpar (c) guardaria dado
  pessoal sobre conteúdo que já não existe. É também a opção que dá para
  apertar depois. Risco registrado: o próprio `source` pode revelar o conteúdo
  purgado (por exemplo um nome de arquivo como "exame-HIV.pdf") enquanto outra
  entrada do mesmo ChangeSet estiver viva. Se o usuário quiser que "apagar de
  vez" cubra esse caso, a regra é apertada depois.
- **DA-115 — Semântica de Undo e restauração** (consenso, Q9, depois de um
  contra-argumento). Registra o que a Fase 2 implementou:
  - Restaurar dentro de 30 dias é desfazer o ChangeSet do Delete. Depois do
    prazo, ou se a entidade já foi purgada, a resposta é
    `graph.undo_window_closed`. O prazo conta do `deleted_at` da entidade.
    Depois do prazo, o purge sempre ganha.
  - Isso só vale porque, no E2, um ChangeSet de Delete tem uma única raiz (um
    Node e as Relations que caem com ele). A ChangeEntry de cada Relation
    removida em cascata guarda `cascade_of`, apontando para a entrada da raiz,
    já no E2, para que um restore por entidade possa remontar o conjunto depois.
  - Uma Relation em cascata cuja outra ponta não está mais ativa não bloqueia a
    volta do Node: ela continua tombstone, e o resultado informa quais Relations
    não voltaram. Só conflito na raiz recusa a restauração, e a recusa diz qual
    entrada conflitou.
  - Desfazer um Undo funciona como refazer, com detecção de conflito. Um refazer
    que apaga de novo cria tombstone novo, com `deleted_at` novo e job novo. Um
    ChangeSet que tocou entidade já purgada não pode ser desfeito nem refeito, e
    a recusa tem código próprio. O feed mostra a cadeia entre o original
    (Reverted) e o Undo.
  - Desfazer a criação de um Node deixa um tombstone, sujeito ao purge de 30
    dias.
  - Desfazer a criação de um Type ou Property Definition apaga a linha pelo
    pipeline. Isso é recusado quando algo fora desse Undo ainda o usa, o que
    inclui tombstones deixados por outro Delete. Assim, restaurar um Node nunca
    deixa referência para definição inexistente. Faltam dois testes: refazer
    esse Undo traz de volta o Type e o `TypeId` do Node; e o purge de um Node
    sem Type apaga o conteúdo das entradas.
  - **Antes do E5:** o E5 e qualquer ação em massa criam ChangeSets com Delete
    misturado a outras edições, e então desfazer o ChangeSet inteiro deixaria de
    servir como restauração. Por isso o E5 depende de um comando Restore por
    entidade: ChangeSet compensatório próprio, conflito checado só na entidade e
    nas Relations em cascata (`cascade_of`), mesmo scope do Delete
    (`lifegraph.delete`, nível Sensitive para agente), NotFound para outra
    Account ou depois do purge. Candidata a ADR (complementa a ADR 0005 e a
    DA-020).
- **DA-116 — Rate limiting por principal nas rotas autenticadas da API**
  (consenso, Q10; cumpre a API-082). O `RateLimiter` do ASP.NET Core é aplicado
  ao grupo `/api` do grafo, particionado pelo principal (no E2, o Human da
  sessão; sem principal, pelo IP, embora a DA-109 responda 401 antes). São duas
  policies em options: leitura, com janela folgada e dimensionada para o
  polling e o refetch da própria SPA (DA-024); e métodos inseguros, incluindo
  Undo e arquivar, mais apertada, mas sempre acima do que um humano alcança na
  UI. A recusa é 429 com `Retry-After` e o código comum `too_many_requests`
  (sem prefixo, DA-105), escrita no `OnRejected` como no E1. O
  `too_many_attempts` do E1 continua só para credenciais. O corpo da requisição
  ganha um teto explícito global de cerca de 256 KB, que endpoints como os de
  Resources podem subir. Por quê: (b), só escritas, deixaria abertas as leituras
  que ficam caras (travessia, `get_context`); (c), exceção à API-082, gastaria
  uma exceção a uma DEVE para economizar pouca configuração. O limite protege
  contra abuso e **não** é limite comercial: é igual em todo plano e nunca vira
  quota Free/Premium. A chave é o principal, e não só a Account, para que no E5
  um agente falante não tire do humano o acesso ao próprio grafo, inclusive ao
  Undo do que o agente fez. Os nomes das policies e o resolvedor de partição
  ficam num lugar só, para o E5 reusar com a chave da AgentIdentity. Os
  contadores ficam em memória, o que vale enquanto houver uma única instância.
  Testes: N+1 requisições → 429 com `Retry-After` e `code`; duas Accounts não
  dividem a cota.
- **DA-117 — Contrato de versão do Node: `version` no corpo ou na query e 409
  no conflito** (consenso, Q12; desvio justificado da API-084, DEVERIA). O
  PATCH leva `version` no corpo e o DELETE na query, e o conflito é 409
  `graph.node_version_conflict`, em vez de `ETag`/`If-Match` com 412. Por quê:
  a DA-013 faz REST, escritas em lote e MCP dividirem o mesmo contrato de
  escrita. O MCP não tem headers, e o lote leva uma versão por item. Um ETag só
  na borda REST seria uma segunda representação da mesma coisa. Toda leitura de
  Node, o recibo da escrita e o corpo do 409 devolvem a `version` atual, para o
  cliente nunca precisar adivinhá-la, e o 409 diz que a cópia do cliente está
  desatualizada e que basta reler. `version` versiona a representação inteira
  do Node, incluindo o estado de Inbox e de tombstone (complementa a DA-022).
  Arquivar não pede versão, mas incrementa a `version`. Arquivar um Node que já
  saiu da Inbox não grava ChangeEntry nem muda a versão, e arquivar tombstone
  responde NotFound. Apagar um Type ou Property Definition em uso por tombstones
  (DA-115) é recusado com código próprio (422), dizendo quantos Nodes apagados
  ainda o usam e quando o último será purgado. Renomear continua permitido.
  Testes: PATCH com a versão anterior ao arquivamento recebe 409; desfazer o
  arquivamento depois de outra edição é recusado por conflito; desfazer o
  arquivamento devolve `inboxEnteredAt` e incrementa a versão. Candidata a ADR
  (vale para todo endpoint futuro e para o MCP do E5).
- **DA-118 — Interfaces dos módulos de frontend do grafo** (consenso, Q11, via
  `design-an-interface`: quatro desenhos, o arquiteto escolheu e a analista
  validou depois de um contra-argumento). Em `web/src/features/graph/`:
  - **Dados do grafo:** query options por recurso (`nodesQuery`, `nodeQuery`,
    `nodeRelationsQuery`, `typesQuery`, `propertyDefinitionsQuery`,
    `graphKeys`), filtro da lista na URL (`useNodeListFilter`:
    `?type=none&inbox=1`, FE-012) e um hook de mutação nomeado por ação
    (`useUpdateNode(id)`, `useArchiveNode(id)`, `useDeleteType(id)`…), como
    `useLogin`. A versão enviada é capturada no início da edição
    (`useEditBaseline`): um refetch nunca reseta um formulário alterado nem
    troca a versão dele. Se o Node mudou, o formulário avisa antes de salvar, e
    salvar com a base antiga recebe o 409, sem sobrescrever nada. Depois de
    escrever, invalida `graphKeys.all` e o feed; no 409, relê o Node, sem
    retry automático. `toSchemaRefusal` mapeia `type_in_use`, `property_in_use`
    e `property_has_values`. O Inspector tem os estados carregando, pronto,
    `gone` (404 de um Node já mostrado na sessão, com link para desfazer em
    Recent Changes), não encontrado e erro.
  - **Recent Changes e Undo:** `useRecentChanges()` com páginas antigas por
    `before` e uma consulta de sondagem por `since=latestCursor` (sem `since`
    quando o cursor é nulo, nunca pulando), no foco e a cada ~30 s com a aba
    visível, respeitando o `Retry-After`. Itens novos ligam `hasNewer` e não
    entram na lista sozinhos (foco e rolagem ficam no lugar), mas invalidam
    `graphKeys.all`. `useUndoChangeSet()` é uma mutação comum;
    `toUndoRefusal` cobre conflito (com as entradas, ou mensagem genérica sem
    detalhes), `in_use`, `already_reverted`, `window_closed`,
    `content_purged` e `not_found`. As Relations que não voltaram aparecem pelo
    rótulo. Cada item mostra quem, por qual canal, de qual fonte e quando, e o
    estado Reverted vem do próprio item.
  - **`PropertyValueEditor`:** componente controlado (`value`/`onChange`/
    `error`) usado dentro do `Controller` do React Hook Form, dono do label, do
    erro e dos `aria-*` nos 8 tipos (`fieldset`/`legend` no multi-select,
    "Limpar" no booleano), com Select do shadcn. `PropertyValueDisplay` mostra
    Outras propriedades. `toWire`/`fromWire` copiam as regras de
    `PropertyDefinition.Normalize`. Opção removida aparece como "(opção
    removida)", desabilitada e removível.
  - O frontend não depende do corpo dos 409 e 422: sem detalhes, mostra a
    mensagem genérica. O backend deve um teste do corpo HTTP desses erros
    (`entries[n]` no conflito de Undo, `version` no 409 de versão, contagem e
    data do último purge no 422 de Type/Definition em uso), como a DA-115 e a
    DA-117 prometem. Se o formato da DA-102 tirar esses detalhes, a correção
    sai pelo mapper do produto ou vira pedido ao owner do limaj, sem mudar a
    opção global.
  Por quê: a versão vinda do cache ou do refetch apagaria em silêncio a mudança
  de um agente; um hook por ação mantém o estado de pendência de cada uma
  (FE-022) e o padrão da casa; o componente controlado é o contrato mais fácil
  de testar (FE-063) e garante a FE-033 nos 8 tipos.

## Estrutura por camada

- **Domain:** Node, Relation, Type, PropertyDefinition, GraphChangeSet,
  ChangeEntry, Provenance; invariantes e validação por Type.
- **Application:** comandos de escrita (um caminho único), Undo, Delete,
  arquivar da Inbox, queries de lista, Inspector e feed.
- **Infrastructure:** EF Core + Npgsql, JSONB, worker de purge
  (`BackgroundService` + fila em Postgres com `FOR UPDATE SKIP LOCKED`) e
  tabela de outbox/jobs.
- **Web:** Lista, Inspector (incluindo Outras propriedades), Recent Changes,
  Undo, Inbox mínima e editor mínimo de Types/Properties.

## Checklist

### Fase 0 — Pré-requisitos
- [x] `/flow` antes de implementar: decidir os itens 1–3 do preâmbulo (DA-107 a DA-109)
- [x] Revalidar as pré-decisões do contrato de erro contra o limaj 3.0.0 publicado e decidir a Q6 (DA-100 a DA-106)
- [x] Confirmar que a 3.0.0 de `Core` e `Web` está listada no nuget.org e referenciar os dois numa única propriedade de versão (DA-100)
- [x] Testes de arquitetura e de dependências do consumo do limaj; Dependabot agrupando os `Limaj.*` e ignorando major (DA-100, DA-102, DA-104)
- [x] `LifeGraph.Http`: `AddLimajHttpErrors` com `V3`, `IncludeExceptionDetails = false` e `IncludeDetailsOutsideValidation = false` explícitos; `IErrorHttpMapper` do produto; catálogo de códigos com status e classe de recuperação (DA-101, DA-102, DA-105)
- [x] Primeira fatia: migrar o E1 (`ApiProblems`, `CredentialProblems`, CSRF, limitador com `TooManyRequests` + `RetryAfter`) para o mapper, renomeando os códigos para `accounts.*` e revendo as mensagens como texto público (DA-103, DA-105)
- [x] Testes: 500 genérico com Development forçado; toda resposta de erro tem `code`; todo código está no catálogo; `Retry-After` igual ao de hoje (DA-101, DA-102)
- [x] Enviar ao owner do limaj os pedidos de `ErrorType.BusinessRule` (4.0.0) e do principal fora do `Abstractions` (DA-101, DA-106) — redigidos em [`docs/reports/2026-10-05-limaj-pedidos-ao-owner.md`](../../reports/2026-10-05-limaj-pedidos-ao-owner.md); envio pelo usuário (DA-110)
- [x] Papel `lifegraph_provisioner`: policy `accounts_provisioning` só `TO lifegraph_provisioner`, grants mínimos, `ConnectionStrings__Provisioning` só na CLI, papel criado no bootstrap (devcontainer, CI e fixtures) e testes de guarda (DA-107)
- [x] `accounts resend` troca o security stamp antes de enviar; teste de que o link antigo falha e o novo funciona (DA-108)
- [x] Sem sessão: 401 com `WWW-Authenticate` em todas as rotas; testes separando anônimo (401) de outra Account (404) e 401 igual para ID existente e inexistente (DA-109)

### Fase 1 — Modelo e pipeline
- [x] Um único `LifeGraphDbContext` com o modelo contribuído pelos módulos (`IModelContributor` em DI, `IEntityTypeConfiguration` na persistência do módulo); projeto `LifeGraph.Migrations` com a factory de design-time e todas as migrations; teste de que o modelo de runtime e o de design-time não têm diferença pendente; comandos `dotnet ef` e scripts atualizados (DA-111)
- [x] Módulo Changes incorporado ao Graph e removido da solução; módulos só dependem de `LifeGraph.<Módulo>.Contracts` de outro, com teste canário; porta de escrita em `LifeGraph.Graph.Contracts` com principal, canal e fonte (DA-112)
- [x] Entidades e migrations (nodes, relations, types, property_definitions, type_properties, changesets, change_entries)
- [x] Comando único de escrita com ChangeSet + Provenance na mesma transação
- [x] Validação de valores por Type; comando inválido não persiste nada
- [x] Concorrência otimista por Node

### Fase 2 — Reversibilidade
- [x] Undo compensatório com detecção de conflito
- [x] Delete como tombstone + restauração dentro de 30 dias
- [x] Job de purge (fim da janela) apagando o conteúdo dos ChangeSets
- [x] Tabela de jobs/outbox e worker em processo
- [x] Purge limpa `changesets.source` quando todas as entradas do ChangeSet foram purgadas, inclusive de Undo (DA-114)
- [x] `IAccountPurgeParticipant` do Graph descarta os jobs pendentes da Account e apaga os dados do grafo (DA-012, DA-113)
- [x] ChangeEntry de cada Relation em cascata guarda `cascade_of` apontando para a entrada da raiz (DA-115)
- [x] Relation em cascata com a outra ponta inativa não bloqueia a volta do Node; o resultado lista as Relations que não voltaram; a recusa por conflito na raiz nomeia a entrada (DA-115)
- [x] ChangeSet que tocou entidade já purgada não pode ser desfeito nem refeito, com código próprio (`graph.changeset_content_purged`); refazer um Delete cria tombstone, `deleted_at` e job novos (DA-115)
- [x] Testes que faltam da DA-115: refazer o Undo da criação de um Type devolve o Type e o `TypeId` do Node; o purge de um Node sem Type apaga o conteúdo das entradas

### Fase 3 — Types e Inbox
- [x] CRUD mínimo de Types e Property Definitions (8 tipos), anexar e desanexar
- [x] Troca de Type move valores para Outras propriedades e os restaura pelo `property_id`
- [x] Estado de Inbox + comando "arquivar"
- [x] Bloquear mudança de tipo de propriedade que já tem valores
- [x] Rate limiting por principal no grupo `/api` do grafo (policies `api.read` e `api.write` em options, partição pelo principal), 429 `too_many_requests` com `Retry-After`, teto de corpo de 256 KB que um endpoint pode subir; testes de N+1 → 429 e de duas Accounts com cotas separadas (DA-116)
- [x] `version` nas leituras de Node, no recibo e no corpo do 409; arquivar sem versão incrementa a `version`, não grava nada quando o Node já saiu da Inbox e responde NotFound em tombstone; Type/Definition em uso só por tombstones recusado com código próprio (`graph.type_in_use_by_deleted_nodes`, `graph.property_in_use_by_deleted_nodes`, 422) com `deletedNodeCount` e `lastPurgeAt`; testes da DA-117
- [x] Testes do corpo HTTP do 409 (`version`, `entries[n]`) e do 422 (contagem e data do último purge); o formato da DA-102 tirava os detalhes do 409, corrigido pelo mapper do produto (códigos com `WithPublicDetails`), sem mudar a opção global (DA-118)

### Fase 4 — UI mínima
- [x] Lista com filtro "Sem Type", Inspector, Recent Changes com Undo, Inbox
- [x] Feed de ChangeSets com cursor + refetch on focus

### Testes obrigatórios
- [x] Atomicidade: uma falha no meio do comando não deixa ChangeSet nem dados parciais
- [x] Undo com e sem conflito
- [x] Ida e volta de Type preserva os valores
- [x] Purge apaga o conteúdo do histórico
- [x] Isolamento cross-account em todas as tabelas novas

## Critérios de saída

Um humano cria, edita, relaciona, apaga e desfaz pela UI. Toda alteração
aparece em Recent Changes com proveniência.

## Fora de escopo

Agentes (E3–E5), grafo 2.5D (E6), Resources (E7), Tags/Collections e editor
completo (E8), embeddings (E9).
