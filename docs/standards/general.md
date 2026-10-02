# Padrões gerais (`GEN`)

Valem para qualquer código do projeto — backend, frontend, scripts, infra como
código e testes. Os demais arquivos complementam estas regras; não as repetem.

## 1. Legibilidade e nomes

**GEN-001 — DEVE: nomes revelam intenção.** Variáveis, funções e tipos dizem o
que representam no domínio (`invoiceDueDate`, não `d2`/`data`/`tmp`).
Abreviações só quando são de uso universal (`id`, `url`, `http`).
_Por quê:_ código é lido muito mais vezes do que escrito; um bom nome dispensa
comentário.

**GEN-002 — DEVE: seguir a convenção de nomes da linguagem.** `camelCase`,
`PascalCase`, `snake_case` etc. conforme o idioma da stack, aplicado de forma
consistente e verificado por linter.
_Por quê:_ consistência reduz atrito cognitivo e evita debates em review.

**GEN-003 — DEVE: um único idioma para identificadores.** O código (nomes,
mensagens de log, commits) usa inglês; termos de negócio sem tradução fiel podem
manter o original se registrados no glossário do projeto.
_Por quê:_ misturar idiomas gera nomes duplicados para o mesmo conceito.

**GEN-004 — DEVERIA: booleanos e funções com forma gramatical clara.**
Booleanos como pergunta (`isActive`, `hasAccess`, `canRetry`); funções como verbo
(`calculateTotal`, `sendInvite`).
_Por quê:_ a leitura de `if (isActive)` é imediata; `if (active)` é ambígua.

**GEN-005 — DEVE: sem números ou strings mágicas.** Valores com significado
viram constantes nomeadas ou enums (`MAX_LOGIN_ATTEMPTS = 5`).
_Por quê:_ dá nome à intenção e centraliza a mudança.

## 2. Funções e módulos

**GEN-010 — DEVERIA: funções pequenas, com uma responsabilidade.** Se o nome
precisa de "e" (`validateAndSave`), provavelmente são duas funções.
_Por quê:_ funções focadas são mais fáceis de testar, reutilizar e nomear.

**GEN-011 — DEVERIA: poucos parâmetros.** Acima de ~3 parâmetros, agrupar em um
objeto/tipo nomeado. Evitar parâmetros booleanos que mudam o comportamento
(`render(true)`) — prefira duas funções ou um enum.
_Por quê:_ chamadas longas e flags posicionais são ilegíveis no ponto de uso.

**GEN-012 — DEVERIA: retorno antecipado em vez de aninhamento.** Use guard
clauses para casos inválidos e mantenha o caminho principal sem indentação
profunda (alvo: no máximo 3 níveis).
_Por quê:_ aninhamento profundo esconde o fluxo principal.

**GEN-013 — DEVERIA: funções sem efeitos colaterais ocultos.** Uma função que
"obtém" não deve gravar; uma que "valida" não deve alterar o objeto recebido.
Efeitos (I/O, mutação, tempo, aleatoriedade) ficam explícitos e nas bordas.
_Por quê:_ efeitos ocultos causam bugs que só aparecem em produção.

**GEN-014 — DEVE: sem código morto ou comentado.** Código não usado é removido;
o histórico fica no Git.
_Por quê:_ código morto confunde leitores e apodrece silenciosamente.

**GEN-015 — DEVERIA: preferir imutabilidade.** Declare valores como imutáveis por
padrão e só torne mutáveis quando necessário.
_Por quê:_ reduz uma classe inteira de bugs de estado compartilhado.

## 3. Design e dependências

**GEN-020 — DEVERIA: simplicidade primeiro (KISS/YAGNI).** Implemente o que o
requisito atual pede. Abstrações, extensões e configurações "para o futuro" só
quando houver um segundo caso real.
_Por quê:_ abstração prematura custa mais para desfazer do que duplicação.

**GEN-021 — DEVERIA: eliminar duplicação de conhecimento, não de texto (DRY).**
Uma regra de negócio vive em um só lugar. Dois trechos parecidos que mudam por
motivos diferentes podem e devem continuar separados.
_Por quê:_ acoplar coisas que só se parecem por acaso cria dependências falsas.

