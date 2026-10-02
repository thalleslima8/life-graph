# Life Graph

## 1. Visão

**Life Graph** é uma camada pessoal de contexto estruturado que representa assuntos, recursos, pessoas, eventos, projetos e relações da vida do usuário.

O sistema deve poder ser utilizado de duas maneiras complementares:

1. **Diretamente pelo usuário**, através de uma interface visual 2.5D.
2. **Por agentes de IA autorizados**, através de API e MCP.

A interface visual permite que o usuário enxergue, explore, corrija e controle o contexto acumulado.

A API/MCP permite que ferramentas como ChatGPT, Claude, Codex e outros agentes consultem e, quando autorizados, modifiquem esse contexto.

A proposta central passa a ser:

> **Uma base contextual pessoal, visual e independente do fornecedor de IA, que humanos e agentes podem consultar e manter.**

---

# 2. Tese do Produto

Hoje informações pessoais tendem a ficar espalhadas entre:

```text
notas
calendários
favoritos
links
conversas com IA
documentos
listas
tarefas
apps
memórias de diferentes assistentes
```

Cada agente também tende a possuir seu próprio contexto:

```text
ChatGPT
   ↓
contexto próprio

Claude
   ↓
contexto próprio

Codex
   ↓
contexto próprio
```

O Life Graph propõe inverter essa relação:

```text
             LIFE GRAPH

                 ↑

      ┌──────────┼──────────┐
      │          │          │
   ChatGPT     Claude      Codex
      │          │          │
   conversa    conversa    conversa
```

O contexto persistente pertence ao usuário.

Os agentes são clientes desse contexto.

---

# 3. Definição resumida

> **Life Graph é um Personal Context Graph que se organiza com pouca intervenção, pode ser utilizado por agentes externos e permite navegar pela própria vida através de contexto, relações semânticas e tempo.**

---

# 4. Princípios fundamentais

## Capture first, organize later

O usuário deve conseguir salvar informação sem decidir imediatamente onde ela pertence.

---

## AI-assisted, not AI-dependent

O sistema continua útil sem chamadas constantes de LLM.

---

## User-owned context

A fonte persistente de contexto pertence ao usuário, não ao agente.

---

## Agents operate on the graph

Agentes podem consultar e modificar o grafo através de interfaces controladas.

---

## Human-visible result

Toda alteração feita por agentes deve produzir um resultado que o usuário consiga enxergar no Life Graph.

---

## Explainable structure

Relações inferidas ou criadas automaticamente devem poder explicar sua origem.

---

## Reversible automation

Alterações feitas por agentes devem ser rastreáveis e reversíveis.

---

# 5. Modelo conceitual principal

O produto será construído sobre alguns conceitos fundamentais:

```text
Node
Resource
Relation
Type
Property
Tag
Collection
View
TemporalAnchor
SharedGraphView
AgentIdentity
GraphChangeSet
Provenance
```

---

# 6. Node

Um `Node` representa uma entidade relevante.

Exemplos:

```text
Bebê
Filosofia
Casa
Viagem dos sogros
Marco Aurélio
Quarto do bebê
Comprar casa
Projeto pessoal
```

Estrutura conceitual:

```text
Node

id
name
description
type
properties
tags
createdAt
updatedAt
semanticEmbedding
```

---

# 7. Types

O usuário poderá definir tipos.

Exemplos:

```text
Book
Person
Project
Topic
Place
Item
Event
Goal
Note
```

Um Type pode definir propriedades esperadas.

Exemplo:

```text
Book

Author
Status
Owned
Location
StartedAt
FinishedAt
Rating
```

Objeto:

```text
Meditações

Type: Book
Author: Marco Aurélio
Status: Reading
Owned: Yes
Location: Estante da sala
```

---

# 8. Properties

Properties podem ser criadas pelo usuário.

Tipos iniciais:

```text
Text
Number
Boolean
Date
DateTime
URL
NodeReference
Select
MultiSelect
Status
Location
Rating
```

Properties automaticamente podem ser utilizadas em:

```text
filtros
Views
Collections
buscas
agrupamentos
queries
```

---

# 9. Tags

Tags oferecem classificação flexível.

Exemplo:

```text
#filosofia
#estoicismo
#quero-ler
#importante
```

