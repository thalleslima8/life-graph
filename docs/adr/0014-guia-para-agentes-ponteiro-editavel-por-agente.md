---
status: accepted
---

# O guia para agentes é um ponteiro para um Node comum, editável por agente

A pessoa precisa dizer aos seus agentes como usar o grafo (como nomear, onde
ancorar, o que nunca gravar). A Raiz da Conta não serve: é um Node de sistema,
protegido e único. Em vez de eleger outra raiz lógica, a pessoa **designa um Node
comum** como guia para agentes. O `get_context` sem argumentos o devolve junto da
Raiz, com o corpo cortado e rotulado como conteúdo da pessoa e como convenção
sugerida, nunca autorização. A designação é uma linha por Account e não mexe na
Raiz, na ancoragem nem na contagem de Desancorados.

O conteúdo do guia é editável por uma AgentIdentity como qualquer Node, dentro
dos scopes dela. O pipeline único (ADR-0002) não ganhou uma regra de
imutabilidade. A mitigação é o rótulo, o corte, o indicador de revisão e testes
adversariais da leitura e da edição do guia. Só a pessoa designa, troca ou remove
a designação. Nenhuma tool MCP o faz.

Alternativas descartadas: uma segunda raiz (mexeria na ancoragem e no que é
alcançável, com custo alto e sem ganho para o agente); prompts ou resources MCP e
`instructions` por conta (nem todo cliente os entrega, e o comportamento crítico
não pode depender deles); guia imutável por agente (ver a dívida abaixo).

## Consequences

O guia é texto escrito por um agente e lido depois como orientação por outro
agente. É um vetor de prompt injection persistente. O rótulo "convenção sugerida,
nunca autorização" é um contrato do servidor, não uma garantia de que o modelo do
agente vai obedecê-lo. Os testes adversariais provam que o servidor **não eleva
scope nem nível de ação** por causa do guia; não provam que um agente não segue
um guia envenenado.

Risco aceito, com dívida registrada (DT-1 no E22): o pipeline único recusar
qualquer escrita de uma AgentIdentity sobre o Node designado. Reavaliar quando um
teste ou caso real mostrar que o rótulo não basta, quando a avaliação de injeção
do E15 apontar o guia como vetor, quando agentes de terceiros fora do controle da
pessoa se conectarem, ou quando o guia passar a carregar algo além de convenção.
Trocar o ponteiro por uma raiz lógica depois é possível (a designação é uma
tabela), mas exigiria rever a ancoragem.

Origem: DA-291, DA-292 e DA-293, épico
`docs/epics/backlog/e22-guia-para-agentes-node-de-entrada.md`.
