# E9 — Camada semântica

- **Ordem:** 9
- **Depende de:** E2, E7
- **Última revisão:** 2026-10-02

## Contexto

A Semantic Layer (v2 §45) descobre proximidade por embeddings, sem LLM: busca
semântica básica, Soft Relations sugeridas e o passo semântico do
`get_context`. O conteúdo dos Resources é a principal entrada dos embeddings,
por isso este épico vem depois do E7.

## Decisões

- **DA-069 — Embeddings assíncronos via outbox** (consenso). Nunca bloqueiam
  nem derrubam uma escrita. Ficam numa tabela à parte, com chave
  `(node_id, model_id)`, `dimension` e `content_hash`. Só são recalculados
  quando o texto muda (título, corpo, propriedades de texto, Outras
  propriedades de texto, metadados de Resource). Uma troca de modelo dispara
  reindexação em lote.
- **DA-070 — Soft Relations persistidas como projeção derivada, fora do log de
  ChangeSet** (consenso, ADR: [docs/adr/0011-soft-relations-fora-do-changeset.md](../../adr/0011-soft-relations-fora-do-changeset.md)). Top-k ≈ 5 por Node acima de um
  limiar (~0.75, a calibrar), via HNSW, com pares sem ordem e explicação
  (método, modelo, score). Se passassem pelo ChangeSet, o recálculo encheria o
  Recent Changes de ruído e geraria conflitos de Undo falsos. Só as ações do
  usuário (Accept/Ignore) geram ChangeSet.
- **DA-071 — Accept e Ignore** (consenso, glossário). Accept cria uma Hard
  Relation com origin=user. Ignore grava uma supressão por par
  (`soft_dismissal`), que sobrevive a trocas de modelo e evita que o par seja
  sugerido de novo.
- **DA-072 — Nodes ocultos para agentes nunca vão para um provedor de
  embeddings de terceiros** (consenso). O usuário espera que "oculto" signifique
  "não enviado a uma empresa de IA". Com modelo local, eles podem ter
  embeddings para as sugestões do próprio usuário, mas sempre filtrados das
  respostas a agentes.
- **DA-073 — Provedor de embeddings: decisão do usuário neste épico**
  (pendente). Resumo das opções:
  - **Modelo local** (ex.: `multilingual-e5-small`, 384 dimensões, bom em PT):
    sem suboperador e com residência igual à do host, mas pede ~1 GB de RAM.
  - **API** (ex.: OpenAI `text-embedding-3-small`, custo desprezível): processa
    fora do Canadá, entra como suboperador, exige linha na PIA e o nosso Purge
    não apaga a retenção do lado do provedor.
  - Cada épico que adiciona um suboperador registra uma linha no registro
    contínuo de "suboperadores e fluxos de dados".

## Checklist

- [ ] **Decisão do usuário:** provedor de embeddings (DA-073)
- [ ] Tabela de embeddings + índice HNSW
- [ ] Job de embedding via outbox (idempotente, com dead-letter: BE-034, BE-036)
- [ ] Cálculo de Soft Relations top-k com explicação
- [ ] Accept/Ignore no Inspector e na Inbox; arestas Soft no grafo 2.5D
- [ ] Busca semântica básica (híbrida FTS + vetor)
- [ ] Passo semântico no `get_context` (mesmo contrato, Soft só na profundidade 1)
- [ ] Purge e exclusão de conta removem embeddings e Soft Relations

## Critérios de saída

Salvar um vídeo sobre Marco Aurélio sugere uma relação com Estoicismo, com
explicação. Ignore a faz sumir de vez.

## Fora de escopo

Clustering semântico avançado, linguagem natural com LLM interno.