Tags não substituem relações estruturadas.

---

# 10. Collections

Collections são conjuntos criados pelo usuário.

Podem ser manuais ou dinâmicos.

Exemplo:

```text
Collection: Quero ler

Type = Book
Status = WantToRead
```

Ou:

```text
Collection: Filosofia

Tag = filosofia
OR
Relation → Filosofia
```

Collections podem aparecer como itens de navegação.

Assim, o próprio usuário constrói progressivamente os menus relevantes para sua vida.

---

# 11. Resources

Conteúdo externo deve ser tratado como objeto relevante.

Tipos possíveis:

```text
Video
Article
Web Page
Social Post
Podcast
PDF
Image
Book
Document
File
```

Exemplo:

```text
Resource

url
title
type
author
source
thumbnail
description
publishedAt
savedAt
status
notes
metadata
embedding
```

---

# 12. Relations

Relations conectam Nodes e Resources.

Exemplos:

```text
related_to
belongs_to
part_of
created_by
written_by
located_at
depends_on
scheduled_for
inspired_by
used_for
about
```

Estrutura:

```text
Relation

source
target
type
strength
origin
confidence
createdAt
```

---

# 13. Hard Graph

Representa informações confirmadas.

```text
Meditações ──written_by──> Marco Aurélio

Meditações ──located_at──> Estante da sala
```

Origem possível:

```text
UserConfirmed
AgentConfirmed
Imported
SystemDerived
```

---

# 14. Soft Graph

Representa relações inferidas.

```text
Meditações - - - Estoicismo
              0.94

Meditações - - - Ética
              0.78
```

Origem possível:

```text
EmbeddingSimilarity
AIInference
SharedContext
CoOccurrence
```

O usuário poderá:

```text
Confirm
Ignore
Remove
Promote to Hard Relation
```

---

# 15. Captura

Adicionar informação precisa exigir pouco esforço.

Exemplo:

> Preciso comprar uma cômoda para o quarto do bebê antes de dezembro.

O sistema pode extrair:

```text
Cômoda
Type: Item

Relation:
→ Quarto do bebê
→ Bebê

Intent:
Comprar

TemporalConstraint:
Antes de dezembro
```

---

# 16. Inbox

Conteúdo ainda não classificado entra na Inbox.

```text
Inbox

🎥 vídeo
📄 artigo
📝 pensamento
📕 livro
🔗 página
```

O usuário não precisa organizar durante a captura.

---

# 17. Captura de links

Fluxo esperado:

```text
Browser / YouTube / Social App
             ↓
           Share
             ↓
         Life Graph
```

O sistema extrai:

```text
Título
URL
Fonte
Autor
Thumbnail
Descrição
Tipo
```

Depois busca objetos semanticamente próximos.

```text
Relacionar com:

Estoicismo       92%
Filosofia        84%
Marco Aurélio    79%
```

---

# 18. Interface 2.5D

A experiência principal será espacial, mas não completamente 3D.

Exemplo:

```text
                  Família

         Bebê               Casa


                    Eu


      Finanças           Trabalho

              Conhecimento
```

Elementos visuais podem representar:

```text
tamanho      → relevância
proximidade  → relação
agrupamento  → cluster
intensidade  → atividade
profundidade → abstração
```

---

# 19. Semantic Zoom

Zoom não deve significar apenas aumentar geometricamente a tela.

Ele deve mudar o nível conceitual apresentado.

```text
MINHA VIDA

       ↓

Conhecimento

       ↓

Filosofia

       ↓

Estoicismo

       ↓

Marco Aurélio
Meditações
Epicteto
Vídeos
Artigos
```

---

# 20. Local Graph

Ao entrar em um Node:

```text
               Bebê

      Quarto   Saúde   Família

      Cômoda   Médico  Sogros
```

O usuário poderá controlar:

```text
Depth 1
Depth 2
Depth 3
```

---

# 21. Global Graph

Pode existir um modo global.

Seu objetivo será principalmente:

```text
exploração
descoberta
análise
contemplação
```

Não será a principal interface operacional.

---

# 22. Views

Os mesmos dados podem ser apresentados de formas diferentes.

```text
Graph
List
Cards
Table
Timeline
Calendar
Gallery
Map
```

