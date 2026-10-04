# E2 — Núcleo de escrita do grafo

- **Ordem:** 2
- **Depende de:** E1
- **Última revisão:** 2026-10-04

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

A Q9 (ação Sensitive de agente responde como sucesso pendente) e a Q4b (em que
ordem checar scope e visibilidade) estão no início do
[E5](e05-mcp-escrita-agent-changes.md).

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
- [ ] `/flow` antes de implementar: decidir os itens 1–3 do preâmbulo
- [x] Revalidar as pré-decisões do contrato de erro contra o limaj 3.0.0 publicado e decidir a Q6 (DA-100 a DA-106)
- [ ] Confirmar que a 3.0.0 de `Core` e `Web` está listada no nuget.org e referenciar os dois numa única propriedade de versão (DA-100)
- [ ] Testes de arquitetura e de dependências do consumo do limaj; Dependabot agrupando os `Limaj.*` e ignorando major (DA-100, DA-102, DA-104)
- [ ] `LifeGraph.Http`: `AddLimajHttpErrors` com `V3`, `IncludeExceptionDetails = false` e `IncludeDetailsOutsideValidation = false` explícitos; `IErrorHttpMapper` do produto; catálogo de códigos com status e classe de recuperação (DA-101, DA-102, DA-105)
- [ ] Primeira fatia: migrar o E1 (`ApiProblems`, `CredentialProblems`, CSRF, limitador com `TooManyRequests` + `RetryAfter`) para o mapper, renomeando os códigos para `accounts.*` e revendo as mensagens como texto público (DA-103, DA-105)
- [ ] Testes: 500 genérico com Development forçado; toda resposta de erro tem `code`; todo código está no catálogo; `Retry-After` igual ao de hoje (DA-101, DA-102)
- [ ] Enviar ao owner do limaj os pedidos de `ErrorType.BusinessRule` (4.0.0) e do principal fora do `Abstractions` (DA-101, DA-106)

### Fase 1 — Modelo e pipeline
- [ ] Entidades e migrations (nodes, relations, types, property_definitions, type_properties, changesets, change_entries)
- [ ] Comando único de escrita com ChangeSet + Provenance na mesma transação
- [ ] Validação de valores por Type; comando inválido não persiste nada
- [ ] Concorrência otimista por Node

### Fase 2 — Reversibilidade
- [ ] Undo compensatório com detecção de conflito
- [ ] Delete como tombstone + restauração dentro de 30 dias
- [ ] Job de purge (fim da janela) apagando o conteúdo dos ChangeSets
- [ ] Tabela de jobs/outbox e worker em processo

### Fase 3 — Types e Inbox
- [ ] CRUD mínimo de Types e Property Definitions (8 tipos), anexar e desanexar
- [ ] Troca de Type move valores para Outras propriedades e os restaura pelo `property_id`
- [ ] Estado de Inbox + comando "arquivar"
- [ ] Bloquear mudança de tipo de propriedade que já tem valores

### Fase 4 — UI mínima
- [ ] Lista com filtro "Sem Type", Inspector, Recent Changes com Undo, Inbox
- [ ] Feed de ChangeSets com cursor + refetch on focus

### Testes obrigatórios
- [ ] Atomicidade: uma falha no meio do comando não deixa ChangeSet nem dados parciais
- [ ] Undo com e sem conflito
- [ ] Ida e volta de Type preserva os valores
- [ ] Purge apaga o conteúdo do histórico
- [ ] Isolamento cross-account em todas as tabelas novas

## Critérios de saída

Um humano cria, edita, relaciona, apaga e desfaz pela UI. Toda alteração
aparece em Recent Changes com proveniência.

## Fora de escopo

Agentes (E3–E5), grafo 2.5D (E6), Resources (E7), Tags/Collections e editor
completo (E8), embeddings (E9).
