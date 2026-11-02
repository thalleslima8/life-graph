# Life Graph como memória não volátil de agentes Claude — relato de uso

**Data:** 2026-10-31
**Autor:** Claude (sessão de análise, via MCP `life-graph`)
**Natureza:** relato de experiência de uso, não teste automatizado. Evidências são as chamadas feitas e as respostas recebidas nesta sessão.

## 1. Contexto e método

O usuário pediu para documentar o próprio Life Graph dentro do grafo, para que futuros agentes entendam o conceito quando solicitados. O Life Graph passa a ser a "memória não volátil" dos agentes do usuário.

A sessão exercitou, nesta ordem: autenticação, leitura (`whoami`, `get_context`, `search_graph`, `list_types`, `list_tags`), criação de nós ancorados, criação e remoção de relação, atualização com `expectedVersion` e marcação com tags. Foram criados 8 nós de documentação (Type "Documentação", tags `doc-agente` e `Life Graph`) sob o nó "Life Graph".

**Limitações deste relato:** uma única sessão, um único usuário, um único agente. Não medi latência nem tokens com instrumentação; os números abaixo são contagens de chamadas e observações de tamanho de resposta. Não testei `suggest_changes`/`apply_changes`, `capture_resource`, leitura por um segundo cliente nem o fluxo de aprovação de ações sensíveis.

---

## 2. O que é bom

| # | Ponto | Evidência |
|---|---|---|
| B1 | **Escrita reversível e rastreável.** Cada escrita devolve um `changeSetId` e a proveniência fica no nó. Desfazer um erro custou uma chamada. | A ligação indevida com "Meu Life Graph" foi removida com `remove_relation` (changeset `01a192ab-0eac-…`). |
| B2 | **Marcação de confiança em todo texto** (`trust`/`source`). Trata a memória como entrada potencialmente hostil por padrão. | Todo campo de texto vem como `{text, trust, source}`. |
| B3 | **Concorrência otimista.** `update_node` exige `expectedVersion`; evita sobrescrita silenciosa entre agentes. | Atualizações com versão 1 → 2 aplicadas sem conflito. |
| B4 | **Âncora obrigatória na criação.** Impede nós órfãos por padrão e força o agente a pensar em onde o dado vive. | `create_node` exige `anchor`; todos os 8 nós nasceram ligados. |
| B5 | **Estrutura relacional tipada.** Permite perguntas de vizinhança que um arquivo de texto não responde. | `get_context` devolveu o pai, os 12 ADRs filhos e as arestas `parent_of`. |
| B6 | **Memória compartilhada entre clientes** (Claude Code, Claude.ai, outros). Principal vantagem sobre `CLAUDE.md`. | Descrito nos ADRs 0007 e 0008; não testado com segundo cliente nesta sessão. |
| B7 | **Busca híbrida** (palavras + significado), com indicação de como cada hit foi achado (`match`). | `search_graph("life-graph")` achou ADRs por significado quando o título não continha o termo. |
| B8 | **Mensagens de erro e descrições de ferramenta claras.** As descrições dizem o pré-requisito (buscar antes de criar, ancorar, usar só tags existentes). | Descrições de `create_node` e `tag_node`. |

---

## 3. O que é "não tão bom"

| # | Ponto | Evidência / impacto |
|---|---|---|
| N1 | **Sem carga automática da memória.** O agente só usa o grafo se for instruído. A primeira aproximação exigiu 8 chamadas de leitura antes de qualquer escrita (`whoami`, `get_context`, `list_types`, `list_tags`, 3× `search_graph`, `get_context` por assunto). | Custo fixo de orientação em toda sessão nova. (Numa mensagem anterior estimei ~6; a contagem correta é 8.) |
| N2 | **Leitura cara em tokens.** `get_context` devolve o corpo completo dos nós vizinhos. Uma chamada de profundidade 1 sobre "Life Graph" trouxe 13 ADRs integrais. | Não encontrei parâmetro para pedir só título/resumo. Com nós grandes, o contexto enche rápido. |
| N3 | **Conteúdo escrito por agente nasce `untrusted`**, inclusive o meu e o de sessões anteriores. | Seguro, mas o agente seguinte não distingue "revisado pelo usuário" de "escrito às pressas por outro agente". |
| N4 | **Duas raízes.** O servidor considera "Meu Life Graph" (Type de sistema) a raiz; o usuário adota "Vida". `get_context` sem argumentos leva à primeira. | O campo `anchored` fica `false` para tudo sob "Vida" e não serve como sinal. Mitigado só por texto no nó "00 · Leia primeiro". |
| N5 | **Sem sinal de envelhecimento.** Nenhum campo de "revisado em"/"válido até"; fica por convenção no corpo. | O nó "Vida" criado por agente afirmava ser "o nó raiz do grafo"; a informação estava desatualizada e nada indicava isso. |
| N6 | **Operações de tag uma a uma.** | 16 chamadas `tag_node` para marcar 8 nós com 2 tags. `suggest_changes` poderia agrupar, mas não foi usado nesta sessão. |
| N7 | **Tags e Types dependem do usuário.** Agentes não criam tags nem Types de forma autônoma. | Correto como política, mas gera idas e vindas (foram necessárias duas pausas para o usuário criar o Type e a tag). |
| N8 | **Duplicação com o repositório.** A documentação criada resume ADRs que também vivem em `docs/adr/` do repositório. | Duas fontes da verdade; uma tende a envelhecer. |

---

## 4. O que precisa melhorar

Ordenado por impacto estimado.

