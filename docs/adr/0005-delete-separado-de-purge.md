---
status: accepted
---

# Delete (reversível por 30 dias) separado de Purge (irreversível)

Delete é um tombstone que pode ser desfeito por 30 dias, em todos os planos.
Purge é a eliminação irreversível e acontece ao fim dessa janela, na exclusão
de conta (imediata, por decisão do usuário) ou em "apagar para sempre". O Purge
**apaga o conteúdo dos ChangeSets** que tocaram o dado, deixando só o
esqueleto. Sem isso, o histórico (que no Premium dura 1 ano) preservaria o
conteúdo apagado, contra LGPD, PIPEDA e Lei 25. Um ledger sem PII dos purges,
gravado na mesma transação, garante que uma restauração de backup não
ressuscite dados.

Origem: DA-021 (`docs/epics/backlog/e02-nucleo-de-escrita.md`), DA-082 a DA-084
(`docs/epics/backlog/e11-export-e-exclusao-de-conta.md`).
