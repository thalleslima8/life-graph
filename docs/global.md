# Life Graph

## 1. Visão

**Life Graph** é uma aplicação para organizar a vida como uma rede de assuntos, objetos, recursos, pessoas, eventos e relações.

Em vez de organizar informações principalmente através de pastas, páginas ou listas isoladas, o sistema representa a vida do usuário como um **grafo navegável**, no qual diferentes assuntos se relacionam por:

- contexto;
- significado;
- proximidade semântica;
- relações explícitas;
- tempo;
- pessoas;
- projetos;
- recursos externos;
- localização;
- propriedades definidas pelo próprio usuário.

A proposta central é:

> **Um Life Graph que se organiza quase sozinho e permite navegar pela própria vida por contexto, proximidade semântica e tempo, sem exigir que o usuário mantenha manualmente uma base de conhecimento.**

O produto deve ser **AI-assisted, not AI-dependent**.

A inteligência artificial ajuda a organizar, classificar e sugerir relações, mas a estrutura principal do sistema deve continuar funcional sem depender de chamadas constantes a modelos de IA.

---

# 2. Problema

Ferramentas tradicionais normalmente organizam informações em estruturas rígidas:

```text
Pasta
└── Subpasta
    └── Documento
```

ou:

```text
Projeto
├── Tarefa
├── Nota
└── Arquivo
```

Mas assuntos da vida não seguem uma estrutura hierárquica simples.

Um mesmo objeto pode pertencer simultaneamente a diversos contextos.

Exemplo:

```text
Meditações
├── Filosofia
├── Estoicismo
├── Marco Aurélio
├── História romana
├── Livros que possuo
├── Livros que estou lendo
└── Estante da sala
```

Da mesma maneira:

```text
Cômoda
├── Bebê
├── Quarto do bebê
├── Compras
├── Planejamento financeiro
└── Preparação para nascimento
```

Forçar o usuário a escolher uma única pasta ou categoria remove relações importantes.

O Life Graph busca representar essas conexões diretamente.

---

# 3. Princípios do Produto

## 3.1 Capture first, organize later

Adicionar informação deve ser extremamente simples.

O usuário não deve precisar decidir antecipadamente:

- onde guardar;
- qual pasta usar;
- qual tag criar;
- com quais objetos relacionar;
- qual estrutura utilizar.

O sistema pode sugerir essa organização posteriormente.

---

## 3.2 O grafo deve emergir

O usuário não deve precisar desenhar manualmente todo o grafo.

As conexões podem surgir de três fontes:

### Relações explícitas

Criadas pelo usuário.

```text
Meditações
    └── escrito por → Marco Aurélio
```

### Relações estruturais

Derivadas de propriedades e objetos.

```text
Cômoda
    └── pertence a → Quarto do bebê
```

### Relações semânticas

Inferidas pelo sistema.

```text
Meditações
 - - - relacionado a - - -
Ética
```

---

# 4. Hard Graph e Soft Graph

O Life Graph terá conceitualmente duas camadas de relações.

## Hard Graph

Representa fatos, relações e associações confirmadas.

Exemplo:

```text
Meditações ── written_by ──> Marco Aurélio
Meditações ── owned_by ────> Usuário
Meditações ── located_at ──> Estante da sala
```

Essas relações fazem parte efetiva da estrutura do usuário.

---

## Soft Graph

Representa relações inferidas.

Exemplo:

```text
Meditações - - - Estoicismo
              0.94

Meditações - - - Ética
              0.78
```

A origem pode ser:

- embeddings;
- similaridade textual;
- propriedades compartilhadas;
- recursos semelhantes;
- coocorrência;
- contexto;
- IA.

Soft Relations devem ser claramente diferenciadas das relações confirmadas.

O usuário poderá:

- aceitar;
- ignorar;
- remover;
- transformar uma sugestão em relação permanente.

---

# 5. Conceito de Node

Um `Node` representa alguma entidade relevante na vida do usuário.