Uma View é uma query sobre o grafo.

Não possui seus próprios dados.

---

# 23. Tempo como dimensão

Tempo deve fazer parte do próprio modelo.

```text
TemporalAnchor

start
end
deadline
period
recurrence
temporal relevance
```

Exemplo:

```text
                 Bebê

OUT        NOV        DEZ        JAN
 │          │          │          │
chá       cômoda      sogros    nascimento
```

---

# 24. Calendário

O Life Graph não precisa substituir calendários existentes.

Pode integrar:

```text
Google Calendar
Apple Calendar
outros
```

O calendário registra o compromisso.

O Life Graph fornece seu contexto.

```text
Consulta
14:30

   ↓

Bebê
└── Gestação
    ├── exames
    ├── notas
    ├── perguntas
    └── documentos
```

---

# 25. Busca textual

Exemplo:

```text
"Marco Aurélio"
```

---

# 26. Busca estruturada

Exemplo:

```text
Type = Book
Status = Reading
```

---

# 27. Busca semântica

Exemplo:

> coisas que salvei relacionadas a como lidar melhor com problemas

Pode retornar:

```text
Estoicismo
Meditações
Epicteto
Vídeo X
Nota Y
```

---

# 28. Conversação com o grafo

O usuário poderá fazer perguntas:

> Quais livros de filosofia eu possuo?

> Quais vídeos sobre estoicismo ainda não vi?

> Onde está meu livro Meditações?

> O que estou planejando para o bebê em dezembro?

> O que já pesquisei sobre compra de casa?

---

# 29. Nova dimensão: Personal Context Backend

O Life Graph não deve existir apenas como aplicação visual.

Ele também será uma **plataforma de contexto pessoal**.

Arquitetura:

```text
          Human Interfaces

 Web App      Mobile App

       │          │
       └────┬─────┘
            │
            ▼
     ┌───────────────┐
     │ Life Graph API│
     └───────┬───────┘
             │
             ▼
     ┌───────────────┐
     │ Domain Layer  │
     └───────┬───────┘
             │
             ▼
        Life Graph
```

Agentes entram através de adapters.

---

# 30. API

A API deve ser a interface principal do domínio.

Possíveis clientes:

```text
Web
Mobile
CLI
Integrations
Automation
MCP Server
```

O domínio nunca deve depender diretamente do MCP.

---

# 31. MCP

MCP funciona como uma interface especializada para agentes.

Arquitetura:

```text
ChatGPT
Claude
Codex
Other Agent
     │
     │ MCP
     ▼
┌─────────────────┐
│ Life Graph MCP  │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ Life Graph API  │
└────────┬────────┘
         │
         ▼
     Domain Layer
         │
         ▼
      Graph Data
```

---

# 32. Ferramentas MCP iniciais

O conjunto inicial deve permanecer pequeno.

```text
search_graph
get_node
get_context

create_node
update_node

create_relation
remove_relation

capture_resource

suggest_changes
apply_changes
```

---

# 33. get_context

Uma das operações fundamentais.

Exemplo:

```text
get_context(
    subject = "comprar casa",
    depth = 2
)
```

Resposta conceitual:

```text
Comprar casa
├── Finanças
│   ├── Mise de fonds
│   ├── CELIAPP
│   └── Hipoteca
│
├── Localização
│   └── bairros
│
├── Família
│   └── necessidades
│
└── Resources
    ├── imóvel A
    └── artigo X
```

O agente recebe apenas contexto relevante.

Não todo o banco.

---

# 34. Fluxo Agent → Life Graph

Exemplo de conversa:

> Estou querendo estudar estoicismo. Tenho Meditações e achei este vídeo sobre Marco Aurélio.

O agente consulta:

```text
search_graph("estoicismo")
search_graph("Meditações")
search_graph("Marco Aurélio")
```

Depois pode executar:

```text
create_node("Estoicismo")

create_node("Marco Aurélio")

create_relation(
    Meditações,
    Marco Aurélio,
    written_by
)

create_relation(
    Meditações,
    Estoicismo,
    about
)

capture_resource(video)
```

O usuário termina a conversa.

No Life Graph:

```text
                 Filosofia

                     │

                 Estoicismo
                /     │      \
               /      │       \
       Meditações   Vídeo   Marco Aurélio
```

