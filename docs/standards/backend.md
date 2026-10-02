# Padrões de backend (`BE`)

Complementam [general.md](general.md) para serviços e aplicações de servidor
(APIs, workers, funções serverless, jobs). Regras de contrato HTTP ficam em
[api-rest.md](api-rest.md) e de persistência em [database.md](database.md).

## 1. Arquitetura e camadas

**BE-001 — DEVE: separar domínio, aplicação e infraestrutura.**

- **Domínio:** entidades, value objects e regras de negócio. Não depende de
  nada externo (framework, banco, HTTP).
- **Aplicação:** casos de uso que orquestram o domínio e definem as portas
  (interfaces) de que precisam.
- **Infraestrutura / entrada:** controllers, handlers, repositórios, clientes
  HTTP, filas — implementam as portas e traduzem o mundo externo.

Dependências apontam sempre para dentro (infra → aplicação → domínio). Em
projetos que adotam vertical slices, a separação vale dentro de cada slice,
com as flexibilizações de BE-006.
_Por quê:_ regras de negócio sobrevivem a trocas de framework e são testáveis
sem infraestrutura.

**BE-002 — DEVE: controllers/handlers finos.** A camada de entrada só valida o
formato da entrada, chama um caso de uso e traduz o resultado para a resposta.
Nenhuma regra de negócio ali.
_Por quê:_ regra no controller é duplicada em toda nova porta de entrada (fila,
CLI, job).

**BE-003 — DEVERIA: um caso de uso por operação de negócio** (`CreateOrder`,
`CancelSubscription`), com entrada e saída explícitas.
_Por quê:_ torna o sistema navegável pelas capacidades de negócio.

**BE-004 — DEVERIA: organizar por funcionalidade, não por tipo técnico.**
Prefira `orders/` contendo controller, caso de uso e repositório a pastas
globais `controllers/`, `services/`, `repositories/`.
_Por quê:_ mudanças de uma funcionalidade ficam próximas; o acoplamento fica
visível.

**BE-006 — PODE: vertical slice architecture.** Cada operação (`CreateOrder`,
`GetOrderById`) é um slice autocontido: entrada, handler, validação, acesso a
dados e resposta juntos, otimizados para aquela operação. Regras de convivência
com BE-001:

- Operações **com regra de negócio** continuam usando o domínio (entidades e
  invariantes de BE-010). O slice orquestra, o domínio decide.
- Operações **sem regra** (leituras, consultas de tela, CRUD trivial) podem
  acessar a persistência direto do handler, com consultas/projeções próprias,
  sem atravessar repositórios genéricos.
- Slices **não chamam outros slices** diretamente. O que for compartilhado é
  extraído para o domínio ou para um módulo comum só quando surgir duplicação
  real (GEN-021).

A escolha entre camadas clássicas e slices é registrada como `DA-###` no início
do projeto.
_Por quê:_ alinha a estrutura do código com a entrega em fatias verticais
(GEN-100). Cada mudança fica concentrada em um lugar, e cada operação usa só a
complexidade de que precisa, em vez de camadas cerimoniais para um `SELECT`.

**BE-005 — DEVE: não expor entidades de domínio ou de persistência na borda.**
Requests/responses usam DTOs próprios, mapeados explicitamente.
_Por quê:_ evita vazar campos internos (senha, flags) e acoplar o contrato
público ao modelo interno.

## 2. Domínio

**BE-010 — DEVERIA: modelo de domínio rico.** Invariantes ficam dentro da
entidade (`order.cancel()` valida se pode cancelar), não espalhadas em services
que manipulam setters.
_Por quê:_ a entidade nunca fica em estado inválido, independente de quem a usa.

**BE-011 — DEVERIA: value objects para conceitos com regra.** Email, CPF,
dinheiro, intervalo de datas etc. são tipos que se validam na criação, não
strings/números soltos.
_Por quê:_ elimina validação repetida e confusão entre parâmetros do mesmo tipo
primitivo.

**BE-012 — DEVE: dinheiro nunca em ponto flutuante.** Use tipo decimal ou
inteiro em menor unidade (centavos), sempre com a moeda.
_Por quê:_ `float` acumula erro de arredondamento.

**BE-013 — DEVE: datas e horas em UTC internamente,** com tipos que carregam
fuso quando relevante; conversão para fuso local só na apresentação. O relógio
é uma dependência injetável.
_Por quê:_ evita bugs de horário de verão e de servidores em fusos diferentes, e
permite testar lógica temporal.

## 3. Validação e autorização

**BE-020 — DEVE: validar em duas camadas.** Formato/sintaxe na borda (tipos,
obrigatórios, tamanhos); regras de negócio no domínio/caso de uso.
_Por quê:_ cada camada protege o que conhece; nenhuma confia na outra.

