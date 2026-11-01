# E13 — Entitlements Free/Premium

- **Ordem:** 13
- **Depende de:** E5, E10, E11
- **Última revisão:** 2026-11-01

## Contexto

A v2 (§52–61) pede uma arquitetura preparada para Free/Premium desde o MVP, com
os limites da §55. O modelo comercial ainda não está claro para o usuário, que
pediu para discutir a **Q3** neste épico. Até lá, os limites de segurança
(E5) e os limites de share (E11) funcionam como constantes no código.

## Decisões

- **DA-086 — Limites nunca bloqueiam acesso a dados existentes** (invariante,
  §56 e §61). Ao atingir um limite, só a criação nova é impedida. Visualizar,
  editar, exportar, excluir, consultar proveniência, desfazer alterações de
  agentes e controlar permissões **nunca** são bloqueados nem viram Premium.
  Testes cobrem isso.
- **DA-087 — Limites de segurança ≠ limites de plano** (consenso). Rate limits,
  o teto de 50 operações, o bulk de 20 e a janela deslizante ficam no E5 e não
  dependem de plano.

- **DA-260 — Plano atribuído à mão, sem cobrança** (consenso; Q3 do épico).
  O MVP entrega só o mecanismo de limites. O plano mora numa tabela própria
  `account_plans` (sem linha = `free`) no módulo Accounts, exposta a Graph,
  Agents e Sharing por uma porta em `Accounts.Contracts` (`IEntitlements`).
  O papel da app só faz SELECT, porque um `REVOKE UPDATE(plan)` numa coluna de
  `accounts` é frágil (o GRANT na tabela inteira o anula). A atribuição é uma
  CLI `accounts plan` por um papel de operação; se for o provisioner, a
  emenda à DA-107 é registrada aqui. A UI não precifica nem vende Premium.
  Teste de integração: o papel da app tenta mudar o próprio plano e falha.
- **DA-261 — Quota de escrita MCP** (consenso). Unidade: operação aplicada por
  uma AgentIdentity; período: mês-calendário UTC; consumo somado das colunas
  do ChangeSet. Estourada, o ChangeSet inteiro vira proposta com
  `ProposalReason.Quota` (nunca aplicação parcial). Proposta aprovada pela
  pessoa e Undo não consomem nem devolvem quota. Propostas pendentes têm teto
  e expiração. A checagem roda no pipeline único (DA-013), na mesma transação.
- **DA-262 — Agent Connection** (consenso). Conta a AgentIdentity ativa
  (`RevokedAt == null`); reconectar o mesmo client nunca é nova conexão e nunca
  é bloqueado; reativar uma revogada conta. Checagem e criação são atômicas
  (lock por conta). A recusa fica na página de consentimento, sem botão Allow,
  com link para revogar.
- **DA-263 — Downgrade** (consenso). Nada é ocultado, revogado ou apagado;
  shares e conexões existentes continuam funcionando. Só a criação nova do
  tipo acima do limite é recusada (DA-086).
- **DA-264 — O que conta** (consenso). Só linhas vivas (tombstone não conta);
  Undo e restore sempre passam; Resource conta como Resource e como Node;
  Accept de Soft Relation conta como Relation; Types e Properties de sistema,
  a Raiz da Conta, a Relation de âncora e as Soft Relations nunca contam;
  Property Definition arquivada conta apenas se puder ser excluída (senão o
  limite viraria armadilha).
- **DA-265 — Limites de share Premium e o selo "compartilhado"** (consenso;
  emenda à DA-240). O selo passa a consultar só os ids visíveis na tela, em
  vez de paginar todos os ids de Nodes compartilhados; a exceção à API-060 das
  coleções do visitante fica justificada por "limitado pelo teto do plano" e
  não por "≤ 25". A lista de shares do dono pagina. Os valores do Premium
  (100 shares × 250 Nodes) não mudam.
- **DA-266 — Interface do frontend** (consenso; escolha do arquiteto,
  validada pelo analista). Híbrido em `web/src/features/account/`:
  `planData.ts` (`planKeys`, `planOptions`, `LimitKind` derivado do schema
  gerado, `isPlanLimitReached`, `limitStatus`, `usePlanLimit`,
  `useInvalidatePlan` no `onSettled` de cada criação), `LimitNotice` (dono do
  texto e da a11y) e `PlanUsageSection`. O limite no cliente é só UX; o
  servidor decide. Sem plano carregado, `createShareSchema(maxNodes)` não
  aplica teto (nunca volta a 25). "Sem limite" é `max: null`, distinto de
  carregando. A seção tem estados de carregando, erro com retry e uso zero; a
  quota de escrita do agente e a data de reinício são só exibição.