O resultado da conversa tornou-se parte visível do grafo.

---

# 35. Atualização visual

Quando possível, alterações podem aparecer em tempo real.

```text
Agent
   ↓
MCP
   ↓
API
   ↓
Database
   ↓
Event Stream
   ↓
Life Graph UI
```

O usuário poderá literalmente ver Nodes e Relations surgirem.

---

# 36. AgentIdentity

Alterações externas devem identificar seu autor.

```text
AgentIdentity

id
provider
name
connection
permissions
createdAt
```

Exemplos:

```text
ChatGPT
Claude
Codex
Local Agent
```

---

# 37. Provenance

Todo objeto criado ou alterado precisa conhecer sua origem.

```text
Provenance

createdBy
createdVia
source
sourceReference
createdAt
```

Exemplo:

```text
CreatedBy:
ChatGPT

CreatedVia:
MCP

Source:
Conversation

CreatedAt:
2026-10-02
```

---

# 38. Proveniência das relações

Exemplo:

```text
Meditações
    │
    └── Estoicismo
```

Ao abrir:

```text
Relation:
about

Created by:
ChatGPT

Source:
Conversation

Confidence:
0.93
```

---

# 39. GraphChangeSet

Uma interação de agente pode gerar diversas alterações.

Elas devem ser agrupadas.

```text
GraphChangeSet #8392

Actor:
ChatGPT

Source:
MCP

Changes:

+ Node Estoicismo
+ Node Marco Aurélio

+ Relation
  Meditações → Marco Aurélio

+ Relation
  Meditações → Estoicismo
```

---

# 40. Undo

Um GraphChangeSet deve poder ser revertido.

Interface:

```text
ChatGPT fez 4 alterações

[Ver alterações]
[Desfazer]
```

Isso é fundamental para confiar em agentes com permissão de escrita.

---

# 41. Agent Permissions

Permissões podem ser divididas em scopes.

```text
lifegraph.read

lifegraph.write

lifegraph.resources

lifegraph.calendar

lifegraph.share

lifegraph.delete
```

---

# 42. Níveis de ação

## Safe

```text
search
read
semantic_search
get_context
```

## Write

```text
create_node
create_relation
capture_resource
update_metadata
```

## Sensitive

```text
delete_node
bulk_delete
share_graph
change_permissions
calendar_write
```

Ações sensíveis podem exigir confirmação.

---

# 43. Agent Suggestions

Nem toda sugestão precisa modificar imediatamente o grafo.

O agente pode produzir:

```text
Suggested changes

+ criar Node Epicteto

+ relacionar vídeo com Estoicismo

+ adicionar tag filosofia

+ adicionar Resource à coleção
```

O usuário pode aceitar tudo ou parcialmente.

---

# 44. IA interna opcional

O Life Graph poderá possuir IA própria.

Porém ela não é requisito.

O usuário poderá utilizar agentes externos.

Isso evita uma dependência econômica permanente de LLMs internos.

---

# 45. Semantic Layer

Arquitetura:

```text
USER GRAPH
Nodes
Relations
Properties
Collections

        ↓

SEMANTIC LAYER
Embeddings
Similarity
Clustering
Ranking

        ↓

AI LAYER
Classification
Extraction
Summaries
Suggestions
Natural Language
```

---

# 46. Estratégia de custo

Evitar:

```text
abrir tela → LLM

navegar → LLM

mover Node → LLM

filtrar → LLM

background agent constante → LLM
```

Preferir:

```text
novo conteúdo
→ enriquecimento

pergunta complexa
→ LLM

reorganização solicitada
→ LLM

navegação
→ zero LLM

Graph rendering
→ zero LLM

filtering
→ zero LLM
```

---

# 47. SharedGraphView

O usuário poderá compartilhar apenas parte de seu grafo.

```text
SharedGraphView

RootNode
IncludedNodes
IncludedRelations
MaxDepth
MaxNodes
Visibility
Mode
```

---

# 48. SharedGraphView — segurança

O backend deve garantir:

> Quem recebe acesso a uma View nunca pode atravessar relações para Nodes não autorizados.

Não basta esconder os Nodes na interface.

A API não deve retorná-los.

---

# 49. Snapshot e Live Share

## Snapshot

