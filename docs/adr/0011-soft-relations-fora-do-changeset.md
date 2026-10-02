---
status: accepted
---

# Soft Relations são projeção derivada, fora do log de ChangeSet

Isso é uma exceção deliberada ao pipeline único de escrita (ADR 0002). As Soft
Relations (inferidas por embeddings) são persistidas para poderem ser
explicadas, listadas e ignoradas sem custo na navegação, mas o recálculo
**não** gera GraphChangeSet. Se gerasse, cada atualização de embedding
encheria Recent Changes e o feed dos agentes de ruído do sistema, e criaria
falsos conflitos de Undo contra coisas que o usuário não fez. Só as ações do
usuário (Accept e Ignore) geram ChangeSet.

Origem: DA-070 e DA-071, épico `docs/epics/backlog/e09-camada-semantica.md`.
