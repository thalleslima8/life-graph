---
status: accepted
---

# Resource é um Node de Type de sistema, não uma entidade separada

Conteúdo externo (vídeo, artigo, página, links para pdf/imagem/documento) é um
Node do Type de sistema `Resource`. A natureza do conteúdo fica em
`resource_kind` e a plataforma em `provider`. Assim, relações, travessia,
filtro de ocultos, busca, export, quotas, ChangeSet e Undo funcionam sem caso
especial. As propriedades de sistema ficam travadas, e o usuário pode adicionar
as dele. Um livro físico é um Type do usuário, não um Resource.

Origem: DA-055 a DA-057, épico `docs/epics/backlog/e07-resources-e-captura.md`.