Inicialmente, o sistema deve evitar uma ontologia excessivamente rígida.

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

Um Node pode possuir:

```text
id
name
description
type
properties
tags
createdAt
updatedAt
relations
resources
temporal information
semantic embedding
```

---

# 6. Tipos

O usuário poderá criar tipos personalizados.

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

Um tipo define quais propriedades normalmente existem naquele objeto.

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

# 7. Tags

Tags funcionam como classificadores flexíveis.

Exemplo:

```text
#filosofia
#estoicismo
#quero-ler
#importante
```

Tags não substituem relações.

Elas complementam o sistema oferecendo:

- filtros;
- agrupamentos;
- buscas;
- views;
- classificação simples.

---

# 8. Collections

Collections são agrupamentos dinâmicos ou manuais.

Elas podem começar vazias e ser preenchidas conforme o usuário cria conteúdo.

Exemplo:

```text
Filosofia
Livros
Bebê
Casa
Receitas
Pessoas
```

Uma collection pode funcionar como uma query.

Exemplo:

```text
Collection: Quero ler

Type = Book
Status = WantToRead
```

Outro exemplo:

```text
Collection: Filosofia

Tag = filosofia
OR
Relation → Filosofia
```

Collections funcionam também como itens de navegação da aplicação.

O usuário poderá criar seu próprio conjunto de menus.

---

# 9. Properties

Objetos podem possuir propriedades configuráveis.

Tipos possíveis:

```text
Text
Number
Boolean
Date
DateTime
URL
Person
Node reference
Location
Status
Select
Multi-select
Rating
```

Exemplo:

```text
Book

Status:
- Want to read
- Reading
- Finished
- Abandoned
```

Essas propriedades automaticamente podem virar:

- filtros;
- agrupamentos;
- menus;
- visualizações;
- consultas.

---

# 10. Resources

Conteúdo externo deve ser tratado como objeto relevante do sistema, não apenas como texto armazenado em uma nota.

Um `Resource` pode representar:

```text
Video
Article
Web page
Social post
Podcast
PDF
Image
Book
Document
File
```

Estrutura possível:

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

Exemplo:

```text
YouTube Video
"Why Marcus Aurelius Wrote Meditations"

Relacionado a:
→ Marco Aurélio
→ Estoicismo
→ Meditações
```

---

# 11. Captura de conteúdo

Capturar informação deve ser uma das experiências mais rápidas do produto.

## Entrada manual

O usuário escreve:

> Preciso comprar uma cômoda para o quarto do bebê antes de dezembro.

O sistema pode identificar:

```text
Item:
Cômoda

Relacionado a:
Quarto do bebê
Bebê

Possible action:
Comprar

Temporal constraint:
Antes de dezembro
```

---

## Compartilhamento pelo celular

Fluxo:

```text
YouTube
↓
Share
↓
Life Graph
```

O sistema recebe a URL e extrai automaticamente:

```text
Título
Thumbnail
Autor
Fonte
Descrição
Tipo
```

Depois sugere:

```text
Relacionar com:

Estoicismo       92%
Filosofia        84%
Marco Aurélio    79%
```

Idealmente:

```text
1 toque
↓
Salvar
```

---

# 12. Inbox

Todo conteúdo que ainda não possui uma classificação definitiva pode entrar em uma Inbox.

Exemplo:

```text
Inbox

🎥 vídeo
📄 artigo
📝 pensamento
📕 livro
🔗 site
```

O usuário poderá organizar manualmente ou aceitar sugestões.

A Inbox evita exigir organização durante a captura.

---

# 13. Visualização 2.5D

O Life Graph não deve começar como um grafo 3D irrestrito.

A visualização principal será uma interface **2.5D**, com sensação espacial e profundidade sem sacrificar usabilidade.

Exemplo:

```text
                  Família

         Bebê               Casa

                    Eu

      Finanças           Trabalho

              Conhecimento
```

Elementos visuais podem representar:

