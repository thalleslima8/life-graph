# Life Graph

> **Capture without organizing. Explore without searching. Understand
> relationships without building them all by hand.**

Life Graph is an application for organizing your life as a network of topics,
objects, resources, people, events and the relationships between them.

Instead of forcing information into folders, pages or isolated lists, Life
Graph represents your life as a **navigable graph**, where things connect
through context, meaning, semantic proximity, explicit relations, time, people,
projects, external resources, location and user-defined properties.

The goal: **a life graph that almost organizes itself**, letting you navigate
your own life by context, semantic proximity and time, without having to
maintain a knowledge base by hand.

> **Status:** development platform in place (epic E0), no product features yet.
> The current product specification (in Portuguese) is in
> [`docs/global_v2.md`](docs/global_v2.md); the roadmap is in
> [`docs/epics/README.md`](docs/epics/README.md). See [Development](#development)
> to build and run it.

## The problem

Traditional tools organize information hierarchically:

```text
Folder
└── Subfolder
    └── Document
```

But life doesn't follow a single hierarchy. The same thing belongs to many
contexts at once:

```text
Meditations
├── Philosophy
├── Stoicism
├── Marcus Aurelius
├── Books I own
├── Books I'm reading
└── Living room bookshelf
```

Forcing a single folder or category throws away those connections. Life Graph
represents them directly.

## Principles

- **Capture first, organize later.** Adding information should be effortless.
  You never have to decide up front where something goes, which tags to use or
  what it relates to. The system can suggest that later, and anything
  unclassified waits in an **Inbox**.
- **The graph emerges.** Connections come from three sources:
  - **explicit relations** you create (`Meditations ─ written_by → Marcus Aurelius`);
  - **structural relations** derived from properties (`Dresser ─ belongs_to → Nursery`);
  - **semantic relations** inferred by the system (`Meditations - - - Ethics`).
- **AI-assisted, not AI-dependent.** AI helps extract, classify and suggest,
  but creating, editing, filtering, searching by property and navigating the
  graph all work without any model call. Embeddings and traditional algorithms
  handle most similarity work, and models are called only on meaningful events
  such as new content, a new URL or a natural-language question.
- **You are the authority over your graph.** AI produces *proposals*, not
  silent changes, and every automatic relation can answer *"why is this
  related?"*

## Core concepts

| Concept         | Description                                                                                                                              |
| --------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| **Node**        | Any relevant entity in your life: a topic, person, place, item, project, goal or event.                                                  |
| **Relation**    | A typed, weighted link between Nodes (`related_to`, `part_of`, `written_by`, `located_at`, …) with an `origin` (user, system, ai, import) and a `confidence`. |
| **Hard Graph**  | Confirmed facts and relations: the actual structure of your life.                                                                        |
| **Soft Graph**  | Inferred relations with a confidence score, visually distinct, which you can accept, ignore, remove or promote to permanent.              |
| **Type**        | User-defined schema (Book, Person, Project, Place, …) describing the usual properties of a Node.                                         |
| **Property**    | Configurable fields (text, date, status, select, rating, node reference, location, …) that become filters, groupings and views.           |
| **Tag**         | Lightweight classifier for filtering and grouping. It complements Relations and never replaces them.                                      |
| **Collection**  | A manual or dynamic grouping, often a saved query (`Type = Book AND Status = WantToRead`), that also serves as navigation.                 |
| **Resource**    | External content (video, article, web page, podcast, PDF, …) treated as a first-class object with metadata and an embedding.             |
| **View**        | A representation of a query over the graph (Graph, List, Cards, Timeline, Table, Gallery, Map). Views never own data.                    |

## Navigating the graph

- **2.5D graph view:** spatial depth without the usability cost of free 3D.
  Size reflects relevance, proximity reflects similarity, and intensity
  reflects recent activity.
- **Semantic zoom:** zooming changes the *level of meaning*, not just the
  scale (`My Life → Knowledge → Philosophy → Stoicism → Meditations`). It
  should feel like *entering* a subject.
- **Local graph:** open any Node and see only its immediate context, with
  adjustable depth.
- **Global graph:** an exploratory, contemplative overview with automatic
  clustering. It is not the main working interface.
- **Time as a dimension:** Nodes carry dates, deadlines, periods and
  recurrence, so you can ask *"show everything related to the baby in the next
  three months."*

## Search

- **Text search:** `"Marcus Aurelius"`
- **Property search:** `Type = Book AND Status = Reading`
- **Semantic search:** `"things about dealing better with problems"` finds
  Stoicism, *Meditations* and related notes without a literal match.
- **Natural-language questions:** *"Which philosophy books do I already
  own?"*, *"Where is my copy of Meditations?"*

## Sharing

Share **part** of your graph, never the whole thing, through a
**Shared Graph View**: a root Node with depth and size limits, in *snapshot*
or *live* mode, with private, unlisted, public or password-protected access.
Access control is enforced on the backend, so a viewer can never use visible
relations or APIs to escape the authorized subgraph.

## Architecture (conceptual)

```text
USER GRAPH        Nodes · Relations · Properties · Collections
      ↓
SEMANTIC LAYER    Embeddings · Similarity · Clustering · Ranking
      ↓
AI LAYER          Classification · Extraction · Summaries · Suggestions · Natural language
```

**The user's graph is the product.** AI, calendar, notes and resources are all
ways to enrich and interact with it.

## Roadmap

**MVP:** validate one hypothesis: *do people find it useful to organize and
navigate personal information through context and relationships, without
manually structuring a knowledge base?*

- Nodes, Relations, Types, Tags, Collections
- Resources from URLs (web pages, YouTube, articles)
- Inbox
- Local 2.5D graph view
- Embedding-based semantic suggestions
- Text search and basic filters
- Basic AI capture (suggests type, tags and relations)

**Phase 2:** calendar integration, timeline, shared graph views, advanced
collections, mobile share target, AI summaries, natural-language graph search,
physical locations.

**Phase 3:** Constellations (shareable curated subgraphs), public sharing,
collaborative graphs, import/export, agents, browser extension.

**Explicitly out of the MVP:** 3D graph, social network, collaborative
editing, autonomous agents, native mobile apps, complex offline-first, plugin
system.

## What Life Graph is not

Not a Notion clone, an Obsidian clone, a task manager, a calendar app, a
bookmark manager or an AI chatbot. It may borrow from these, but its core is
**a contextual, evolving map of your life**.

## Inspiration

Learning from [TheBrain](https://thebrain.com), [Capacities](https://capacities.io),
[Heptabase](https://heptabase.com), [Anytype](https://anytype.io),
[Tana](https://tana.inc) and [Obsidian](https://obsidian.md) without copying
any of them.

## Development

Stack: .NET 10 (ASP.NET Core, EF Core, PostgreSQL + pgvector) and React 19
(Vite, TypeScript). Coding standards live in [`docs/standards/`](docs/standards/).

### Recommended: devcontainer

Requirements: Docker and VS Code with the Dev Containers extension.

1. Open the repository in VS Code and run **Reopen in Container**.
2. Wait for `post-create.sh`: it restores packages, runs `npm ci` and applies
   the migrations to the `postgres` service.
3. Run the API and the frontend in two terminals:

   ```bash
   dotnet run --project src/LifeGraph.Host   # http://localhost:5000
   cd web && npm run dev                     # http://localhost:5173
   ```

Mailpit (captured e-mails) is forwarded from `mailpit:8025`. Details in
[`.devcontainer/README.md`](.devcontainer/README.md).

### Tests and checks

```bash
dotnet test --solution LifeGraph.sln
dotnet format LifeGraph.sln --verify-no-changes
cd web && npm run lint && npm run typecheck && npm test
```

Integration tests need PostgreSQL. Inside the devcontainer they create a
throwaway database on the `postgres` service; anywhere else they start one with
Testcontainers, so Docker must be running.

### Without the devcontainer

The .NET 10 SDK, Node 24 and Docker are enough to build and run every test
(integration tests use Testcontainers). Running the API itself needs a
PostgreSQL bootstrapped with [`db/bootstrap/`](db/bootstrap/) (roles, database,
extensions), which the devcontainer does for you. The application connects as
`lifegraph_app` (`ConnectionStrings__Default`) and migrations run as
`lifegraph_migrator` (`ConnectionStrings__Migrations`).