- **DA-267 — A CLI `accounts plan` roda com o `lifegraph_provisioner`**
  (emenda à DA-107; aplica a DA-260). O papel de operação da DA-217 não
  serve: ele não tem SELECT em tabela de Account nem em `users`, e achar a
  Account pelo e-mail exige ler o diretório de credenciais, que o provisioner
  já lê. O provisioner ganha SELECT, INSERT e UPDATE só em `account_plans`, e
  escreve sob a mesma policy de isolamento das outras tabelas de Account,
  num escopo que age pela Account que o e-mail aponta. Assim ele alcança o
  plano dessa Account e de nenhuma outra, e a DA-107 continua valendo para o
  resto: nenhum grant no grafo, nenhuma leitura de `accounts`. O papel da app
  só faz SELECT em `account_plans`, e a FK com `ON DELETE CASCADE` leva o
  plano junto com a Account sem dar escrita à app. Testes: o papel da app
  tenta INSERT e UPDATE no próprio plano e recebe 42501; a CLI troca o plano
  de uma Account sem tocar outra.

- **DA-268 — Relation de âncora que não conta = a que toca a Raiz da Conta**
  (consenso; esclarece a DA-264). Nada persiste o marcador de âncora, e uma
  coluna nova exigiria migration, mudança no pipeline e backfill sem
  preencher o passado. A contagem de Relations deixa de fora as Hard Relations
  vivas que tocam a Raiz da Conta (encanamento do sistema, não escolha da
  pessoa); uma âncora a um Node comum conta como Relation normal. Teste:
  Relation para a Raiz não conta; Hard Relation entre dois Nodes comuns conta.
  O GLOSSARY deixa de sugerir que toda Âncora é gratuita.
- **DA-269 — Padrões do Premium que o §55 não fixa** (consenso, provisório
  até revisão de negócio). Collections 1000, CustomTypes 200, CustomProperties
  1000, AgentConnections 10, AgentWriteOperationsPerMonth 10000, sobrescritos
  por configuração; valores não publicados. `max: null` (sem limite) segue
  suportado pelo mecanismo, mas não é padrão. Teste: Premium ≥ Free em toda
  dimensão. Rever antes de qualquer Premium público, junto com o teto de
  propostas pendentes (DA-261).
- **Q10 (consenso):** o teste de grants do provisioner é atualizado para a
  DA-267 (regra mudou, categoria B). A lista continua exata
  (`account_plans` INSERT/SELECT/UPDATE, sem DELETE, sem BYPASSRLS) e a
  mudança de plano registra `updated_at` e origem.

- **DA-298 — O export da conta inclui o plano** (**provisória** — decidida pelo analista; emenda a DA-247).
  O plano é um fato sobre a Conta, mostrado na página Account (DA-266); o
  export é o canal de transparência da titular (LGPD), então não o omite.
  O objeto `account` do cabeçalho ganha o campo `plan`, com `plan`, `origin` e
  `updatedAt` (`account.plan.{plan, origin, updatedAt}`; campo aditivo, sem
  subir a versão do formato), lidos por `Accounts.Contracts.IEntitlements`
  e nunca contornando a RLS. O export nunca é limitado nem moldado pelo plano:
  o conteúdo é idêntico entre Free e Premium, salvo o campo do plano. Nenhum
  dado interno do operador sai. O import ignora o campo. Outra visão (arquiteto
  ao início, analista depois da contra-argumentação, posições trocadas): deixar
  o plano fora e declarar em `OmittedData`. Para reverter: remover o campo `account.plan`
  e a leitura de `IEntitlements` no exportador, declarar `plan` em `OmittedData`
  com o motivo e ajustar o teste Free×Premium.

- **DA-299 — Interface do selo "compartilhado"** (consenso; detalha a DA-265
  e a DA-266). `useSharedNodeIds(visibleNodeIds)` devolve
  `{ ids, isPending, isError, refetch }`, com `ids` estável; divide em blocos
  de 75 ids (o servidor recusa mais de 75 com 422, nunca trunca), mantém o
  conjunto anterior enquanto carrega e não faz requisição sem Nodes visíveis.
  `SharedBadge({ state: "shared" | "not-shared" | "unknown" })` é
  apresentacional: pendente ou com erro sem dado anterior é `unknown`, nunca
  `not-shared` (um falso negativo diria à pessoa que o Node não está exposto
  por um link). O endpoint aceita só o principal Human e o hook fica fora do
  bundle do visitante. Os membros são fixos na criação (ADR 0012), então só
  criar, revogar, editar escopo e Delete/Purge de Node compartilhado
  invalidam o prefixo `['shares','shared-node-ids']`; a revogação em outra aba
  converge por `staleTime` ~30 s e refetch ao focar a janela.
- **Renumeração:** as decisões desta execução ficaram DA-260 a DA-269 e
  DA-298/DA-299, porque o E19 (criado em paralelo) já usava a DA-270.

## Checklist

- [x] Modelo de plano + limites lidos de configuração + contadores de uso
- [x] Aplicação só nos comandos de criação e na quota de escrita de agentes
- [x] Converter as constantes de share (E11) em limites por plano
- [x] Mensagens de limite atingido sem bloquear o acesso
- [x] A Raiz da Conta fica fora dos limites do Free (DA-204)

## Fora de escopo

Gateway de pagamento, a menos que a Q3 decida o contrário.
