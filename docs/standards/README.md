# Padrões de desenvolvimento

Regras de boas práticas **agnósticas de linguagem e stack** que servem de base
para todos os projetos criados a partir deste kit. Elas descrevem *o que* deve
ser verdade no código, não *qual ferramenta* usar — a escolha de framework,
linter e biblioteca é de cada projeto e fica registrada no `CLAUDE.md` dele.

| Arquivo                          | Prefixo | Escopo                                                        |
| -------------------------------- | ------- | ------------------------------------------------------------- |
| [general.md](general.md)         | `GEN`   | Vale para todo código: nomes, funções, erros, logs, segurança, testes/TDD, vertical slicing, feature flags. |
| [backend.md](backend.md)         | `BE`    | Serviços e aplicações de servidor: camadas ou vertical slices, domínio, I/O, resiliência. |
| [frontend.md](frontend.md)       | `FE`    | Interfaces de usuário: componentes, estado, acessibilidade, performance. |
| [api-rest.md](api-rest.md)       | `API`   | Contratos HTTP/REST: recursos, verbos, status, erros, paginação, versão. |
| [database.md](database.md)       | `DB`    | Modelagem, migrations, consultas, transações e dados sensíveis. |

## Como ler as regras

Cada regra tem um **ID estável** (`GEN-001`, `API-014`, ...) e um nível:

- **DEVE** — obrigatória. Descumprir exige uma decisão registrada (ver abaixo).
- **DEVERIA** — padrão esperado. Desviar é aceitável com motivo claro no PR.
- **PODE** — recomendação; use quando fizer sentido.

Toda regra traz o **porquê**. Se o porquê não se aplica ao seu caso, a regra
provavelmente também não — e isso deve ser dito explicitamente, não ignorado em
silêncio.

## Precedência e exceções

1. Requisitos legais, de segurança e de compliance do produto.
2. Decisões registradas no projeto (`DA-###` nos épicos, ADRs).
3. `CLAUDE.md` do projeto (convenções da stack).
4. Estes padrões.
5. Convenções idiomáticas da linguagem/framework.

Uma exceção a uma regra **DEVE** é registrada como `DA-###` no épico
correspondente, citando o ID da regra e a justificativa. Ex.:

```
DA-004 — Exceção a API-060 (coleção paginada) em `GET /countries`:
lista fixa de ~250 itens, cacheada e sempre consumida inteira pelo
frontend; paginar só adicionaria requisições.
```

## Uso em revisão de código

- Comentários de review citam o ID: `API-030: este 200 deveria ser 201 com Location`.
- Regras que puderem ser verificadas por ferramenta (formatação, lint, análise
  estática, scanners de segredo) **DEVERIAM** ser automatizadas no CI do
  projeto, em vez de depender de revisão humana.

## Evoluindo os padrões

Estes arquivos vivem no `ai-starter-kit` e são copiados para cada projeto.
Melhorias genéricas voltam para o kit; ajustes específicos de um produto ficam
no próprio projeto (como exceção registrada ou no `CLAUDE.md`). IDs nunca são
reaproveitados: regra removida vira `~~GEN-0XX~~ (removida: motivo)`.
