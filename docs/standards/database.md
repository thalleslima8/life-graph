# Padrões de banco de dados (`DB`)

Complementam [general.md](general.md) e [backend.md](backend.md). Focados em
bancos relacionais; para bancos não relacionais, aplicam-se os princípios
(migrations, índices, menor privilégio, dados sensíveis) adaptados ao modelo.

## 1. Modelagem e nomes

**DB-001 — DEVE: convenção de nomes única no schema.** Por padrão:
`snake_case`, tabelas no plural (`orders`, `order_items`), colunas no singular,
sem prefixos de tipo (`tbl_`, `str_`). Chave primária `id`; chave estrangeira
`<entidade_singular>_id` (`order_id`).
_Por quê:_ consultas legíveis e previsíveis sem consultar o schema.

**DB-002 — DEVERIA: nomes explícitos para constraints e índices**
(`pk_orders`, `fk_order_items_order_id`, `ix_orders_customer_id_created_at`,
`uq_users_email`, `ck_orders_total_positive`).
_Por quê:_ mensagens de erro e migrations ficam compreensíveis e estáveis entre
ambientes.

**DB-003 — DEVE: toda tabela tem chave primária.** Prefira chaves substitutas
(UUID v7/ULID ou inteiro gerado) em vez de chaves naturais mutáveis (email,
CPF). Se usar inteiro sequencial internamente, não o exponha na API (API-014).
_Por quê:_ chaves naturais mudam; IDs ordenáveis temporalmente mantêm índices
eficientes.

**DB-004 — DEVE: integridade garantida pelo banco.** Use `NOT NULL`, chaves
estrangeiras, `UNIQUE` e `CHECK` para as invariantes que o banco pode
garantir — não só na aplicação.
_Por quê:_ o banco é a última linha de defesa contra dados inconsistentes,
inclusive de scripts e outros sistemas.

**DB-005 — DEVE: tipos de dados adequados.** Decimal para dinheiro (nunca
float — BE-012), tipo de data/hora com fuso (ou UTC documentado), booleano
nativo, tamanhos definidos para strings com limite de negócio.
_Por quê:_ tipo errado gera perda de precisão e conversões em toda consulta.

**DB-006 — DEVERIA: normalizar até a 3FN;** desnormalizar só com motivo de
performance medido e registrado.
_Por quê:_ duplicação de dados exige sincronização e diverge.

**DB-007 — DEVERIA: colunas de auditoria** `created_at` e `updated_at` (UTC) em
tabelas de negócio; `created_by`/`updated_by` quando houver requisito.
_Por quê:_ diagnóstico e suporte dependem de saber quando algo mudou.

**DB-008 — DEVERIA: evitar colunas genéricas** (EAV, `data JSON` como
substituto de modelagem). JSON é aceitável para dados realmente semiestruturados
e não consultados por campo.
_Por quê:_ perde-se integridade, tipagem e indexação.

## 2. Migrations

**DB-010 — DEVE: toda mudança de schema é uma migration versionada** no
repositório, aplicada automaticamente pelo pipeline. Nenhuma alteração manual
em ambiente compartilhado.
_Por quê:_ todos os ambientes ficam reproduzíveis e auditáveis.

**DB-011 — DEVE: migrations imutáveis depois de aplicadas** em qualquer
ambiente compartilhado. Correção é uma nova migration.
_Por quê:_ editar migration aplicada deixa ambientes divergentes.

**DB-012 — DEVE: migrations compatíveis com a versão anterior da aplicação**
(expand/contract). Remover ou renomear coluna é feito em etapas: adicionar
nova → migrar dados e código → remover antiga em deploy posterior.
_Por quê:_ durante o deploy, versão nova e antiga rodam ao mesmo tempo.

**DB-013 — DEVERIA: migrations pequenas e seguras para produção.** Avalie
locks em tabelas grandes (criação de índice concorrente, backfill em lotes,
`NOT NULL` em etapas).
_Por quê:_ uma migration que trava uma tabela grande derruba o sistema.

**DB-014 — DEVERIA: separar migração de schema de migração de dados volumosa.**
Backfills grandes rodam como job idempotente e retomável.
_Por quê:_ evita deploys longos e transações gigantes.

## 3. Consultas

**DB-020 — DEVE: consultas sempre parametrizadas.** Nunca concatenar entrada
em SQL. Nomes dinâmicos (coluna de ordenação) vêm de lista branca.
_Por quê:_ SQL injection continua entre as vulnerabilidades mais exploradas.

**DB-021 — DEVE: sem `SELECT *` em código de aplicação;** selecione as colunas
necessárias.
_Por quê:_ evita tráfego desnecessário e quebra silenciosa quando colunas mudam.