**BE-021 — DEVE: autorização no servidor, em toda operação.** Verifique não só
"está autenticado" mas "pode acessar *este* recurso" (dono, tenant, papel).
Nunca confie em IDs, papéis ou preços enviados pelo cliente.
_Por quê:_ falha de controle de acesso (IDOR) é a vulnerabilidade web mais
comum.

**BE-022 — DEVE: isolamento multi-tenant explícito** (quando aplicável). Todo
acesso a dados é filtrado pelo tenant do contexto autenticado, de preferência
por mecanismo central (filtro global, política), não por lembrança em cada
consulta.
_Por quê:_ esquecer um filtro uma vez vaza dados de outro cliente.

## 4. Integrações e resiliência

**BE-030 — DEVE: timeout em toda chamada externa** (HTTP, banco, fila, cache),
com valor explícito e coerente com o SLA do chamador.
_Por quê:_ chamada sem timeout pode prender recursos indefinidamente e derrubar
o serviço.

**BE-031 — DEVERIA: retry só para falhas transitórias, com backoff exponencial
e jitter,** e só em operações idempotentes. Limite de tentativas definido.
_Por quê:_ retry ingênuo amplifica incidentes (tempestade de retries).

**BE-032 — DEVERIA: circuit breaker / fallback** para dependências cuja falha
não deve derrubar o serviço inteiro.
_Por quê:_ isola falhas e permite degradação controlada.

**BE-033 — DEVE: clientes externos atrás de uma porta.** Integrações ficam em
adaptadores que traduzem modelos e erros externos para os do domínio.
_Por quê:_ a mudança de fornecedor ou de API afeta um lugar só.

**BE-034 — DEVE: consumidores de mensagens idempotentes.** Assuma entrega
"pelo menos uma vez": processar a mesma mensagem duas vezes não pode duplicar o
efeito (use chave de deduplicação/idempotência).
_Por quê:_ filas e webhooks reentregam mensagens.

**BE-035 — DEVERIA: outbox para consistência entre banco e mensageria.** Ao
gravar no banco e publicar evento, use outbox transacional (ou equivalente) em
vez de "grava e publica" em sequência.
_Por quê:_ evita eventos perdidos ou fantasmas quando um dos dois falha.

**BE-036 — DEVE: mensagens com falha vão para DLQ** (dead-letter) com contexto
do erro, e existe monitoramento sobre ela.
_Por quê:_ mensagem envenenada não pode travar a fila nem sumir.

## 5. Concorrência e performance

**BE-040 — DEVE: I/O assíncrono/não bloqueante** quando a plataforma oferece,
sem bloquear threads de requisição com chamadas síncronas.
_Por quê:_ bloqueio esgota o pool de threads sob carga.

**BE-041 — DEVE: controle de concorrência em atualizações.** Use concorrência
otimista (versão/ETag) ou locks explícitos onde atualizações simultâneas forem
possíveis.
_Por quê:_ sem isso, a última escrita sobrescreve a outra silenciosamente.

**BE-042 — DEVERIA: trabalho longo fora da requisição.** Operações demoradas
(relatórios, importações, e-mails em massa) vão para fila/job em background, e
a API responde com o status do processamento.
_Por quê:_ requisições longas estouram timeouts e prendem recursos.

**BE-043 — DEVERIA: cache com estratégia explícita** — chave, TTL e
invalidação definidos e documentados. Nunca cachear dados de um usuário em
chave compartilhada.
_Por quê:_ cache sem invalidação clara serve dado errado; cache mal chaveado
vaza dados.

**BE-044 — DEVERIA: medir antes de otimizar.** Otimizações vêm acompanhadas de
medição (profiling, benchmark, métrica de produção).
_Por quê:_ intuição sobre gargalo costuma estar errada.

## 6. Operação

**BE-050 — DEVE: health checks** separando *liveness* (processo vivo) de
*readiness* (pronto para receber tráfego, dependências críticas ok).
_Por quê:_ orquestradores precisam saber quando reiniciar e quando só tirar do
balanceador.

**BE-051 — DEVE: desligamento gracioso.** Ao receber sinal de parada, o serviço
deixa de aceitar trabalho novo, termina o que está em andamento (com limite) e
libera recursos.
_Por quê:_ deploys não podem perder requisições ou mensagens.

**BE-052 — DEVE: métricas essenciais.** No mínimo taxa, erros e latência
(percentis) por operação, além de métricas de negócio relevantes.
_Por quê:_ não se opera o que não se mede.

**BE-053 — DEVERIA: serviços sem estado local** (stateless). Sessão, arquivos e
cache compartilhado ficam em armazenamento externo.
_Por quê:_ permite escalar horizontalmente e substituir instâncias livremente.

**BE-054 — DEVERIA: feature flags para lançamentos arriscados,** seguindo as
regras de flags de [general.md](general.md) (GEN-110 a GEN-116).
_Por quê:_ separa deploy de release e permite desligar sem rollback.