- tamanho → relevância;
- proximidade → similaridade/contexto;
- agrupamento → cluster;
- intensidade → atividade recente;
- profundidade → nível de abstração.

---

# 14. Semantic Zoom

O zoom deve mudar também o nível semântico apresentado.

Não deve ser apenas zoom geométrico.

## Nível 1

```text
Minha Vida

Família
Casa
Trabalho
Finanças
Conhecimento
Lazer
```

## Nível 2

```text
Conhecimento

Filosofia
História
Tecnologia
Música
```

## Nível 3

```text
Filosofia

Estoicismo
Existencialismo
Ética
História da Filosofia
```

## Nível 4

```text
Estoicismo

Marco Aurélio
Epicteto
Sêneca
Meditações
Vídeos
Artigos
Notas
```

A navegação deve gerar a sensação de **entrar em um assunto**.

---

# 15. Local Graph

Ao abrir qualquer Node, deve ser possível visualizar apenas seu contexto imediato.

Exemplo:

```text
                Bebê

    Quarto      Saúde      Família

    Cômoda      Consulta   Sogros

    Berço       Exames     Pais
```

O usuário poderá controlar:

```text
Depth = 1
Depth = 2
Depth = 3
```

Isso evita transformar a visualização em milhares de pontos ilegíveis.

---

# 16. Global Graph

Pode existir uma visualização global da vida.

Porém ela deve ser principalmente:

- exploratória;
- contemplativa;
- analítica.

Não deve ser a principal interface operacional.

O sistema poderá agrupar automaticamente áreas do grafo em clusters.

---

# 17. Tempo como dimensão

Tempo não deve existir apenas no calendário.

Nodes podem possuir:

```text
start date
end date
deadline
recurrence
period
temporal relevance
```

Exemplo:

```text
                 Bebê

OUT        NOV        DEZ        JAN
 │          │          │          │
chá       cômoda      sogros    nascimento
```

O usuário poderá fazer consultas como:

> Mostrar tudo relacionado ao bebê nos próximos três meses.

> Quais assuntos da minha vida estão concentrados em dezembro?

> O que deveria estar concluído antes da chegada dos meus sogros?

---

# 18. Integração com calendário

O Life Graph não deve tentar substituir inicialmente:

- Google Calendar;
- Apple Calendar;
- outros calendários consolidados.

O calendário continua sendo a fonte de compromissos.

O Life Graph adiciona contexto.

Exemplo:

```text
Consulta
14:30

Relacionado a:

Bebê
└── Gestação
    ├── exames
    ├── notas
    ├── perguntas
    └── documentos
```

Uma integração futura poderá permitir:

```text
Node/Event ↔ Calendar Event
```

---

# 19. Busca

Devem coexistir diferentes tipos de busca.

## Busca textual

```text
"Marco Aurélio"
```

## Busca por propriedades

```text
Type = Book
Status = Reading
```

## Busca semântica

```text
"coisas sobre como lidar melhor com problemas"
```

Pode encontrar:

```text
Estoicismo
Meditações
Dichotomy of Control
Vídeo X
Nota Y
```

mesmo sem correspondência literal.

---

# 20. Natural Language Query

O usuário poderá conversar com seu Life Graph.

Exemplos:

> Quais livros de filosofia eu já possuo?

> Quais vídeos sobre estoicismo ainda não assisti?

> O que eu salvei sobre hipoteca?

> Onde está meu livro Meditações?

> O que está acontecendo relacionado ao bebê em dezembro?

> Mostre coisas relacionadas a filosofia que salvei nos últimos seis meses.

---

# 21. Camada semântica

Cada Node ou Resource relevante poderá possuir um embedding.

Arquitetura conceitual:

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

# 22. AI-assisted, not AI-dependent

O funcionamento básico da aplicação não deve depender de LLM.

Operações sem IA:

```text
Criar Node
Editar Node
Criar Relation
Criar Collection
Aplicar filtro
Navegar no grafo
Abrir Resource
Buscar propriedades
Renderizar grafo
```

