# Life Graph

Personal Context Graph: o grafo de contexto pessoal do usuário, mantido por ele
(via interface visual) e por agentes de IA autorizados (via API e MCP).

## Conta e agentes

**Account**:
O titular de um Life Graph e a fronteira de isolamento dos dados. Nenhum dado
cruza de uma Account para outra.
_Avoid_: tenant, workspace, usuário (quando se refere ao dono dos dados)

**AgentIdentity**:
Uma conexão de agente externo: a concessão OAuth que o usuário autorizou, com
seus scopes. O usuário pode renomeá-la e revogá-la. O nome e o provedor
declarados pelo cliente servem só para exibição e não são verificados.
_Avoid_: agente (para a conexão), bot, integração

**Oculto para agentes**:
Estado de um Node, herdado do seu Type ou definido no próprio Node, que o torna
inexistente para qualquer AgentIdentity: não aparece em busca, contexto,
travessia, relações nem sugestões. Também nunca aparece em um SharedGraphView.
_Avoid_: privado, restrito, invisível

## Grafo

**Hard Relation**:
Relation afirmada explicitamente por um ator (usuário, agente ou importação).
_Avoid_: relação confirmada, relação manual

**Soft Relation**:
Relation inferida pelo sistema (similaridade de embeddings, coocorrência). Só o
sistema cria Soft Relations, e o usuário decide sobre cada uma com Accept ou
Ignore.
_Avoid_: sugestão (quando se refere à relação já persistida), relação de IA

**Accept** / **Ignore**:
As duas ações do usuário sobre uma Soft Relation. Accept a transforma em Hard
Relation com origin `user`. Ignore a descarta e impede que o mesmo par volte a
ser sugerido.
_Avoid_: confirmar, promover, remover (para Soft Relations)

**Resource**:
Node do Type de sistema `Resource`, que representa conteúdo externo
identificado por uma URL (vídeo, artigo, página...). Seu tipo de conteúdo é o
`resource_kind`, não um Type próprio. Um livro físico é um Type do usuário, não
um Resource.
_Avoid_: link, bookmark, favorito

**resource_kind** / **provider**:
resource_kind é a natureza do conteúdo de um Resource (web_page, article, video,
pdf, image, document, other). provider é a plataforma de origem (ex.: youtube).
Um não substitui o outro.
_Avoid_: tipo do link, "youtube" como kind

**Property Definition**:
Uma Property da Account (nome e tipo de valor), que pode ser anexada a vários
Types. O valor de um Node pertence à definição, não ao Type.
_Avoid_: campo, atributo, coluna

**Outras propriedades**:
Valores de um Node cuja Property Definition não está anexada ao Type atual dele.
Ficam guardados e visíveis, e voltam a ser propriedades do Type quando a
definição volta a se aplicar.
_Avoid_: propriedades órfãs, campos perdidos

**Inbox**:
Estado explícito de um Node capturado e ainda não arquivado pelo usuário. O
Node só sai da Inbox por uma ação explícita, nunca como efeito colateral de
outra edição.
_Avoid_: não classificados, pendentes

**Origin**:
Quem originou um objeto do grafo: `user`, `agent`, `system` ou `import`. É
independente de a Relation ser Hard ou Soft.
_Avoid_: fonte, source (reservados para Provenance)

## Histórico e confiança

**Provenance**:
O registro de quem criou ou alterou um objeto, por qual via (UI, API, MCP,
import) e a partir de qual fonte.
_Avoid_: autoria, log, auditoria

**GraphChangeSet**:
Um grupo de alterações no grafo feitas por um ator em uma interação. É a
unidade que se inspeciona, aprova e desfaz. Estados: Proposed, Applied,
Rejected, Reverted, Expired.
_Avoid_: lote, transação, commit

**Undo**:
A reversão de um GraphChangeSet aplicado. Ela é registrada como um novo
GraphChangeSet e é recusada quando uma alteração posterior tocou as mesmas
entidades.
_Avoid_: rollback, desfazer silencioso

**Delete**:
Remoção de um Node ou Relation que ainda pode ser desfeita: o estado anterior
fica guardado durante a janela de retenção.
_Avoid_: apagar definitivamente

**Purge**:
Eliminação irreversível de dados e de todo o seu histórico, embeddings e dados
derivados (exclusão de conta, "apagar para sempre" ou fim da janela de
retenção).
_Avoid_: hard delete, delete (quando irreversível)

## Compartilhamento

**SharedGraphView**:
Um recorte do grafo publicado por link não listado. Os Nodes que fazem parte
dele são fixados na criação, e o conteúdo exibido é sempre o atual. O visitante
só vê o que o usuário escolheu expor.
_Avoid_: compartilhamento público, snapshot, Constellation
