# E23 — Tags e Types criados por agente, com aprovação da pessoa

- **Ordem:** 23 (executa **depois do E22**, antes do E16 e do E14, DA-305)
- **Depende de:** E5, E20, E21, E22
- **Última revisão:** 2026-11-01

## Contexto

Hoje o agente não cria Tags nem Types: `tag_node` exige uma Tag existente ("you
cannot create Tags", `GraphWriteTools.cs:144`) e os Types vêm de `list_types`
(DA-039, que só empurra o agente a escolher um Type existente; **não** proíbe
criar). Não há DA que justifique a restrição.

O usuário quer que o agente possa criar Tags e Types, desde que **avise antes e
que o aviso seja para a pessoa que usa o agente**, para não nascer algo com
sentido duplicado. Isso complementa a DA-039 e a DA-134 (não as reverte) e muda o
texto de consentimento do scope `lifegraph.write`, hoje "Criar e alterar Nodes e
Relations... Pode propor exclusões, que só você aprova".

A ontologia continua sendo da pessoa: o agente propõe, a pessoa decide.

## Decisões

- **DA-298 — Portão de similaridade no servidor antes de qualquer criação**
  (consenso).
  - Nenhuma criação de Tag ou Type passa sem a checagem, e o resultado não
    depende da educação do agente. Camadas, sem LLM obrigatório:
    1. nome normalizado (caixa, acento, espaço, plural simples): colisão exata
       **bloqueia** e devolve o item existente (`ErrorType.Validation`, 422);
    2. similaridade do nome por embedding, com fallback para trigram/FTS quando
       os embeddings estiverem indisponíveis: **avisa** e devolve candidatos.
  - A resposta sempre diz se a checagem está degradada. Degradada nunca libera
    criação sem a pessoa (DA-299).
  - Limiares são configuração, calibrados com fixtures de integração.
  - Alias e sinônimo ficam fora (limitação conhecida: "mesmo significado" só se
    pega por semelhança de nome).
  - Por quê: "só aconselhar" não é testável e contraria a regra de que
    comportamento crítico mora no contrato (DA-284).
  - ADR: `docs/adr/0015-tag-e-type-por-agente-sempre-via-proposta.md`
    (exceção deliberada à ADR-0009; cobre também a DA-299 e a DA-302).
- **DA-299 — Toda Tag e todo Type criado por agente é uma ChangeProposal; só um
  Human decide** (usuário; Q17 pelo analista).
  - A pessoa é a destinatária do aviso. Criar Tag ou Type por `apply_changes`
    devolve sempre "precisa de proposta": nunca usa o caminho de confirmação
    do nível Sensitive, que o cliente do próprio agente poderia satisfazer.
  - A proposta grava os candidatos de similaridade no momento da criação e os
    mostra lado a lado. A pessoa decide sobre o que viu, não sobre uma consulta
    refeita depois.
  - A decisão vem só de um principal Human. Um token de AgentIdentity no endpoint
    de decisão recebe `NotFound` (teste de integração). Não há auto-decisão do
    agente para esta operação.
  - Criação de **Type** fica fora do "selecionar todos" do E20, pelo mesmo
    mecanismo da DA-278. Criação de Tag pode entrar em lote.
  - Por quê: só uma decisão registrada na proposta prova que a pessoa foi
    avisada antes. Criar direto, mesmo sem candidato, deixaria o aviso na boa
    vontade do agente, e uma busca degradada que não acha nada liberaria a
    criação justo quando o sistema está mais cego. O custo é atrito, que o E20
    reduz com a fila e a decisão em lote.
- **DA-300 — Scope, nível de ação e teto** (consenso).
  - `lifegraph.write`, sem scope novo (a DA-134 já limita o conjunto e um scope
    novo mudaria a tela de consentimento). Tag em nível Write e Type em Sensitive;
    como ambos exigem proposta (DA-299), o nível documenta o risco e orienta a
    tela.
  - Teto técnico de criações por proposta, contra um agente em loop ou levado por
    injeção que inunda a ontologia. Free e Premium iguais; o teto vale para criar,
    nunca para ler ou usar item existente.
  - Nomes de Tag e Type são entrada não confiável: validação de tamanho e
    charset, nunca em logs (GEN-043).
  - O texto de consentimento do `lifegraph.write` é atualizado, e esta decisão
    complementa a DA-039 e a DA-134.
- **DA-301 — Uma proposta leva a criação e as operações que dependem dela**
  (consenso).
  - Exige uma `Ref` para Tag e Type dentro do lote (hoje só Node tem). A proposta
    inclui, por exemplo, `create_tag` e os `tag_node` que apontam para ela, e a
    aprovação é uma decisão, um GraphChangeSet e um Undo.
  - Se a pessoa rejeita a criação, o servidor **exclui automaticamente** as
    operações que dependem da `Ref` e mostra essa cascata antes de confirmar.
    Uma dependente nunca é aprovada sem a criação (teste unitário do grafo de
    dependências).
  - Enquanto a proposta espera, o agente trabalha com o que não depende dela. Não
    repete a criação nem usa por conta própria um item parecido. O estado da
    proposta é legível pelo MCP e ela expira como hoje.
- **DA-302 — Rejeição estruturada; sem substituição por candidato** (consenso
  após contra-argumento).
  - Não existe "a pessoa escolhe um candidato e o servidor reescreve a `Ref`":
    o ChangeSet gravaria uma operação diferente da que o agente enviou, o que
    mexe em Provenance e explicabilidade. O agente **re-propõe** usando o item
    existente.
  - Critério de aceite: a rejeição devolve código estável, motivo e candidatos
    ordenados (id, nome, similaridade), tudo pelo filtro central (nunca Node
    oculto nem outra conta). Isso permite à substituição entrar depois sem
    quebrar contrato.
  - Substituição de Tags é candidata explícita para um épico posterior; para
    Types fica adiada pelo risco de esquema.
  - ADR: registrado como consequência em
    `docs/adr/0015-tag-e-type-por-agente-sempre-via-proposta.md`.
- **DA-303 — Provenance, Undo e estado de revisão** (consenso).
  - Tag e Type criados por agente são entradas de GraphChangeSet com Provenance
    (AgentIdentity, via, fonte), pelo pipeline único (DA-013). O Undo já recusa
    enquanto um Node ainda usa a Tag ou o Type (`ChangeSetUndo.cs:735-745`); não
    há cascata, e nada muda aqui.
  - O agente **só cria**. Renomear, editar e excluir Tag e Type continuam da
    pessoa, porque renomear muda o significado de tudo que já usa o item.
  - Como a criação sempre passa pela pessoa, não existe "criado por agente, não
    revisado". O estado "criado por agente, aprovado por você" é derivado da
    Provenance, sem booleano duplicado.
- **DA-304 — Guia de uso e guia para agentes revisados; injeção coberta**
  (consenso).
  - O guia de uso (E21, DA-287) e o texto do guia para agentes (E22) são
    revisados: o agente passa a poder propor Tags e Types.
  - Testes adversariais por fatia, no mesmo PR: nome de Tag/Type com instrução,
    inundação de propostas, conteúdo injetado que tenta criar Types em massa. A
    varredura ampla fica no E15.
- **DA-305 — Ordem de execução do roadmap** (usuário, com arquiteto e analista).
  - **E13 → E21 → E19 → E20 → E22 → E23 → E16 → E14 → E15.**
  - E21 vem logo depois do E13 e não depende dele. A Home (E19) chega cedo e é
    migrada para os tokens quando o E16 rodar. E22 e E23 vêm depois do E20 (a fila
    e a decisão vivem lá). E22 precede E23: não se dá poder de criar Types a
    agentes antes de testar injeção. O E16 roda antes do E14.
  - O E14 **não** depende de E19 a E23: a regra só aceita número menor, e a
    ordem de execução não é dependência. E19, E20, E22 e E23 usam só os tokens de
    design existentes, para a migração do E16 ficar barata.
  - **Declaração de dependência:** o E23 declara o E20, porque a fila é o que faz
    a confirmação de Type escalar. O fluxo funciona também sobre as telas do E5, e
    o E23 não é projetado em torno de um fallback. (O usuário respondeu
    "concordo" sem escolher um lado; adotei a posição do analista, **a
    confirmar**.)

## Estrutura proposta

- `src/LifeGraph.Graph/`: o portão de similaridade (domínio puro mais consulta de
  embedding/trigram), operações `create_tag` e `create_type` em
  `suggest_changes`, `Ref` de Tag e Type no lote, validação do grafo de
  dependências e a exclusão em cascata na decisão parcial.
- `src/LifeGraph.Agents/`: operações novas em `suggest_changes`; `apply_changes`
  devolve "precisa de proposta"; descrições das tools e o texto de consentimento
  em `src/LifeGraph.Accounts/Http/AuthorizationEndpoints.cs` e
  `web/src/features/agents/agentIdentities.ts`.
- `web/src/features/agents/`: o detalhe da proposta mostra os candidatos lado a
  lado e a cascata antes de confirmar; Type fora de "selecionar todos".

## Checklist

### Backend e MCP
- [ ] Domínio do portão: normalização, colisão exata e limiares (DA-298)
- [ ] Similaridade por embedding com fallback trigram/FTS e sinal de degradada (DA-298)
- [ ] `create_tag` e `create_type` como operações de `suggest_changes`, sempre como proposta (DA-299)
- [ ] `apply_changes` recusa criação de Tag e Type com "precisa de proposta" (DA-299)
- [ ] Decisão só por Human; `NotFound` para token de AgentIdentity (DA-299)
- [ ] Candidatos gravados na proposta no momento da criação (DA-299)
- [ ] Teto de criações por proposta e validação de nome (DA-300)
- [ ] `Ref` de Tag e Type no lote e validação das dependentes (DA-301)
- [ ] Rejeição da criação exclui as dependentes e mostra a cascata (DA-301)
- [ ] Rejeição estruturada: código, motivo e candidatos pelo filtro central (DA-302)
- [ ] Provenance e entradas de ChangeSet para Tag e Type; Undo recusa com dependentes (DA-303)
- [ ] Texto de consentimento do `lifegraph.write` atualizado (DA-300)
- [ ] OpenAPI regenerado e commitado

### Frontend
- [ ] Detalhe da proposta com candidatos lado a lado e cascata das dependentes (DA-299, DA-301)
- [ ] Type fora do "selecionar todos"; Tag pode entrar em lote (DA-299)
- [ ] Estado "criado por agente, aprovado por você" a partir da Provenance (DA-303)
- [ ] Usar só os tokens de design já existentes (DA-305)
- [ ] Textos novos no catálogo (FE-062)

### Documentação
- [ ] Revisar o guia de uso (E21) e o texto do guia para agentes (E22) (DA-304)
- [ ] Nota na DA-039 e na DA-134 apontando para esta decisão (DA-300)

### Testes obrigatórios
- [ ] Unitário: normalização, colisão exata, limiares, grafo de dependências e cascata
- [ ] Integração em Postgres real: o portão com embeddings disponíveis e degradados (degradada nunca cria direto)
- [ ] Integração: AgentIdentity não decide proposta de criação (`NotFound`)
- [ ] Integração: Provenance, ChangeSet e Undo, inclusive Undo recusado com Nodes usando a Tag
- [ ] Integração: isolamento entre contas (RLS) e candidato oculto nunca aparece na rejeição
- [ ] Contrato MCP: `apply_changes` com criação de Tag/Type é recusado; o portão não depende de `instructions` (DA-284)
- [ ] Contrato: a rejeição estruturada traz código, motivo e candidatos
- [ ] Adversarial: nome com instrução, inundação de propostas e criação em massa de Types (DA-304)
- [ ] Frontend: candidatos lado a lado, cascata e exclusão do "selecionar todos"; axe
- [ ] Nada de nome de Tag ou Type em logs (GEN-043)

## Critérios de saída

Um agente que precisa de uma Tag ou de um Type novo propõe a criação. A pessoa vê
os itens parecidos já existentes, decide, e o resultado é um GraphChangeSet
desfazível com Provenance. Nenhuma criação acontece sem essa decisão, nem com a
busca degradada, nem pelo `apply_changes`.

## Fora de escopo

Substituição por candidato (DA-302). Alias e sinônimo. Renomear, editar e excluir
Tag e Type por agente. Criação direta sem proposta. Scope novo. Quota por plano.
Privacidade e varredura ampla de injeção (E15).