IA pode ser utilizada para:

```text
extrair entidades;
sugerir tipo;
sugerir tags;
sugerir relações;
classificar Resources;
resumir conteúdo;
interpretar linguagem natural;
detectar clusters;
responder perguntas;
```

---

# 23. Estratégia de custo de IA

Evitar:

```text
abrir tela → LLM
mover Node → LLM
navegar → LLM
filtrar → LLM
background agent constante → LLM
```

Preferir:

```text
novo conteúdo
→ enriquecimento eventual

nova URL
→ classificação

pergunta inteligente
→ chamada de modelo

solicitação de reorganização
→ chamada de modelo
```

Embeddings e algoritmos tradicionais devem resolver boa parte da similaridade.

---

# 24. Agents

Agentes autônomos não são necessários para o MVP.

No futuro podem existir funcionalidades como:

```text
"Organize minha Inbox."

"Revise os itens adicionados esta semana."

"Encontre relações que talvez eu não tenha percebido."

"Identifique assuntos abandonados."

"Prepare meu planejamento da próxima semana."
```

Mas esses agentes devem preferencialmente gerar **propostas**, não alterar silenciosamente a estrutura.

Exemplo:

```text
AI Suggestions

+ relacionar vídeo a Estoicismo
+ criar Topic "Epicteto"
+ adicionar #filosofia
+ mover para coleção "Quero assistir"
```

O usuário continua sendo autoridade sobre seu próprio grafo.

---

# 25. Explicabilidade

Toda relação criada automaticamente deve poder responder:

> Por que isso está relacionado?

Exemplo:

```text
Relacionado a "Estoicismo"

Motivos:

- alta similaridade semântica;
- menciona Marco Aurélio;
- três recursos relacionados pertencem ao mesmo cluster.
```

Isso aumenta confiança e reduz sensação de comportamento arbitrário da IA.

---

# 26. Compartilhamento

O usuário poderá compartilhar apenas uma parte de seu grafo.

Nunca será necessário expor o Life Graph inteiro.

Conceito:

```text
SharedGraphView
```

Exemplo:

```text
Minha trilha de Filosofia

Filosofia
├── Estoicismo
│   ├── Meditações
│   ├── Marco Aurélio
│   └── Vídeo
├── Ética
└── Platão
```

O usuário gera uma URL compartilhável.

---

# 27. Shared Graph View

Estrutura conceitual:

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

Exemplo:

```text
RootNode: Filosofia
MaxDepth: 3
MaxNodes: 25
```

---

# 28. Modos de compartilhamento

## Snapshot

Representa o estado do grafo no momento do compartilhamento.

Mudanças futuras não aparecem.

## Live

O compartilhamento acompanha alterações futuras dentro dos critérios definidos.

---

# 29. Privacidade do compartilhamento

Possíveis níveis:

```text
Private
Unlisted
Public
Password protected
```

Regra fundamental:

> Um usuário acessando uma Shared Graph View nunca pode utilizar relações visíveis ou APIs para escapar do subgrafo autorizado.

A filtragem deve acontecer no backend.

Não apenas na interface.

---

# 30. Constellations

Uma evolução futura de Shared Graph View poderá ser chamada de:

**Constellation**

Uma Constellation é uma pequena curadoria compartilhável de conhecimento.

Exemplo:

```text
Como estou aprendendo Estoicismo

12 Nodes
4 Books
3 Videos
2 Articles
3 Notes
```

Ela preserva não apenas os recursos, mas também as relações entre eles.

---

# 31. Possível camada social futura

Constellations públicas poderão futuramente permitir:

```text
Explore
Follow
Save
Fork
Copy selected Nodes
```

Exemplo:

```text
João publicou:

"Introdução à fotografia"

Fotografia
├── Composição
├── Luz
├── Câmeras
└── Fotógrafos
```

Outro usuário poderia importar parte dessa estrutura para seu próprio grafo.

Essa funcionalidade não pertence ao MVP.

---

# 32. Navegação principal

