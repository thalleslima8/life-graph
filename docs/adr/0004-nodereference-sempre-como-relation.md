---
status: accepted
---

# Ligação entre Nodes é sempre Relation, nunca Property

A v2 lista `NodeReference` como tipo de Property, mas ele não existe no
modelo: toda ligação entre Nodes é uma Relation. Ter dois jeitos de ligar Nodes
quebraria a travessia do `get_context`, o filtro "oculto para agentes" (o Node
vazaria por valor de propriedade), a detecção de conflito do Undo e a
explicabilidade das relações. Se a UI precisar de "slots" de relação esperados
por Type, eles serão declarados no Type, não guardados como valor.

Origem: DA-017, épico `docs/epics/done/e02-nucleo-de-escrita.md`.
