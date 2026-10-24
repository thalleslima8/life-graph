# E10 — SharedGraphView (link compartilhado)

- **Ordem:** 10
- **Depende de:** E2, E6, E8
- **Última revisão:** 2026-10-02

## Contexto

O usuário pode compartilhar parte do grafo por link (v2 §47–49). Por decisão do
usuário, isso entra **no MVP e no plano Free**. Quem recebe o link nunca pode
atravessar relações para Nodes não autorizados (§48): a filtragem é feita no
backend. O épico vem depois do 2.5D, para o visitante reaproveitar a
visualização, e depois do E8, porque a lista de propriedades expostas depende
das definições e das Tags.

## Decisões

- **DA-074 — SharedGraphView no MVP e no Free** (usuário). Os especialistas
  recomendaram deixá-la fora; o usuário decidiu incluir.
- **DA-075 — Membros fixados na criação, conteúdo exibido ao vivo** (consenso,
  ADR: [docs/adr/0012-share-com-membros-fixos-e-conteudo-ao-vivo.md](../../adr/0012-share-com-membros-fixos-e-conteudo-ao-vivo.md)).
  - Na criação, o usuário escolhe RootNode + MaxDepth, revisa a lista proposta
    (máximo de 25) e desmarca o que quiser. A lista confirmada é a allowlist.
  - Node novo nunca entra sozinho: um share totalmente "Live" vazaria Nodes
    sensíveis adicionados depois.
  - Um snapshot copiado sobreviveria ao Purge, contra LGPD e Lei 25.
  - Node apagado some do link; se o Node raiz for apagado, o link fica suspenso
    (404); se a raiz for restaurada, o link volta; se a raiz for purgada, o
    link é revogado.
- **DA-076 — Nodes ocultos para agentes nunca entram em um share, sem exceção
  no MVP** (consenso após desempate). Um link público pode ser lido por
  qualquer IA que navegue, o que tornaria falso o rótulo "oculto". Uma regra
  sem exceção é mais simples de provar. Quem quiser compartilhar desoculta o
  Node antes. Relações que tocam Nodes ocultos e qualquer contagem que revele o
  descarte também ficam de fora.
- **DA-077 — O que o visitante vê é uma allowlist fechada** (consenso após
  desempate).
  - Sempre: título, corpo, nome do Type, URL de Resource (com aviso sobre
    links assinados) e Hard Relations entre membros.
  - Properties: nenhuma por padrão; o usuário escolhe `property_id` por
    `property_id`. Um toggle único exporia automaticamente, nos links já
    publicados, uma propriedade sensível criada depois, como "medicação".
  - Tags: booleano, desligado por padrão. **Risco residual aceito:** com o
    booleano ligado, novas Tags passam a aparecer, e a UI avisa isso.
  - Nunca: Outras propriedades, Soft Relations, proveniência, nomes de
    agentes, ChangeSets, Inbox, e-mail do dono ou contagens de vizinhos
    excluídos.
  - A projeção usa um DTO de visitante próprio, que não reaproveita os DTOs
    internos nem os do MCP (BE-005).
- **DA-078 — Links sem validade no MVP** (usuário). A validade fica
  registrada para uma versão futura (`expires_at` anulável, migration
  trivial). Mitigações obrigatórias (consenso):
  - lista de links com `created_at`, último acesso (timestamp grosseiro, sem
    IP nem user agent ligados ao link) e contagem;
  - revogar um ou todos, com efeito imediato e sem cache longo;
  - um selo "compartilhado" nos Nodes que fazem parte de um link ativo;
  - um kill-switch do operador, global e por link.
- **DA-079 — Acesso por URL de capacidade não listada** (consenso):
  - token aleatório de ≥128 bits, guardado com hash, no caminho (nunca na
    query) e redigido nos logs;
  - mesmo 404 para link inválido e revogado;
  - `noindex` (header e meta), `Referrer-Policy: no-referrer` e
    `Cache-Control: private, no-store`;
  - rate limit por IP e por token;
  - **origem separada**, sem cookies de sessão e com CSP estrita. Em dev, é uma
    segunda porta localhost.
- **DA-080 — Criar share é Sensitive, e agentes não criam shares no MVP**
  (consenso). Não há nenhuma ferramenta MCP para isso; o scope
  `lifegraph.share` fica reservado.
- **DA-081 — Limites de 3 links com 25 Nodes cada, como constantes** (consenso)
  até o E12.

## Checklist

- [ ] Modelo de share: raiz, profundidade, allowlist de Nodes, allowlist de propriedades, booleano de Tags, token com hash
- [ ] Fluxo de criação: Node raiz → lista proposta → revisão → aviso legal ("visível para quem tiver o link; você tem o direito de compartilhar dados de outras pessoas?")
- [ ] Endpoint de visitante com principal `ShareVisitor` no filtro central e DTO próprio
- [ ] Visualização read-only (lista + Inspector + 2.5D read-only) na origem separada
- [ ] Lista de links, revogar um ou todos, selo "compartilhado", kill-switch do operador
- [ ] Link "denunciar esta página" em todo share

### Testes obrigatórios
- [ ] Tentar atravessar para IDs fora da allowlist por todos os endpoints e parâmetros
- [ ] Node oculto nunca aparece, nem como contagem
- [ ] Snapshot do schema do DTO de visitante; propriedade fora da allowlist nunca aparece
- [ ] Revogação imediata; inválido e revogado respondem igual

## Critérios de saída

O usuário compartilha "Como estou aprendendo Estoicismo" com 12 Nodes. O
visitante vê só o que foi escolhido, e revogar derruba o link na hora.

## Fora de escopo

Validade do link, destinatários autenticados, Constellations, compartilhamento
por agentes.

⚖️ **Validação jurídica humana antes do beta:** publicação de dados de
terceiros, crianças e saúde (LGPD arts. 11 e 14, PIPEDA, Lei 25), e a redação
dos termos e da AUP.
