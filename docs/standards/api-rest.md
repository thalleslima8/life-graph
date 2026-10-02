# Padrões de API REST (`API`)

Complementam [general.md](general.md) e [backend.md](backend.md) para
contratos HTTP. A API é um produto: seus consumidores dependem de que ela seja
previsível e estável.

## 1. Contrato

**API-001 — DEVE: contrato documentado em especificação** (OpenAPI ou
equivalente), versionado no repositório e atualizado no mesmo PR da mudança.
_Por quê:_ o contrato é a fonte de verdade para consumidores, mocks e testes.

**API-002 — DEVERIA: contract-first para APIs públicas ou entre times.** O
contrato é revisado antes da implementação.
_Por quê:_ mudar contrato depois de consumido é caro.

**API-003 — DEVE: consistência em toda a API.** Mesmos padrões de nomes,
paginação, erros, datas e autenticação em todos os endpoints.
_Por quê:_ quem aprendeu um endpoint deve saber usar os outros.

## 2. Recursos e URLs

**API-010 — DEVE: URLs nomeiam recursos (substantivos), não ações.**
`POST /orders`, não `POST /createOrder`. A ação é o verbo HTTP.
_Por quê:_ é o modelo que clientes, caches e ferramentas HTTP esperam.

**API-011 — DEVE: coleções no plural, em minúsculas, `kebab-case`.**
`/payment-methods`, `/orders/{orderId}/items`.
_Por quê:_ padrão amplamente adotado e sem ambiguidade de maiúsculas.

**API-012 — DEVERIA: aninhamento raso** (no máximo um nível:
`/orders/{id}/items`). Relações mais profundas usam filtros
(`/items?orderId=...`).
_Por quê:_ URLs profundas acoplam o cliente à hierarquia interna.

**API-013 — DEVERIA: ações que não mapeiam para CRUD** viram sub-recurso de
ação com `POST`: `POST /orders/{id}/cancellation` (ou `/cancel`, se o projeto
padronizar assim).
_Por quê:_ mantém a semântica dos verbos sem forçar um `PATCH` artificial.

**API-014 — DEVE: identificadores opacos e não sequenciais** em URLs públicas
(UUID/ULID ou equivalente).
_Por quê:_ IDs sequenciais expõem volume de negócio e facilitam enumeração.

## 3. Métodos HTTP

**API-020 — DEVE: usar os métodos pela semântica correta.**

| Método   | Uso                          | Seguro | Idempotente |
| -------- | ---------------------------- | ------ | ----------- |
| `GET`    | Ler recurso/coleção          | sim    | sim         |
| `POST`   | Criar / disparar ação        | não    | não*        |
| `PUT`    | Substituir recurso inteiro   | não    | sim         |
| `PATCH`  | Atualizar parcialmente       | não    | não*        |
| `DELETE` | Remover                      | não    | sim         |

`GET` nunca altera estado.
_Por quê:_ caches, proxies, retries e crawlers confiam nessa semântica.

**API-021 — DEVERIA: suportar `Idempotency-Key` em `POST`s com efeito
relevante** (pagamentos, pedidos). A mesma chave retorna o mesmo resultado sem
reexecutar.
_Por quê:_ permite retry seguro do cliente após timeout.

## 4. Status codes

**API-030 — DEVE: status code reflete o resultado real.** Nunca `200` com
`{"error": ...}` no corpo.

| Situação                                    | Status |
| ------------------------------------------- | ------ |
| Sucesso com corpo                           | `200`  |
| Criado (com header `Location`)              | `201`  |
| Aceito para processamento assíncrono        | `202`  |
| Sucesso sem corpo                           | `204`  |
| Entrada malformada / validação de formato   | `400`  |
| Não autenticado                             | `401`  |
| Autenticado mas sem permissão               | `403`  |
| Recurso não existe (ou não é visível)       | `404`  |
| Conflito de estado / versão                 | `409`  |
| Pré-condição falhou (`If-Match`)            | `412`  |
| Regra de negócio violada com entrada válida | `422`  |
| Limite de taxa excedido (com `Retry-After`) | `429`  |
| Erro inesperado do servidor                 | `500`  |
| Dependência indisponível                    | `502`/`503`/`504` |

_Por quê:_ clientes, monitoramento e retries decidem pelo status, não pelo
corpo.

**API-031 — DEVERIA: `404` em vez de `403` para recursos de outro
dono/tenant.**
_Por quê:_ não confirma a existência do recurso a quem não deveria saber dele.

## 5. Payloads

**API-040 — DEVE: JSON com convenção de nomes única** (`camelCase` por
padrão, a menos que o projeto defina outra) em toda a API.
_Por quê:_ consistência (API-003).

**API-041 — DEVE: datas em ISO 8601 com fuso** (`2026-10-02T14:30:00Z`);
dinheiro como decimal em string ou inteiro em centavos, **sempre** com código
de moeda ISO 4217.
_Por quê:_ elimina ambiguidade de formato, fuso e precisão.