Representa o estado do grafo no momento do compartilhamento.

## Live

Atualizações futuras aparecem automaticamente.

---

# 50. Constellations

Evolução futura:

```text
Constellation

"Como estou aprendendo Estoicismo"

12 Nodes
4 Books
3 Videos
2 Articles
3 Notes
```

Uma Constellation preserva não apenas o conteúdo, mas suas relações.

---

# 51. Futuro social

Possibilidades:

```text
Explore

Follow

Save

Fork

Import selected Nodes
```

Não pertence ao MVP.

---

# 52. Modelo SaaS desde o MVP

Mesmo que o produto comece como aplicação pessoal, a arquitetura deve permitir desde cedo:

```text
Free
Premium
```

O Free precisa ser útil o suficiente para demonstrar o valor real.

O Premium deve vender:

```text
escala
histórico
automação
integrações
IA
compartilhamento avançado
```

Não simplesmente desbloquear funções básicas arbitrariamente.

---

# 53. Free — objetivo

O Free deve permitir que uma pessoa realmente construa um pequeno Life Graph e entenda o produto.

Ela deve conseguir validar:

```text
Nodes
Relations
Resources
Collections
2.5D Graph
Semantic connections
MCP
Agent interaction
```

---

# 54. Premium — objetivo

Premium deve atender quem passa a utilizar o Life Graph como infraestrutura real de sua vida.

Principalmente usuários que:

```text
armazenam muito conteúdo;
usam vários agentes;
querem histórico extenso;
usam integrações;
compartilham grafos;
automatizam workflows;
fazem grandes importações.
```

---

# 55. Limites sugeridos para MVP

Os números abaixo são limites iniciais de produto e devem ser configuráveis.

| Capability | Free | Premium |
|---|---:|---:|
| Nodes | 500 | 10.000+ |
| Relations | 2.000 | 50.000+ |
| Resources | 100 | 5.000+ |
| Collections | 10 | Ilimitadas / Fair Use |
| Custom Types | 5 | Ilimitados / Fair Use |
| Custom Properties | 20 | Ilimitadas / Fair Use |
| Agent Connections | 1 | Múltiplas |
| MCP Read | Sim | Sim |
| MCP Write | Limitado | Amplo |
| MCP write operations | ~100/mês | Fair Use / quota alta |
| Shared Graph Views | 3 | 100+ |
| Nodes por Shared View | 25 | 250+ |
| Semantic indexing | Sim | Sim |
| AI enrichment interno | Pequena quota | Quota maior |
| Change history | 30 dias | 1 ano ou mais |
| Undo de ChangeSet | Sim | Sim |
| Calendar integration | Não / limitada | Sim |
| Bulk import | Não | Sim |
| Advanced semantic search | Limitada | Sim |
| Graph export | Sim | Sim |
| API access | Básico | Completo |
| Priority processing | Não | Sim |

---

# 56. Regra importante sobre limites

O usuário não deve perder seus dados quando atingir um limite.

Exemplo:

```text
500 Nodes atingidos
```

O sistema pode impedir novas criações, mas continua permitindo:

```text
visualizar;
editar;
exportar;
excluir;
acessar seus dados.
```

Nunca bloquear acesso a informações já armazenadas como mecanismo de upgrade.

---

# 57. MCP Free

MCP deve existir no plano gratuito.

É parte da proposta central.

O usuário Free pode, por exemplo:

```text
1 Agent Connection

Read access

Limited write operations
```

Isso permite experimentar:

> ChatGPT gerindo meu Life Graph.

Sem Premium obrigatório desde o primeiro uso.

---

# 58. MCP Premium

Premium pode desbloquear:

```text
multiple agents

larger write quota

bulk operations

advanced scopes

automation

agent-to-agent workflows

longer history

background processing
```

---

# 59. BYO Agent

O usuário pode conectar seu próprio agente.

Exemplos:

```text
ChatGPT
Claude
Codex
Local LLM
Custom Agent
```

Nesse modelo:

> O custo de raciocínio pertence ao agente escolhido.

O Life Graph cobra principalmente por:

```text
storage
graph scale
sync
semantic infrastructure
history
integrations
sharing
platform automation
```

---

# 60. Built-in AI

O produto também poderá oferecer IA própria.