Uma possível Home:

```text
Minha Vida

● Bebê
● Casa
● Filosofia
● Finanças
● Trabalho

Recentemente

○ Estoicismo
○ Cômoda
○ Hipoteca
○ Projeto X
```

A Home não precisa mostrar imediatamente todo o grafo.

Ela deve facilitar entrada em contextos relevantes.

---

# 33. Views

Um mesmo conjunto de objetos pode ser apresentado em diferentes Views.

Possíveis Views:

```text
Graph
List
Cards
Timeline
Calendar
Table
Gallery
Map
```

Exemplo:

```text
Books

Graph View
Table View
Gallery View
```

O objeto permanece o mesmo.

Apenas sua representação muda.

---

# 34. Relação entre View e dados

Views nunca devem possuir os dados primários.

Elas representam queries sobre o grafo.

Exemplo:

```text
View: Livros para ler

Type = Book
Status = WantToRead
```

Assim, adicionar:

```text
Book
Status = WantToRead
```

faz automaticamente o livro aparecer na View.

---

# 35. Possíveis relações

O modelo deve permitir relações genéricas.

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
parent_of
child_of
inspired_by
used_for
about
```

Também pode existir uma relação genérica inicial:

```text
related_to
```

que posteriormente poderá ser especializada.

---

# 36. Peso das relações

Relações podem possuir um peso.

Exemplo:

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

Origem:

```text
user
system
ai
import
```

Exemplo:

```text
Meditações → Estoicismo

Origin: Semantic
Confidence: 0.91
```

---

# 37. Relevância

Nodes podem possuir relevância dinâmica.

Fatores possíveis:

```text
atividade recente;
quantidade de relações;
frequência de acesso;
eventos futuros;
relevância temporal;
interações manuais;
```

Isso pode determinar tamanho ou destaque visual.

---

# 38. Localização física

Objetos físicos poderão possuir localização.

Exemplo:

```text
Meditações

Location:
Estante da sala
```

ou:

```text
Documentos do carro

Location:
Gaveta escritório
```

Isso transforma o sistema também em uma espécie de índice da vida física.

---

# 39. Casos de uso iniciais

## Bebê

```text
Bebê
├── Quarto
│   ├── Cômoda
│   └── Berço
├── Saúde
├── Compras
├── Visitas
└── Eventos
```

---

## Filosofia

```text
Filosofia
├── Estoicismo
│   ├── Marco Aurélio
│   ├── Meditações
│   └── vídeos
├── Existencialismo
└── Ética
```

---

## Biblioteca pessoal

```text
Livros

Possuo
Quero ler
Lendo
Finalizados

