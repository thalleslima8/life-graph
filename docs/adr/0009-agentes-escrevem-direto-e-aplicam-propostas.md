---
status: accepted
---

# Agentes escrevem direto no nível Write e podem aplicar as próprias propostas

O v1 dizia "IA propõe, usuário decide". A v2 troca isso por "resultado visível
e automação reversível", e o usuário confirmou. No nível Write, agentes criam,
editam (inclusive conteúdo do usuário) e removem relações na hora, como
GraphChangeSet que pode ser desfeito. A ferramenta `apply_changes` existe no
MCP: um agente pode aplicar a própria proposta não-sensível, com proteções
re-checadas no momento de aplicar (mesma identidade, versão base,
autorização, filtro de ocultos). O nível Sensitive (delete, bulk, share,
permissões), a troca de Type de um Node do usuário e as propostas criadas com
o toggle de aprovação ligado só são aprovados pelo usuário na UI. Limites de
segurança (50 operações, bulk de 20, janela deslizante) contêm agentes com
defeito ou manipulados por prompt injection.

## Consequences

Um agente manipulado pode sobrescrever texto do usuário. A defesa é a
visibilidade (diff em Agent Changes) e o Undo, não a prevenção.

Origem: DA-040 a DA-047, épico
`docs/epics/backlog/e05-mcp-escrita-agent-changes.md`.
