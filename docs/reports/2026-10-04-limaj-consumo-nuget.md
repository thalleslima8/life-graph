# Consumo do limaj-framework: feed NuGet e mapeamento HTTP

- **Data:** 2026-10-04
- **Origem:** `/flow` de 2026-10-04 (branch `flow/2026-10-04`), depois do commit
  do E1 (`4aebdff`)
- **Afeta:** DA-005 (E0), DA-094 (E1) e o início do E2
- **Estado:** superado em 2026-10-04. O limaj 2.0.0 saiu no nuget.org (net10.0,
  MIT), o owner aceitou os pedidos de evolução, e o usuário decidiu padronizar no
  limaj (DA-099). O que segue em aberto está no E2, seção "Contrato de
  resultado e erro". Este relatório fica como registro histórico.

## Resumo

A DA-005 manda consumir o `Abstractions` (Result/Error, exceções,
`IUserIdentityGateway`) e o `Web` (ResultExtensions, RequestRunner) do
limaj-framework como pacotes versionados. Os pacotes existem, mas há dois
problemas:

1. **Bloqueante de consumo:** o feed onde estão publicados exige autenticação, e
   o life-graph é um repositório público.
2. **Incompatibilidade de contrato:** o `Limaj.Framework.Web` produz Problem
   Details num formato que viola API-051 e API-030 e não é extensível. A saída
   possível é usar só o `Abstractions` e manter o mapeamento HTTP no life-graph.

Nenhum dos dois impede tecnicamente o E2. Os dois precisam de decisão antes do
primeiro caso de uso do pipeline de escrita (DA-013), porque o tipo de retorno
dos casos de uso vira o contrato de todo o backend.

## Correção de uma análise anterior

Durante o E1, o `/spike` e depois o `/flow` analisaram
`thalleslima8/limaj-framework`, uma cópia **privada e antiga** (último push em
2026-06-05). Ela não tem pacote `Web` e não tem nada publicado. O repositório
vigente é **`limajsolutions/limaj-framework`**, público, com push em
2026-09-20.

Consequências:

- A DA-094 (E1) cita "repo privado, net9.0, sem feed, sem pacote `Web`" como
  contexto, e isso é falso para o repositório vigente. A decisão continua
  válida pelo motivo principal: `IUserIdentityGateway` só devolve um user id
  `string?`, sem `account_id` nem tipo de principal. **O texto da DA-094 precisa
  ser corrigido.**
- Uma descrição de requisitos escrita a partir da cópia errada não deve ser
  usada. A versão correta está na seção "Pedido ao limaj-framework" abaixo.

## Fatos verificados (repositório `limajsolutions/limaj-framework`)

| Item | Situação | Como foi verificado |
|---|---|---|
| Pacotes publicados | `Abstractions`, `Web`, `Application`, `Persistence.EFCore` no GitHub Packages da conta `limajsolutions`, visibilidade pública | `gh api "users/limajsolutions/packages?package_type=nuget"` |
| Versões | `1.0.0` (estável), `1.0.1-alpha.0.2` (pré-release), via MinVer | `gh api users/limajsolutions/packages/nuget/<pacote>/versions` |
| Feed | `https://nuget.pkg.github.com/limajsolutions/index.json` | `.github/workflows/publish-packages.yml` |
| Leitura anônima do feed | **401** no índice e no download | `curl -s -o /dev/null -w "%{http_code}" https://nuget.pkg.github.com/limajsolutions/index.json` |
| nuget.org | nenhum pacote `Limaj.*` | busca em `azuresearch-usnc.nuget.org/query?q=Limaj` |
| Target | `net9.0` nos dois pacotes; pipeline com SDK `9.0.x` | `.csproj` e workflow |
| `Web` depende de Azure Functions? | Não: só `Abstractions` + `Microsoft.AspNetCore.App` | `.csproj` |
| `Abstractions` traz persistência? | Sim: `BaseEntity` (soft delete `IsActive`), `IAudit`, `IRepository`, `IUnitOfWork` | árvore `packages/Limaj.Framework.Abstractions/src` |
| Configuração no life-graph | não há `nuget.config`; o Dependabot já monitora `nuget` | raiz do repositório e `.github/dependabot.yml` |

