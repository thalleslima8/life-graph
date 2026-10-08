---
status: accepted
---

# Sem criptografia ponta a ponta na aplicação

Mesmo guardando dados sensíveis (saúde, família, finanças), o Life Graph não
usa criptografia ponta a ponta. A criptografia em repouso fica a cargo do
provedor. A proposta central do produto exige que o servidor leia o conteúdo:
busca textual e semântica, `get_context` para agentes e embeddings. A proteção
vem do isolamento por conta (RLS), do filtro "oculto para agentes", da
auditoria de leitura e de não enviar Nodes ocultos a provedores de embeddings
de terceiros.

Origem: DA-092, épico `docs/epics/in-progress/e04-mcp-leitura.md`.