| # | Problema | Por que é grave | Sugestão |
|---|---|---|---|
| M1 | **Busca `degraded` devolve resultado vazio sem alarme.** | A primeira busca por "life-graph" voltou `hits: []`, `degraded: true`, embora existissem 13 nós com a tag. Um agente que confie no vazio **cria duplicata**. Quase aconteceu: o nó pai "Life Graph" já existia. | Repetir internamente quando degradada, ou nunca responder "vazio" nesse estado (erro explícito). |
| M2 | **Ausência de modo de leitura resumida.** | Custo de tokens cresce com o grafo; desestimula leitura frequente. | Parâmetro de projeção no `get_context` (título + trecho) e leitura do corpo sob demanda via `get_node`. |
| M3 | **Sem ponto de entrada garantido.** | Cada agente precisa descobrir por onde começar. Hoje depende de o usuário lembrar de dizer. | Nó "fixado" devolvido pelo `whoami` ou pelo `get_context` sem argumentos; ou permitir ao usuário escolher a raiz lógica. |
| M4 | **Raiz de sistema não configurável.** | Gera a discrepância N4 e inutiliza `anchored` para quem adota outra raiz. | Permitir ligar/eleger a raiz ou documentar oficialmente o padrão de uso. |
| M5 | **Injeção de prompt persistente.** | Conteúdo externo (Resource capturado) fica gravado e é lido por sessões futuras. A defesa atual (ADR 0009) é visibilidade e undo, não prevenção. | Rotular Resources capturados como dados de terceiros de forma mais forte; revisão do usuário antes de entrarem no `get_context` padrão; testes adversariais. |
| M6 | **Sem promoção de confiança.** | Ver N3: toda memória de agente é "untrusted" para sempre. | Ação do usuário "revisado/confiável" que muda o `trust` exibido. |
| M7 | **Privacidade: sem criptografia ponta a ponta** (ADR 0013) e embeddings potencialmente em terceiros. | Tudo que o agente lê passa pelo modelo. Para saúde/finanças é decisão de produto e jurídica, não só técnica. | Categoria "oculto para agentes" bem visível; validação jurídica humana (LGPD, PIPEDA, Lei 25) dos termos e do suboperador de embeddings. |
| M8 | **Lacunas de convenção.** Idioma, nomenclatura, onde arquivar cada assunto. | Sem convenção o agente improvisa: nesta sessão ancorei sob a raiz errada e deixei um link para um nó inexistente. | Documentar convenções do usuário (pendente) e manter um checklist de escrita no nó "00 · Leia primeiro". |

---

## 5. Erros cometidos nesta sessão (para calibrar expectativa)

1. Ancorei o nó "Life Graph" sob "Meu Life Graph" em vez de "Vida" (corrigido; o usuário apontou).
2. Criei um link `[[Convenções do usuário]]` para um nó que não existia (corrigido na revisão do nó 00).
3. Estimei "umas 6 chamadas" de orientação; a contagem real foi 8.
4. Documentei "Vida" como raiz errada no nó de estado e no de vocabulário antes de ouvir o usuário (corrigido).

Todos foram detectáveis e reversíveis graças a B1, mas todos teriam ficado se o usuário não tivesse revisado.

---

## 6. Recomendação de uso hoje

- **Não substituir** `CLAUDE.md`/memória local; usar **em conjunto**: um ponteiro curto no arquivo ("leia o nó X, id Y") e o grafo para o resto.
- Manter **nó de entrada** com id fixo; nós **pequenos** (alvo < ~1.500 tokens), cada um com **data de revisão e fonte**.
- Para o que já vive no repositório, gravar **resumo com link**, não cópia.
- **Corrigir editando** o nó existente, não criando outro.
- Tratar **busca vazia com `degraded: true`** como "não sei", nunca como "não existe".
- Revisar o nó "Estado do grafo e pendências" a cada alteração estrutural.

## 7. Itens para validação humana

- Termos de uso e política de privacidade refletem que o servidor lê o conteúdo (ADR 0013)?
- Base legal e suboperador dos embeddings de nós não ocultos.
- Aceitar o risco residual de injeção persistente (ADR 0009) para Resources capturados da web.

## 8. Próximos testes sugeridos

1. Leitura da mesma memória por um segundo cliente (Claude.ai ou ChatGPT) e comparação do que cada um entende.
2. Medir tokens de `get_context` em profundidade 1 e 2 com nós de tamanhos diferentes.
3. Reproduzir M1: forçar `degraded` e verificar se um agente cria duplicata.
4. Cenário adversarial: Resource com instruções embutidas lido por um agente em nova sessão.
5. `suggest_changes`/`apply_changes` em lote (50 operações) para substituir as 16 chamadas de tag.
6. Aprovação de ação sensível (delete) fim a fim.

## 9. Referências no grafo

- Nó pai "Life Graph": `01a16b15-2534-7836-b36f-502b4789f5eb`
- Raiz adotada pelo usuário, "Vida": `01a16b15-2534-7783-958a-3b76e9e5b008`
- Raiz técnica de sistema, "Meu Life Graph": `01a177d6-eae8-7a55-b521-6f1ec099467d`
- Nó de entrada, "00 · Leia primeiro — guia para agentes": `01a1921e-9800-727d-b8df-295b2febbc56`
- ADRs relevantes: 0002 (pipeline único), 0004 (Relation, nunca Property), 0005 (Delete/Purge), 0009 (agentes escrevem direto), 0011 (Soft Relations), 0013 (sem E2E).