## Problema 1: o feed exige autenticação

O GitHub Packages exige token para restaurar pacotes NuGet, mesmo quando os
pacotes são públicos. O life-graph é público e o CI roda em `pull_request`.
Consumir os pacotes como estão implica:

- **Local e devcontainer:** cada clone precisa de um `nuget.config` com o feed e
  de um PAT com `read:packages` (por variável de ambiente, nunca no repositório).
- **CI:** um secret com PAT `read:packages`. O `GITHUB_TOKEN` do workflow
  provavelmente não lê pacotes de outro dono (`limajsolutions` ≠
  `thalleslima8`). **Não testado.**
- **PRs de fork:** rodam sem secrets e quebram no `restore`.
- **Dependabot:** só funciona com o mesmo PAT cadastrado também como secret do
  Dependabot e um `registries` no `dependabot.yml`.
- **Quem clona o repositório público** não compila sem criar um token.

### Saídas

| Opção | O que exige | Custo |
|---|---|---|
| A. Aceitar o feed autenticado agora | `nuget.config` com o feed e o nuget.org; PAT no CI, no Dependabot e no devcontainer; documentar no README | PRs de fork quebram; atrito para quem clona; mais um segredo para gerir (GEN-050) |
| B. Esperar o limaj publicar no nuget.org | Mudança no `publish-packages.yml` do limaj | O E2 espera, ou começa sem o pacote e troca depois |
| C. Copiar `Result`/`Error` para a solução | Um projeto pequeno (ex. `LifeGraph.SharedKernel`) e uma DA registrando a origem e a versão copiada | Fork manual; divergência com o limaj com o tempo |
| D. Dispensar o limaj | Revisar a DA-005; `Result`/`Error` próprios | Perde o reuso que motivou a DA-005 |

**Decisão do usuário.** Envolve acesso a um framework de outra conta e o risco
aceitável no CI de um repositório público.

## Problema 2: o `Limaj.Framework.Web` não segue o contrato de erro do projeto

### O que o pacote faz

- `ResultExtensions.MapError` coloca `Error.Code` em `detail` e `Error.Message`
  em `title`. `ValidationProblem` não leva código.
- `TooManyRequests` vira 429 sem `Retry-After`, e não há como passar o valor.
- 422 só sai pela escotilha `Error.HttpStatusCode`, sem `ErrorType` próprio.
- `RequestRunner` → `ExceptionExtensions.ToHttpResult` → o mesmo
  `ResultExtensions`. Todo erro capturado sai no mesmo formato.
- `ExceptionExtensions` lê `ASPNETCORE_ENVIRONMENT` para decidir se expõe
  `ex.Message`. É o mesmo padrão que o review do E1 barrou por GEN-051 (achado
  F6).

### O que o projeto exige

- **API-051 (DEVE):** código de erro estável em `code`/`type`. O E1 usa a
  extensão `code` ([ApiProblems.cs](../../src/LifeGraph.Accounts/Http/ApiProblems.cs)),
  e a SPA decide por ela (DA-097).
- **API-030 (DEVE):** 422 para regra de negócio com entrada válida; 429 com
  `Retry-After`.
- **GEN-051 (DEVE):** nada de comportamento decidido pelo nome do ambiente.

### Por que não dá para estender o pacote

- `ResultExtensions` é estática e `MapError` é `private`: sem herança nem
  override.
- `IExceptionToErrorMapper` só escolhe **qual** `Error` produzir, não **como** a
  resposta é escrita.
- Um ajuste por fora via `CustomizeProblemDetails` não é confiável: 404 e 409
  saem como `Results.NotFound(ProblemDetails)` e `Results.Conflict(...)`, que
  não passam pelo `IProblemDetailsService`.