**GEN-022 — DEVERIA: depender de abstrações nas fronteiras.** Código de domínio
não conhece detalhes de framework, banco, fila ou HTTP; recebe dependências por
injeção (construtor/parâmetro), não por instanciação interna ou estado global.
_Por quê:_ permite testar sem infraestrutura e trocar detalhes sem reescrever
regras.

**GEN-023 — DEVE: dependências externas justificadas e fixadas.** Toda
biblioteca nova tem motivo (no PR), licença compatível e versão fixada via
lockfile versionado.
_Por quê:_ cada dependência é superfície de ataque e custo de manutenção.

**GEN-024 — DEVE: sem estado global mutável.** Singletons com estado,
variáveis globais e caches estáticos mutáveis são proibidos fora de
infraestrutura explicitamente projetada para isso.
_Por quê:_ estado global torna o comportamento dependente de ordem de execução.

## 4. Tratamento de erros

**GEN-030 — DEVE: nunca engolir erros.** Todo `catch` trata, enriquece e
relança, ou registra com contexto suficiente. `catch {}` vazio é proibido.
_Por quê:_ erro engolido vira dado corrompido ou comportamento inexplicável.

**GEN-031 — DEVE: falhar cedo e de forma explícita.** Valide entradas na
borda e rejeite estados inválidos imediatamente, com mensagem clara, em vez de
propagar `null`/valores padrão silenciosos.
_Por quê:_ quanto mais longe da causa, mais caro é diagnosticar.

**GEN-032 — DEVERIA: distinguir erros esperados de inesperados.** Erros de
negócio previsíveis (validação, não encontrado, conflito) são modelados como
tipos/resultados explícitos; exceções ficam para falhas realmente excepcionais.
_Por quê:_ fluxo de negócio via exceção genérica é difícil de seguir e de mapear
para respostas corretas.

**GEN-033 — DEVE: preservar a causa original.** Ao encapsular um erro, mantenha
o erro original (cause/inner) e o stack trace.
_Por quê:_ sem a causa raiz, o diagnóstico vira adivinhação.

## 5. Logs e observabilidade

**GEN-040 — DEVE: logs estruturados.** Logs são emitidos em formato estruturado
(chave-valor/JSON) com nível, timestamp em UTC, mensagem e contexto.
_Por quê:_ logs estruturados são pesquisáveis e agregáveis; texto livre não.

**GEN-041 — DEVE: níveis de log com significado.** `ERROR` exige ação humana;
`WARN` é anomalia tolerada; `INFO` são eventos de negócio relevantes; `DEBUG` é
detalhe de diagnóstico desligado em produção por padrão.
_Por quê:_ se tudo é ERROR, nada é — alertas perdem valor.

**GEN-042 — DEVE: correlação entre chamadas.** Toda requisição/mensagem carrega
um ID de correlação (ou trace ID) propagado entre serviços e presente em todos
os logs.
_Por quê:_ sem correlação, é impossível reconstruir um fluxo distribuído.

**GEN-043 — DEVE: nunca registrar dados sensíveis.** Senhas, tokens, chaves,
documentos pessoais, dados de cartão e de saúde não aparecem em logs, métricas,
traces ou mensagens de erro. Mascare quando precisar identificar
(`***-***-123`).
_Por quê:_ logs são amplamente acessíveis e retidos por muito tempo; vazamento
por log é incidente de segurança e de LGPD.

## 6. Configuração e segredos

**GEN-050 — DEVE: segredos fora do código.** Credenciais, chaves e connection
strings vêm de variáveis de ambiente ou cofre de segredos, nunca do repositório
(nem em arquivos de exemplo com valores reais). O CI **DEVERIA** rodar scanner
de segredos.
_Por quê:_ segredo commitado deve ser considerado vazado, mesmo após remoção.

**GEN-051 — DEVE: configuração por ambiente, código único.** O mesmo artefato
roda em todos os ambientes; só a configuração muda. Nada de `if (env == "prod")`
espalhado pela lógica.
_Por quê:_ o que foi testado é o que vai para produção.

**GEN-052 — DEVE: configuração validada na inicialização.** A aplicação falha ao
subir se uma configuração obrigatória estiver ausente ou inválida.
_Por quê:_ é melhor falhar no deploy do que na primeira requisição real.

## 7. Segurança (base)

**GEN-060 — DEVE: nunca confiar em entrada externa.** Todo dado vindo de
usuário, rede, arquivo, fila ou outro sistema é validado (tipo, formato,
tamanho, faixa) antes do uso.
_Por quê:_ a maioria das vulnerabilidades nasce de entrada não validada.

