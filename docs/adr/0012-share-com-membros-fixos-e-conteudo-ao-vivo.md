---
status: accepted
---

# SharedGraphView com membros fixos e conteúdo ao vivo

A v2 oferece "Snapshot" ou "Live". Nenhum dos dois é usado puro. Os Nodes de um
link compartilhado são fixados na criação (o usuário revisa a lista), e o
conteúdo exibido é sempre o atual. O que o visitante vê é uma allowlist fechada
(nenhuma propriedade por padrão), e Nodes ocultos para agentes nunca entram.

## Considered Options

- **Live:** rejeitado. Um Node sensível adicionado depois, como uma nota de
  saúde ligada a "Bebê", vazaria sozinho num link já público.
- **Snapshot (cópia):** rejeitado. A cópia sobreviveria ao Purge, contra LGPD
  e Lei 25.

Origem: DA-075 a DA-077, épico `docs/epics/backlog/e10-shared-graph-view.md`.