Location:
Sala
Escritório
Kindle
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
├── Escola
├── Garderie
└── Documentos
```

---

# 40. MVP

O MVP deve validar principalmente uma hipótese:

> As pessoas consideram útil organizar e navegar suas informações pessoais através de contexto e relações sem precisar estruturar manualmente uma base de conhecimento?

## MVP — obrigatório

### Nodes

Criar, editar e remover Nodes.

### Relations

Relacionar dois Nodes.

### Types

Permitir alguns tipos iniciais e estrutura extensível.

### Tags

Tags personalizadas.

### Collections

Collections criadas pelo usuário.

### Resources

Salvar URLs.

Inicialmente:

```text
Web page
YouTube
Article
```

### Inbox

Área para conteúdo capturado ainda não organizado.

### Graph View

Grafo local 2.5D.

### Semantic Suggestions

Embedding para encontrar Nodes relacionados.

### Search

Texto + filtros básicos.

### Basic AI Capture

Interpretar conteúdo simples e sugerir:

```text
Type
Tags
Relations
```

---

# 41. MVP — idealmente não incluir

Para evitar escopo excessivo:

```text
grafo 3D;
rede social;
marketplace;
collaborative editing;
agentes autônomos;
Google Calendar completo;
Apple Calendar;
mobile apps nativos;
offline-first complexo;
importação completa do Obsidian;
email;
contact sync;
browser extension sofisticada;
sistema de plugins;
Constellations públicas.
```

---

# 42. Fase 2

Após validar o núcleo:

```text
Calendar integration
Timeline
Share Graph View
Advanced Collections
Mobile share target
Better resource extraction
AI summaries
Natural-language graph search
Physical locations
Books
PDFs
```

---

# 43. Fase 3

```text
Constellations
Public graph sharing
Collaborative graphs
Graph import/export
Agents
Recurring AI organization
Browser extension
Personal knowledge assistant
Calendar planning
External integrations
```

---

# 44. Fase SaaS

Caso o produto evolua para SaaS:

## Free

```text
Graph
Nodes
Relations
Collections
Manual organization
Limited Resources
Limited AI
```

## Plus

```text
Semantic relations
More Resources
AI classification
AI search
Summaries
Calendar integration
Sharing
```

## Pro

```text
Advanced AI
Agents
Large imports
Automation
Advanced sharing
Collaboration
Version history
```

O modelo deverá ser definido posteriormente com base no custo real por usuário.

---

# 45. Diferenciação

O produto não deve tentar competir apenas como:

> aplicativo de notas com grafo.

Nem apenas:

> segundo cérebro.

O posicionamento desejado é:

> **Um mapa contextual da vida do usuário.**

A principal diferença deve surgir da combinação de:

```text
Life Graph
+
Semantic relationships
+
Temporal context
+
Low-maintenance organization
+
AI-assisted capture
+
Semantic zoom
```

---

# 46. Referências conceituais

Produtos existentes relevantes para estudar:

```text
TheBrain
Capacities
Heptabase
Anytype
Tana
Obsidian
```

Elementos interessantes:

### TheBrain

Visualização de grafo e navegação entre relações.

### Capacities

Objetos, tipos, properties, collections e interface.

### Heptabase

Organização espacial.

### Anytype

Modelo baseado em Objects + Types + Relations.

### Tana

Nodes estruturados e schemas flexíveis.

### Obsidian

Ecossistema de relações, graph e Canvas.

Objetivo:

> aprender com essas interfaces sem criar apenas uma cópia de alguma delas.

---

# 47. O que não queremos construir

O Life Graph não deve virar prioritariamente:

```text
Notion clone
Obsidian clone
task manager
calendar app
social network
bookmark manager
AI chatbot
file manager
```

Ele poderá integrar aspectos dessas categorias, mas o núcleo continuará sendo o grafo contextual da vida.

---

# 48. Métrica de sucesso conceitual

Um teste simples:

Se o usuário salvar:

```text
100 recursos
30 pessoas
20 projetos
15 assuntos
10 objetivos
eventos
livros
notas
```

ele deveria conseguir meses depois perguntar:

> Por que eu salvei isso?

> Com o que isso estava relacionado?

> Onde está aquela coisa?

> O que eu já sei sobre esse assunto?

> O que estava planejando fazer?

> O que acontece em breve relacionado a isso?

Sem precisar lembrar em qual pasta colocou a informação.

---

# 49. Experiência desejada

O produto deve passar gradualmente de:

```text
"Estou colocando informações em um app."
```

para:

```text
"Estou construindo um mapa da minha vida."
```

e eventualmente:

```text
"O sistema lembra como as coisas da minha vida se relacionam."
```

---

# 50. North Star

A experiência ideal pode ser representada por:

```text
Capture
    ↓
Understand
    ↓
Connect
    ↓
Navigate
    ↓
Remember
    ↓
Act
```

Ou:

> **Capture sem organizar. Explore sem procurar. Entenda relações sem precisar construí-las todas manualmente.**

---

# 51. Princípio arquitetural final

O grafo do usuário é o produto.

A IA não é o produto.

O calendário não é o produto.

As notas não são o produto.

Os Resources não são o produto.

Todos são maneiras diferentes de enriquecer e interagir com:

> **um modelo contextual e evolutivo da vida do usuário.**