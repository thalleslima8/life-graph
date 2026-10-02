---
status: accepted
---

# PostgreSQL + pgvector em adjacência relacional, não banco de grafo

O Life Graph é um grafo, mas é guardado em PostgreSQL com tabelas de nodes e
relations (adjacência relacional). A travessia usa CTE recursiva com limites de
profundidade e de quantidade, a busca textual usa FTS e os vetores ficam no
pgvector, tudo no mesmo banco. Os grafos são pequenos (Free com 500 Nodes,
Premium com ~10k Nodes e 50k Relations), então profundidade 2 é trivial em SQL.
Um único armazenamento evita escrita dupla entre grafo, vetores, contas e
histórico, e permite RLS por conta.

## Considered Options

- **Neo4j (ou outro banco de grafo nativo):** rejeitado. Exigiria um segundo
  armazenamento para vetores e contas, custa mais caro de hospedar e tem
  multi-tenancy fraca na edição gratuita.

Origem: DA-003, épico `docs/epics/done/e00-plataforma-dev.md`.