**GEN-061 — DEVE: menor privilégio.** Serviços, usuários de banco, tokens e
identidades têm apenas as permissões necessárias.
_Por quê:_ limita o estrago quando algo é comprometido.

**GEN-062 — DEVE: não implementar criptografia própria.** Use bibliotecas e
algoritmos consagrados; senhas com hash adaptativo (Argon2, bcrypt, scrypt),
nunca hash simples ou criptografia reversível.
_Por quê:_ criptografia caseira é quase sempre quebrável.

**GEN-063 — DEVE: dependências sem vulnerabilidades conhecidas críticas.** O CI
verifica vulnerabilidades em dependências; achados críticos/altos bloqueiam o
merge ou têm exceção registrada.
_Por quê:_ boa parte dos ataques explora bibliotecas desatualizadas.

## 8. Testes

**GEN-070 — DEVE: todo comportamento novo ou bug corrigido vem com teste.**
Correção de bug começa pelo teste: primeiro um teste que reproduz o bug e
falha, depois a correção que o faz passar.
_Por quê:_ impede regressão, documenta o comportamento esperado e prova que o
teste realmente detecta o problema.

**GEN-071 — DEVERIA: pirâmide de testes.** Muitos testes unitários rápidos,
menos testes de integração, poucos testes ponta a ponta.
_Por quê:_ equilibra confiança, velocidade e custo de manutenção.

**GEN-072 — DEVE: testes determinísticos e independentes.** Sem dependência de
ordem, relógio real, aleatoriedade não controlada ou serviços externos reais
em testes unitários. Teste intermitente é tratado como bug.
_Por quê:_ testes instáveis destroem a confiança na suíte.

**GEN-073 — DEVERIA: testar comportamento, não implementação.** Asserções sobre
resultados observáveis, não sobre detalhes internos; estrutura
Arrange/Act/Assert; nome do teste descreve cenário e resultado esperado.
_Por quê:_ testes acoplados à implementação quebram em todo refactor.

**GEN-074 — DEVERIA: cobertura como sinal, não como meta.** Acompanhe cobertura
para achar áreas não testadas; não escreva testes sem asserção para subir o
número.
_Por quê:_ cobertura alta com testes vazios dá falsa segurança.

**GEN-075 — DEVERIA: TDD para regras de domínio e lógica não trivial.** Siga o
ciclo **red → green → refactor**:

1. **Red:** escreva um teste pequeno para o próximo comportamento e veja-o
   falhar pelo motivo certo.
2. **Green:** escreva o mínimo de código de produção para passar.
3. **Refactor:** melhore o design (nomes, duplicação, estrutura) com todos os
   testes verdes.

Ciclos curtos (minutos, não horas). Código de cola, UI exploratória e spikes
podem dispensar test-first. Código de spike é descartado ou reescrito com
testes antes de ir para a branch principal.
_Por quê:_ escrever o teste antes força a pensar no comportamento e na
interface do ponto de vista de quem usa, produz código testável por
construção e deixa uma suíte que permite refatorar com segurança.

## 9. Comentários e documentação

**GEN-080 — DEVERIA: comentar o porquê, não o quê.** O código diz o que faz;
comentários explicam decisões, restrições e armadilhas não óbvias.
_Por quê:_ comentários que repetem o código ficam desatualizados e mentem.

**GEN-081 — DEVE: TODO com dono e rastreio.** `TODO` só com referência a uma
tarefa/épico (`TODO(EP-012): ...`).
_Por quê:_ TODO sem rastreio é dívida invisível.

**GEN-082 — DEVE: README executável.** O README do projeto explica como
instalar, configurar, rodar e testar localmente, e esses passos funcionam.
_Por quê:_ onboarding é o primeiro teste da qualidade do projeto.

## 10. Controle de versão e revisão

**GEN-090 — DEVE: commits pequenos e atômicos** seguindo Conventional Commits
(ver `CLAUDE.md`). Um commit = uma mudança lógica que compila e passa nos
testes.
_Por quê:_ facilita revisão, `bisect` e reversão.

**GEN-091 — DEVE: toda mudança passa por revisão e CI verde** antes de entrar
na branch principal.
_Por quê:_ revisão espalha conhecimento e pega o que ferramentas não pegam.