**DB-022 — DEVE: evitar N+1.** Carregue relações em lote (join, `IN`, eager
loading explícito) em vez de uma consulta por item em laço.
_Por quê:_ N+1 é a causa mais comum de lentidão em sistemas com ORM.

**DB-023 — DEVE: consultas sobre coleções têm limite** (paginação — API-060).
_Por quê:_ consultas sem limite crescem com os dados até derrubar o sistema.

**DB-024 — DEVERIA: revisar o plano de execução** de consultas novas em
tabelas grandes ou críticas, e monitorar consultas lentas em produção.
_Por quê:_ consulta que é rápida com 100 linhas pode ser full scan com 10
milhões.

**DB-025 — DEVERIA: ORM para o comum, SQL para o complexo.** Use o ORM para
CRUD e SQL explícito (parametrizado) quando a consulta gerada for ineficiente
ou ilegível. Desligue rastreamento de mudanças em leituras somente-leitura.
_Por quê:_ o ORM é ferramenta, não dogma.

## 4. Índices

**DB-030 — DEVE: índices para chaves estrangeiras e para filtros/ordenações
frequentes.**
_Por quê:_ sem índice, joins e filtros viram varreduras completas.

**DB-031 — DEVERIA: índices compostos alinhados à consulta** (colunas de
igualdade primeiro, depois intervalo/ordenação) e sem índices redundantes.
_Por quê:_ a ordem das colunas define se o índice é usado; índices sobrando
pesam nas escritas.

**DB-032 — DEVERIA: unicidade de negócio como índice único** (inclusive
parcial/filtrado quando houver soft delete).
_Por quê:_ verificar unicidade só na aplicação falha sob concorrência.

## 5. Transações e concorrência

**DB-040 — DEVE: transações curtas e com fronteira clara** no caso de uso.
Nenhuma chamada externa (HTTP, fila, e-mail) dentro de transação aberta.
_Por quê:_ transações longas seguram locks e conexões; chamadas externas não
são revertidas por rollback.

**DB-041 — DEVE: operações que precisam ser atômicas ficam na mesma
transação;** quando envolvem banco + mensageria, use outbox (BE-035).
_Por quê:_ evita estados parciais após falhas.

**DB-042 — DEVERIA: concorrência otimista** (coluna de versão/rowversion) em
entidades editadas concorrentemente (BE-041); conhecer o nível de isolamento
padrão do banco e só alterá-lo com motivo.
_Por quê:_ previne perda de atualização sem locks pesados.

**DB-043 — DEVE: pool de conexões configurado** e conexões sempre devolvidas
(uso de construções que garantem liberação).
_Por quê:_ vazamento de conexão esgota o pool e derruba a aplicação.

## 6. Exclusão e retenção

**DB-050 — DEVERIA: definir a estratégia de exclusão por entidade:** exclusão
física, soft delete (`deleted_at`) ou arquivamento. Com soft delete, filtros
globais garantem que registros excluídos não apareçam por engano.
_Por quê:_ soft delete sem filtro central gera vazamento de dados "apagados".

**DB-051 — DEVE: política de retenção para dados pessoais,** com exclusão ou
anonimização real quando exigido (LGPD/GDPR) — soft delete não basta para
direito ao esquecimento.
_Por quê:_ obrigação legal.

## 7. Segurança e operação

**DB-060 — DEVE: menor privilégio no banco.** A aplicação usa usuário sem
permissão de DDL em produção; migrations usam credencial separada. Nunca o
usuário administrador.
_Por quê:_ limita o dano de uma SQL injection ou credencial vazada.

**DB-061 — DEVE: dados sensíveis protegidos** — criptografia em trânsito e em
repouso; criptografia em nível de coluna ou tokenização para dados altamente
sensíveis; senhas apenas com hash (GEN-062).
_Por quê:_ backup ou dump vazado não pode expor dados em claro.

**DB-062 — DEVE: dados de produção não vão para outros ambientes** sem
anonimização.
_Por quê:_ ambientes de dev/teste têm controles mais fracos.

**DB-063 — DEVE: backups automáticos com restauração testada periodicamente,**
com RPO/RTO definidos.
_Por quê:_ backup nunca restaurado é só esperança.

**DB-064 — DEVERIA: testes de integração contra o mesmo motor de banco de
produção** (ex.: container), não contra banco em memória de outro motor.
_Por quê:_ diferenças de dialeto, tipos e transações escondem bugs.

**DB-065 — DEVERIA: seeds de dados de referência via migration ou script
versionado e idempotente;** dados de teste nunca em migrations de produção.
_Por quê:_ ambientes reproduzíveis sem poluir produção.