Nesse caso haverá uma quota separada.

Exemplo:

```text
Free

25 AI enrichments / mês


Premium

500 AI enrichments / mês
```

Valores serão definidos posteriormente com base no custo real.

---

# 61. O que não deve virar Premium

Algumas capacidades representam direitos básicos do usuário.

Não devem ser artificialmente bloqueadas.

```text
exportar dados;

visualizar seus próprios dados;

editar dados existentes;

excluir dados;

consultar origem/proveniência;

desfazer alterações recentes de agentes;

controlar permissões.
```

---

# 62. MVP — Core

O MVP deve possuir:

```text
Account

Nodes

Relations

Types

Properties

Tags

Collections

Resources

Inbox

Local 2.5D Graph

Basic Semantic Search

Embeddings

Semantic suggestions

GraphChangeSet

Provenance

Basic API

Basic MCP Server

Agent authentication

Basic permissions

Export
```

---

# 63. MVP — Agent Experience

Fluxo mínimo que precisa funcionar:

```text
User connects ChatGPT/Claude/etc.

          ↓

Agent searches Life Graph

          ↓

Agent receives relevant context

          ↓

Agent creates or updates Nodes

          ↓

GraphChangeSet generated

          ↓

Life Graph UI updates

          ↓

User sees changes

          ↓

User can inspect or undo
```

Esse fluxo é parte da validação central do MVP.

---

# 64. MVP — visual

O MVP não precisa implementar o universo visual inteiro.

Precisa validar:

```text
Local Graph

Semantic Zoom básico

Node Inspector

Collections

Inbox

Recent changes

Agent Changes
```

---

# 65. Tela Agent Changes

Uma tela específica deve mostrar:

```text
Recent Agent Activity


ChatGPT
2 min atrás
+ 3 Nodes
+ 5 Relations

[View]


Claude
Ontem
+ 1 Resource
~ 2 Nodes updated

[View]
```

---

# 66. Tela de GraphChangeSet

Exemplo:

```text
ChatGPT
Conversation: Filosofia
2 October 2026

Added:

+ Estoicismo

+ Marco Aurélio

Relations:

+ Meditações → Marco Aurélio
+ Meditações → Estoicismo


[Undo]
```

---

# 67. MVP — fora do escopo

Inicialmente não implementar:

```text
Full 3D

Social network

Marketplace

Real-time multi-user collaboration

Autonomous background agents

Complex calendar management

Email ingestion

Full file sync

Plugin marketplace

Advanced Constellations

Agent marketplace

Advanced workflow builder
```

---

# 68. Fase 2

```text
Google Calendar

Apple Calendar

Timeline

Advanced SharedGraphView

Mobile Share Extension

Books

PDF ingestion

Advanced resource extraction

Natural language search

Advanced semantic clustering

Multiple MCP connections

Longer version history
```

---

# 69. Fase 3

```text
Constellations

Collaborative graphs

Recurring automation

Background agents

Advanced Agent Policies

Browser Extension

Bulk Import

External integrations

Agent workflows

Public API ecosystem
```

---

# 70. Futuro: Graph Automations

Possibilidade:

```text
WHEN
new Resource is added

IF
similarity with Filosofia > 0.85

THEN
suggest relation with Filosofia
```

Ou:

```text
Every Sunday

Review Inbox

Generate organization suggestions
```

---

# 71. Futuro: Agents

Agentes poderão receber tarefas como:

> Organize minha Inbox.

> Revise o que adicionei esta semana.

> Procure relações que talvez eu não tenha percebido.

> Mostre assuntos que abandonei.

> Prepare o contexto da próxima semana.

Idealmente produzindo ChangeSets revisáveis.

---

# 72. Casos de uso

## Filosofia

```text
Filosofia
├── Estoicismo
│   ├── Marco Aurélio
│   ├── Meditações
│   └── vídeos
├── Ética
└── História
```

---

## Bebê

```text
Bebê
├── Quarto
├── Saúde
├── Compras
├── Família
└── Eventos
```

---

## Biblioteca

```text
Livros

Possuo
Quero ler
Lendo
Finalizados

Location:
Sala
Escritório
Digital
Emprestado
```

---

## Compra de casa