**GEN-092 — DEVE: formatação automática.** O formatador da stack roda no
pre-commit ou CI; formatação não é tema de review.
_Por quê:_ elimina diffs de ruído e discussões improdutivas.

**GEN-093 — DEVERIA: PRs pequenos e focados** (alvo: revisável em menos de 30
min). Refactors grandes vão em PR separado da mudança funcional.
_Por quê:_ revisão de PR grande é superficial.

## 11. Entrega incremental (vertical slicing)

**GEN-100 — DEVERIA: fatiar o trabalho verticalmente.** Cada tarefa de épico e
cada PR entrega uma fatia **de ponta a ponta** (persistência → regra → API → UI,
o que a funcionalidade exigir) que funciona e pode ser demonstrada, mesmo que
estreita (ex.: "criar pedido com um item, sem desconto"). Evite fatias
horizontais ("PR do banco", depois "PR da API", depois "PR da UI").
_Por quê:_ cada fatia gera valor e feedback reais, expõe cedo problemas de
integração e pode ir para produção sozinha (atrás de flag, se incompleta).

**GEN-101 — DEVERIA: fatias pelo critério INVEST** — independentes,
negociáveis, valiosas, estimáveis, pequenas e testáveis. Uma fatia que não cabe
em poucos dias é dividida por cenário, regra de negócio, tipo de dado ou caminho
feliz/exceção, nunca por camada técnica.
_Por quê:_ fatias pequenas e independentes reduzem risco e mantêm o fluxo de
entrega contínuo.

## 12. Feature flags

Valem para backend e frontend. Uma flag é código condicional temporário (ou
explicitamente permanente) controlado por configuração em tempo de execução.

**GEN-110 — DEVE: toda flag tem tipo, dono e destino.** Registre, no código ou
no catálogo de flags do projeto:

| Tipo            | Uso                                              | Vida esperada        |
| --------------- | ------------------------------------------------ | -------------------- |
| **Release**     | Esconder funcionalidade incompleta ou em rollout | Dias a semanas       |
| **Experimento** | Teste A/B, medir impacto                         | Duração do experimento |
| **Operacional** | Kill switch, degradação controlada               | Permanente (explícita) |
| **Permissão**   | Liberar funcionalidade por plano/cliente         | Permanente (explícita) |

Flags de release e de experimento têm dono e data prevista de remoção.
_Por quê:_ sem dono e prazo, flags temporárias viram dívida permanente.

**GEN-111 — DEVE: remover flags temporárias após o rollout completo.** A
remoção (flag, código do caminho antigo e testes dele) é uma tarefa no mesmo
épico, não "depois".
_Por quê:_ cada flag viva duplica os caminhos possíveis do sistema; flag
esquecida já causou incidentes graves (código antigo reativado por engano).

**GEN-112 — DEVE: padrão seguro quando a flag não puder ser avaliada.** Se o
provedor de flags falhar ou a flag não existir, o sistema assume um valor
padrão definido no código, em geral o comportamento já estabelecido
(funcionalidade nova desligada).
_Por quê:_ indisponibilidade do serviço de flags não pode virar incidente.

**GEN-113 — DEVE: testar os dois estados da flag.** Os testes cobrem o
comportamento com a flag ligada e desligada enquanto ela existir.
_Por quê:_ o caminho "desligado" é o que roda quando algo dá errado.

**GEN-114 — DEVERIA: avaliar a flag em um ponto só, o mais perto da borda
possível.** Decida uma vez (no caso de uso, rota ou composição de componentes) e
passe o resultado adiante. Não espalhe `if (flag)` pelo código nem aninhe
flags.
_Por quê:_ verificações espalhadas e combinações de flags tornam o
comportamento impossível de prever e de testar.

**GEN-115 — DEVE: flag não é controle de segurança.** Esconder uma
funcionalidade atrás de flag não substitui autenticação e autorização
(BE-021, FE-053). Flags de permissão são avaliadas no servidor.
_Por quê:_ flags avaliadas no cliente são visíveis e manipuláveis.

**GEN-116 — DEVERIA: mudanças de flag são auditadas** (quem, quando, valor
anterior e novo) e tratadas como mudança de produção.
_Por quê:_ ligar uma flag é um deploy de comportamento; precisa ser
rastreável no diagnóstico de incidentes.