### Extensão possível

- **Referenciar só o `Limaj.Framework.Abstractions`**, que contém `Result`,
  `Result<T>`, `Error`, `ErrorType`, as exceções de domínio e o
  `IExceptionToErrorMapper`.
- **Escrever o mapeamento HTTP no life-graph**, como extensões sobre os tipos
  do limaj, partindo do `ApiProblems` do E1:
  - `Result` → `IResult`, com a extensão `code` em toda resposta, inclusive
    validação;
  - 422 via `ErrorType` ou `HttpStatusCode`;
  - `Retry-After` no 429;
  - runner equivalente, com o ambiente vindo de opção validada (GEN-051/052).
- **Não referenciar o `Web`**, e barrar com um teste de arquitetura. Se ele
  estiver disponível, alguém pode chamar o `ToHttpResult` dele e produzir o
  formato errado.
- **Barrar `BaseEntity`, `IRepository` e `IUnitOfWork`** nos agregados do grafo
  com teste de arquitetura, como a DA-005 já prevê.

Essa extensão resolve o problema 2 sem depender do limaj. **Não resolve o
problema 1:** o `Abstractions` também está no feed autenticado.

**Decisão de desenho**, que deve passar pelo arquiteto e pelo analista num
`/flow` antes do E2. Candidata a revisar a DA-005 para "só `Abstractions`;
mapeamento HTTP local".

## Pedido ao limaj-framework

Para levar ao repositório do limaj. Está escrito sem referências ao
life-graph.

> **Consumidor:** aplicação ASP.NET Core em .NET 10 (Minimal APIs), com
> repositório público, consumindo `Abstractions` e `Web`.
>
> 1. **(Bloqueante) Leitura anônima.** O feed do GitHub Packages responde 401
>    sem credencial, mesmo com os pacotes públicos. Publicar também no nuget.org
>    (ou outro feed com leitura anônima), para quem clona, PRs de fork e
>    Dependabot.
> 2. **Target `net10.0`** (multi-target `net9.0;net10.0` serve) e SDK 10 no
>    pipeline.
> 3. **Contrato de Problem Details no `Web`:**
>    - `code` como extensão em toda resposta de erro, inclusive validação, e não
>      em `detail`;
>    - `Retry-After` no 429;
>    - `ErrorType` para 422;
>    - um ponto de extensão para customizar o mapeamento `Result` → `IResult`
>      sem fork.
> 4. **Ambiente por configuração.** `ExceptionExtensions` não deve ler
>    `ASPNETCORE_ENVIRONMENT`; receber por opção.
> 5. **Separar o núcleo de resultado** (`Result`/`Error`/exceções/
>    `IExceptionToErrorMapper`) das abstrações de domínio e persistência
>    (`BaseEntity`, `IAudit`, `IRepository`, `IUnitOfWork`).
> 6. **(Opcional)** `IUserIdentityGateway` só devolve um user id `string?`. Um
>    principal tipado e extensível serviria mais consumidores.

Se o limaj atender 1 e 3, o life-graph pode usar o `Web` diretamente e a
extensão local deixa de ser necessária.

## Próximos passos

1. **Usuário:** escolher a saída do problema 1 (A, B, C ou D) e, se quiser,
   abrir o pedido no limaj-framework.
2. **Próximo `/flow` (antes do E2):** rodar com arquiteto e analista:
   - a revisão da DA-005 (problema 2: só `Abstractions` + mapeamento local, ou
     esperar o `Web` corrigido);
   - a correção do contexto da DA-094;
   - as três pendências herdadas do E1, que estão no preâmbulo do E2.
3. **Registrar** as decisões a partir de `DA-099`. Candidata a ADR: o contrato
   único de erro HTTP (onde vive o mapeamento e por que não vem do framework).
4. **Retomar** o run `flow/2026-10-04`: push do E1, PR em rascunho, CI, e então
   o E2.
