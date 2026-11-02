---
status: accepted
---

# Tag e Type criados por agente são sempre uma proposta decidida pela pessoa

O agente pode criar Tags e Types, mas **nunca direto**. A criação é uma operação
de `suggest_changes` e vira uma ChangeProposal; só um principal Human a decide.
`apply_changes` recusa a criação com "precisa de proposta" e não usa o caminho de
confirmação do nível Sensitive, que o cliente do próprio agente poderia
satisfazer. O aviso é para a pessoa que usa o agente, e só uma decisão registrada
prova que ela foi avisada antes.

Antes de existir qualquer proposta, o **servidor** roda um portão de
similaridade: nome normalizado idêntico bloqueia e devolve o item existente;
semelhança por embedding (com fallback para trigram/FTS) devolve candidatos, que
a proposta grava e mostra lado a lado. Busca degradada nunca libera a criação. O
portão vive no servidor, não nas `instructions` nem na boa vontade do agente.

Isso é uma exceção deliberada à ADR-0009, em que o agente aplica direto no nível
Write. Aqui a ontologia é da pessoa, e um Type ou Tag duplicado de significado
não se resolve com Undo depois que outros Nodes passam a usá-lo.

Alternativas descartadas: criar direto quando o portão não acha candidato (o
aviso ficaria à mercê do agente, e uma busca degradada que não acha nada liberaria
a criação quando o sistema está mais cego); só aconselhar (não é testável); scope
novo (mudaria a tela de consentimento sem ganho).

## Consequences

Cada Tag ou Type novo custa uma decisão da pessoa. O atrito é reduzido pela fila
de revisão e pela decisão em lote (E20); Type fica fora do "selecionar todos".

Uma proposta leva a criação e as operações que dependem dela por `Ref`. Se a
pessoa rejeita a criação, o servidor exclui as dependentes. **O servidor nunca
reescreve uma `Ref`**: não há "a pessoa escolhe um candidato e o servidor
substitui", porque o GraphChangeSet passaria a registrar uma operação diferente da
que o agente enviou. O agente re-propõe usando o item existente, e a rejeição
devolve código, motivo e candidatos para isso. Substituição de Tags é candidata a
um épico posterior; reverter para ela exige só estender o contrato.

O agente só cria. Renomear, editar e excluir Tag e Type continuam da pessoa.
Alias e sinônimo ficam fora, então "mesmo significado" só é pego por semelhança
de nome.

Origem: DA-298, DA-299 e DA-302, épico
`docs/epics/backlog/e23-tags-e-types-criados-por-agente.md`.
