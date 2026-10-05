# Pedidos ao owner do limaj-framework (4.0.0)

- **Data:** 2026-10-05
- **Origem:** E2, Fase 0 (DA-101, DA-106, DA-110)
- **Estado:** redigidos. O envio é do usuário (DA-110): abrir issue em outro
  repositório, com a identidade dele, é uma ação externa que só ele autoriza.
- **Destino:** `limajsolutions/limaj-framework`, uma issue por pedido. Os textos
  abaixo estão prontos para colar.

Nenhum dos dois pedidos bloqueia o E2. O life-graph consome só
`Limaj.Framework.Core` e `Limaj.Framework.Web` 3.0.0 (DA-100) e contorna os dois
pontos como descrito em cada issue.

---

## Issue 1: `ErrorType.BusinessRule` (422) na 4.0.0

**Título:** `ErrorType.BusinessRule` mapeado para 422 na 4.0.0

**Corpo:**

> **Contexto.** O plano da 3.0.0 trazia `ErrorType.BusinessRule` mapeado para
> 422 e a factory `Result.BusinessRule(...)`. A 3.0.0 saiu sem eles: o enum tem
> `Validation`, `NotFound`, `Conflict`, `Forbidden`, `Unauthorized`,
> `Unexpected` e `TooManyRequests`.
>
> **O problema.** Uma regra de negócio violada ("entrada bem formada, regra não
> cumprida") não tem tipo próprio. Hoje há duas saídas, e as duas pioram o
> contrato:
>
> - usar `Validation`, que sai como 400 e se mistura com entrada malformada;
> - usar `Conflict`, que sai como 409 e se mistura com concorrência e
>   unicidade.
>
> **Como contornamos na 3.0.0.** A regra é `ErrorType.Validation`, e o
> `IErrorHttpMapper` do produto herda o `DefaultErrorHttpMapper` e chama
> `MapWithStatusCode(error, 422)` quando o `Error.Code` está num catálogo de
> códigos de regra. Funciona, mas cada produto repete o catálogo, e um código
> esquecido nele sai como 400 sem ninguém perceber.
>
> **Pedido.**
>
> 1. `ErrorType.BusinessRule`, mapeado para 422 no `DefaultErrorHttpMapper`
>    (com um `MapBusinessRule` virtual, como os outros tipos).
> 2. `Result.BusinessRule(code, message)` e `Result<T>.BusinessRule(...)`.
> 3. No formato `V3`, o mesmo corpo de um `Validation` (com `errors` quando
>    houver `Details`).
>
> **Por que numa major.** Um membro novo no enum quebra `switch` exaustivo nos
> consumidores, então cabe na 4.0.0, junto com a remoção já anunciada de
> `Error.HttpStatusCode`.
>
> **Compatibilidade para quem já contorna.** Quem escolhe o status pelo código
> não percebe a troca: o cliente continua recebendo 422 e o mesmo `code`.

---

## Issue 2: principal fora do `Abstractions`, aceitando um principal sem usuário

**Título:** Mover `IUserIdentityGateway`/`UserPrincipal` para um pacote no
nível do `Core` e aceitar um principal sem usuário

**Corpo:**

> **Contexto.** A 3.0.0 trouxe o principal tipado (`UserPrincipal` extensível e
> `GetCurrentPrincipalAsync`), mas ele mora no `Limaj.Framework.Abstractions`.
>
> **O problema.**
>
> 1. **Dependência.** Para usar o gateway, o produto precisa referenciar o
>    `Abstractions`, que traz junto `BaseEntity`, `IRepository` e `IUnitOfWork`.
>    Um produto que, por decisão própria, não usa esses tipos (no nosso caso, o
>    soft delete por `IsActive` conflita com Delete/Purge e com o histórico de
>    alterações) passa a tê-los no grafo de dependências e precisa barrá-los por
>    review ou por teste.
> 2. **Principal sem usuário.** O `UserPrincipal` supõe um usuário. Temos dois
>    principais que não são um usuário: um visitante de link compartilhado (não
>    tem user id, vem de um token de link) e um agente que age em nome de uma
>    conta.
> 3. **Uso síncrono.** O filtro de leitura e a RLS precisam do id da conta e do
>    tipo de principal de forma síncrona, dentro da transação, falhando fechado
>    quando o principal está incompleto. `GetCurrentPrincipalAsync` não atende
>    esse ponto.
>
> **Como contornamos na 3.0.0.** O produto mantém a própria porta
> (`ICurrentPrincipal`, síncrona) e não usa o gateway.
>
> **Pedido.**
>
> 1. Mover o gateway e o principal para um pacote sem dependências no nível do
>    `Core` (o próprio `Core` ou um `Limaj.Framework.Identity`), sem `BaseEntity`
>    nem repositórios.
> 2. Um principal base que não exija usuário (por exemplo, um tipo de principal
>    e um id de conta opcionais, com o usuário como especialização).
> 3. Um acesso síncrono ao principal já resolvido na requisição, além do
>    `GetCurrentPrincipalAsync`.
>
> **Prioridade.** Não bloqueia. Vamos reavaliar a adoção quando os principais de
> agente e de visitante de link existirem no produto.
