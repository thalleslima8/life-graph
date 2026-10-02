---
status: accepted
---

# Adapter MCP no mesmo processo, chamando os casos de uso diretamente

A v2 (§31) desenha o MCP chamando a Life Graph API. Na implementação, o MCP é
um adapter fino **dentro do mesmo deployable**, que chama os mesmos casos de
uso da camada de aplicação que o REST usa, sem loopback HTTP. O invariante que
importa continua valendo: o domínio nunca depende do MCP, e testes de
arquitetura garantem isso. Um salto HTTP interno só acrescentaria latência,
serialização dupla e um segundo ponto de autenticação, sem ganho de
isolamento.

Origem: DA-032, épico `docs/epics/backlog/e03-conexao-de-agentes.md`.
