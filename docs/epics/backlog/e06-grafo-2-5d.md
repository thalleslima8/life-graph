# E6 — Grafo local 2.5D + Semantic Zoom básico

- **Ordem:** 6
- **Depende de:** E2
- **Última revisão:** 2026-10-02

## Contexto

A interface 2.5D é a representação humana do grafo (v2 §18–20, §64). Ela deixa
o usuário navegar pelo repertório que ele e os agentes construíram. O MVP
valida o grafo local (a partir de um Node, com profundidade controlável), o
Semantic Zoom básico e o Node Inspector, sem o universo visual inteiro.

## Decisões

- **DA-052 — A biblioteca de renderização é escolhida num spike no início do
  épico** (consenso). Critérios: FPS com 1k Nodes, Semantic Zoom, fallback
  acessível (visão em lista) e tamanho do bundle. A inclinação é por
  **Sigma.js + graphology** (WebGL, já que não há 3D no MVP). A alternativa é
  react-force-graph. O estado do grafo fica num renderer imperativo, fora do
  estado do React, para evitar re-renders.
- **DA-053 — Zero LLM na navegação e na renderização** (invariante da v2 §46).
  Layout, zoom e filtros são algoritmos tradicionais.
- **DA-054 — Atualizações em "tempo real" por polling do feed de ChangeSets**
  (DA-024). Nodes criados por agentes aparecem sem o usuário recarregar.

## Checklist

- [ ] Spike comparando as bibliotecas e registrando a escolha
- [ ] Grafo local a partir de um Node, com profundidade 1/2/3
- [ ] Representação visual: tamanho = relevância, proximidade = relação, Hard vs Soft distinguíveis (Soft entra no E9)
- [ ] Semantic Zoom básico (nível conceitual muda com o zoom)
- [ ] Node Inspector integrado ao grafo
- [ ] Destaque de mudanças recentes de agentes
- [ ] Fallback acessível em lista (FE-030), navegação por teclado

## Critérios de saída

O usuário entra num Node, navega pelos vizinhos em 2.5D, vê aparecer o que um
agente acabou de criar e abre o Inspector.

## Fora de escopo

Grafo global/3D, clustering semântico avançado.
