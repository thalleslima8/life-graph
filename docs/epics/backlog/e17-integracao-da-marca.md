# E17 — Integração da marca

- **Ordem:** 17 (posição no roadmap **a definir**, junto do E16)
- **Depende de:** E16
- **Última revisão:** 2026-10-28

## Contexto

O [E16](e16-fundacao-visual-e-acabamento.md) deixa o frontend consumindo um
contrato de tokens com valores placeholder, um gate de contraste e um brief de
handoff. Este épico integra o que o usuário gerar no **Claude Design**: logo,
favicon, paleta, fonte de UI, fontes de marca e ícones.

Só entra em execução depois da entrega do usuário. Até lá fica em `backlog/`, e
nenhum épico depende dele.

## Condição de entrada

O pacote do usuário foi entregue no formato definido no brief do E16
(`docs/design/brief-claude-design.md`) e a checagem de licenças abaixo foi feita.

## Decisões

- **DA-237 — O E17 só troca valores e assets, sem mudar nomes de token nem
  componentes** (consenso).
  - Substitui os placeholders pelos valores da paleta, pelas fontes e pelos
    ícones, re-roda o gate de contraste e corrige o que ele reprovar.
  - Por quê: o contrato do E16 existe para que a troca seja barata. Com a
    paleta real o gate AA pode reprovar pares; o épico reserva espaço para esse
    ajuste, que vai de volta ao Claude Design ou vira correção de token.
  - O Lima `#B8D831` é cor de preenchimento e destaque, nunca de texto sobre
    claro. Texto sobre Lima usa a cor de tinta.
- **DA-238 — Licenças e termos de uso comercial pendentes de validação humana**
  (usuário: "creio que seja ok, não olhei").
  - As fontes (Big Shoulders, Newsreader e JetBrains Mono) são OFL, e os assets
    gerados pelo Claude Design precisam de confirmação de uso comercial em SaaS.
  - Por quê: o produto é SaaS comercial, e licença errada é risco jurídico que
    nem o código nem os especialistas validam. Fica como pré-condição com a
    caixa desmarcada, e não como aprovada.

## Checklist

- [ ] ⚖️ Licenças das fontes e dos assets gerados confirmadas para uso comercial em SaaS (pré-condição, feita pelo usuário)
- [ ] Valores de tokens claro e escuro aplicados, nos nomes do contrato
- [ ] Gate de contraste AA verde nos dois temas; pares reprovados corrigidos
- [ ] Logo (cheio, monocromático e mínimo) no shell, na tela de login e nas páginas públicas
- [ ] Favicon e metadados do `index.html`
- [ ] Fontes woff2 em self-host com subset e licença versionada junto
- [ ] Set de ícones próprio no registro do `<Icon>`; lucide removido se todos os nomes estiverem cobertos
- [ ] Fonte de UI aplicada ao corpo, ao Inspector e às tabelas
- [ ] Revisão visual manual pelo checklist do brief
- [ ] Microcopy de estados vazios e erros no tom da marca, sem renomear termos do `GLOSSARY.md` (DA-236)

### Testes obrigatórios
- [ ] Gate de contraste AA com os valores reais, claro e escuro
- [ ] axe nas telas principais com a marca aplicada
- [ ] E2E de fumaça da troca de tema com a marca aplicada

## Critérios de saída

O app abre com a identidade do Life Graph em claro e escuro, com contraste AA
verificado, fontes em self-host e nenhum placeholder visível.

## Fora de escopo

Criar ou alterar os assets (feito pelo usuário no Claude Design). Registro do
Life Graph no `limaj-solutions`. Cor de Type escolhida pelo usuário. Regressão
visual com Playwright.
