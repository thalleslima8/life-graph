---
status: accepted
---

# Pipeline único de escrita com GraphChangeSet na mesma transação

Toda escrita no grafo, de qualquer ator (UI, API, MCP, import), passa por um
único comando da camada de aplicação, que grava o GraphChangeSet e a
Provenance **na mesma transação** da mudança. Não é uma feature "para agentes"
adicionada depois: Undo, histórico, a tela Agent Changes e a proveniência só
são confiáveis se nenhum caminho de escrita escapar do pipeline. O histórico
guarda o antes e o depois em `change_entries`, sem event sourcing completo. A
única exceção deliberada são as Soft Relations (ADR 0011).

## Consequences

Repositórios genéricos com delete direto (ex.: o `BaseRepository` do
limaj-framework) não podem ser usados nos agregados do grafo.

Origem: DA-013, épico `docs/epics/backlog/e02-nucleo-de-escrita.md`.