```text
Casa
├── Hipoteca
├── Entrada
├── Bairros
├── Imóveis
├── Escolas
├── Garderies
└── Recursos
```

---

# 73. Caso de uso Agent-first

Conversa:

> Encontrei três casas interessantes. Guarde essas três e relacione com meu projeto de compra de casa.

Agente:

```text
get_context("compra de casa")

capture_resource(house1)
capture_resource(house2)
capture_resource(house3)

create_relation(...)
```

Life Graph:

```text
              Comprar casa

       ┌──────────┼──────────┐

    Casa A      Casa B      Casa C

       │           │          │

    Bairro X    Bairro Y   Bairro X
```

Nenhum trabalho duplicado do usuário.

---

# 74. Diferenciação

O produto não deve ser vendido apenas como:

```text
Note-taking app

Second Brain

Knowledge Graph

Bookmark Manager

AI memory
```

Posicionamento desejado:

> **Personal Context Infrastructure.**

Ou, para público geral:

> **Um mapa vivo da sua vida que você e suas IAs podem usar.**

---

# 75. Comparação conceitual

Ferramentas existentes tendem a responder:

> Onde minhas informações estão?

O Life Graph deve progressivamente responder:

> Como as coisas da minha vida se relacionam?

E:

> Como qualquer agente autorizado pode utilizar esse contexto sem eu reconstruí-lo em cada conversa?

---

# 76. Moat potencial

O valor acumulado do produto não será apenas o volume de notas.

Será:

```text
Nodes
+
Relations
+
Personal schema
+
Semantic history
+
Temporal context
+
Provenance
+
Agent activity
```

Com o tempo, o grafo passa a representar uma estrutura pessoal difícil de reconstruir em outro lugar.

Por isso exportabilidade e propriedade dos dados também precisam fazer parte do produto.

---

# 77. Exportabilidade

O usuário deve conseguir exportar seus dados.

Possíveis formatos futuros:

```text
JSON
Markdown
Graph format
CSV
API
```

Isso reforça a tese:

> O contexto pertence ao usuário.

---

# 78. Referências de mercado

Ferramentas importantes para estudar:

```text
TheBrain
Capacities
Heptabase
Anytype
Tana
Obsidian
```

Principal aprendizado:

```text
TheBrain
→ navegação visual

Capacities
→ interface, objects, types, collections

Heptabase
→ espaço visual

Anytype
→ object model

Tana
→ structured nodes

Obsidian
→ graph ecosystem
```

O objetivo não é reproduzir nenhuma delas.

---

# 79. O que não queremos construir

Life Graph não deve se tornar prioritariamente:

```text
Notion clone

Obsidian clone

Calendar app

Task manager

Social network

Chatbot

Bookmark manager

File manager
```

Todos podem ser fontes ou interfaces do grafo.

Nenhum deles é o produto principal.

---

# 80. Métrica conceitual de sucesso

Depois de meses de utilização, o usuário deveria conseguir perguntar:

> Por que salvei isso?

> Com o que isso estava relacionado?

> Quem adicionou essa informação?

> Em qual conversa isso surgiu?

> O que já sei sobre isso?

> O que eu planejava fazer?

> O que acontece em breve relacionado a isso?

> Qual agente modificou isso?

E receber respostas derivadas de seu próprio contexto persistente.

---

# 81. Experiência desejada

Inicialmente:

```text
"Estou colocando coisas no Life Graph."
```

Depois:

```text
"Estou construindo um mapa da minha vida."
```

Depois:

```text
"Minhas IAs conhecem meu contexto através do Life Graph."
```

Finalmente:

```text
"O Life Graph é a infraestrutura de contexto da minha vida digital."
```

---

# 82. North Star

```text
Capture
   ↓
Structure
   ↓
Connect
   ↓
Remember
   ↓
Share Context
   ↓
Agent Acts
   ↓
Human Reviews
   ↓
Graph Evolves
```

---

# 83. Princípio arquitetural final

O **grafo é o produto**.

A interface 2.5D é sua representação humana.

A API é sua interface programática.

O MCP é sua interface para agentes.

Embeddings ajudam a descobrir proximidade.

LLMs ajudam a interpretar e organizar.

Agentes ajudam a agir.

Mas:

> **O contexto persistente continua pertencendo ao Life Graph e ao usuário.**