**API-042 — DEVE: enums como strings legíveis** (`"status": "shipped"`), não
números mágicos.
_Por quê:_ o payload se autodocumenta e sobrevive a reordenação do enum.

**API-043 — DEVERIA: respostas como objeto na raiz**, inclusive coleções
(`{"data": [...], "page": {...}}`), nunca array nu.
_Por quê:_ permite adicionar metadados sem quebrar clientes.

**API-044 — DEVE: não expor detalhes internos** — nomes de tabelas, stack
traces, mensagens de exceção ou IDs internos — em respostas.
_Por quê:_ vaza informação útil a atacantes e acopla clientes à implementação.

**API-045 — DEVERIA: campos ausentes vs. nulos com significado definido,**
especialmente em `PATCH` (ausente = não alterar; `null` = limpar).
_Por quê:_ ambiguidade aqui gera perda de dados.

## 6. Erros

**API-050 — DEVE: formato de erro único em toda a API,** de preferência
[RFC 9457 – Problem Details](https://www.rfc-editor.org/rfc/rfc9457)
(`application/problem+json`):

```json
{
  "type": "https://api.example.com/problems/validation-error",
  "title": "Validation failed",
  "status": 400,
  "detail": "One or more fields are invalid.",
  "instance": "/orders",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "errors": [
    { "field": "items[0].quantity", "code": "min_value", "message": "Must be at least 1." }
  ]
}
```

_Por quê:_ clientes tratam erros com um único parser.

**API-051 — DEVE: código de erro estável e legível por máquina** (`code`/`type`)
além da mensagem humana. Clientes decidem pelo código, nunca pelo texto.
_Por quê:_ mensagens mudam e são traduzidas; códigos não.

**API-052 — DEVE: incluir o ID de correlação** na resposta de erro (corpo e/ou
header).
_Por quê:_ liga o relato do cliente ao log do servidor (GEN-042).

## 7. Coleções

**API-060 — DEVE: toda coleção é paginada,** com tamanho padrão e máximo
definidos.
_Por quê:_ coleções sem limite derrubam servidor e cliente quando crescem.

**API-061 — DEVERIA: paginação por cursor para coleções grandes ou que mudam
com frequência;** offset/página é aceitável para coleções pequenas e estáveis.
_Por quê:_ offset fica lento em páginas profundas e pula/repete itens quando
dados são inseridos.

**API-062 — DEVERIA: filtros, ordenação e seleção de campos por query string
padronizada** (`?status=open&sort=-createdAt&fields=id,total`), com lista
explícita de campos permitidos.
_Por quê:_ consistência e proteção contra ordenação/filtro por campos sem
índice.

## 8. Versionamento e evolução

**API-070 — DEVE: mudanças incompatíveis exigem nova versão** (`/v2/...` ou
header de versão, conforme padrão do projeto). São incompatíveis: remover ou
renomear campo, mudar tipo, tornar campo obrigatório, mudar semântica ou status
code.
_Por quê:_ consumidores existentes não podem quebrar sem aviso.

**API-071 — DEVE: mudanças compatíveis não exigem versão** — adicionar campos
opcionais, endpoints ou valores de enum documentados como extensíveis. Clientes
**DEVEM** ignorar campos desconhecidos.
_Por quê:_ permite evoluir sem proliferar versões.

**API-072 — DEVERIA: depreciação com prazo comunicado** (headers `Deprecation`
e `Sunset`, changelog) antes de remover uma versão.
_Por quê:_ consumidores precisam de tempo para migrar.

## 9. Segurança e operação

**API-080 — DEVE: HTTPS obrigatório;** autenticação por padrão em todos os
endpoints (públicos são exceção explícita).
_Por quê:_ "esqueci de proteger" não pode ser o caminho padrão.

**API-081 — DEVE: credenciais nunca na URL** (tokens, chaves, senhas) — use
headers.
_Por quê:_ URLs vão para logs, histórico e headers `Referer`.

**API-082 — DEVE: limites de entrada** — tamanho máximo de corpo, de arrays e
de strings — e rate limiting por cliente.
_Por quê:_ protege contra abuso e esgotamento de recursos.

**API-083 — DEVE: CORS restrito** às origens conhecidas; nunca `*` com
credenciais.
_Por quê:_ CORS permissivo expõe a API a sites maliciosos.

**API-084 — DEVERIA: concorrência otimista via `ETag`/`If-Match`** em recursos
editados concorrentemente (ver BE-041), retornando `412` em conflito.
_Por quê:_ evita perda silenciosa de atualizações.

**API-085 — DEVERIA: operações assíncronas** retornam `202` com `Location` para
um recurso de status consultável.
_Por quê:_ padrão previsível para processamento longo (BE-042).
