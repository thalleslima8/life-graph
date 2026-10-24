# Runbook: túnel de dev para o aceite com agentes (E3)

Expõe o host de dev para ChatGPT, Claude.ai e Claude Code durante uma **sessão
de aceite**, por um Cloudflare named tunnel num domínio seu (DA-027, ADR 0007).
Não é deploy: o túnel só fica ligado enquanto a sessão dura.

## O que fica exposto (DA-028)

Só estas rotas, sob o hostname público:

- `/mcp` (o endpoint MCP; exige o token do agente);
- `/.well-known/*` (discovery do emissor e Protected Resource Metadata);
- `/connect/*` (autorização, token, login e consentimento do emissor).

Todo o resto responde 404 em duas camadas: as regras de ingress do túnel e a
allowlist do próprio host (`Host:PublicExposure:Hosts`). A SPA, a API `/api`,
o OpenAPI e os health checks nunca passam pelo túnel. O banco exposto é o
`lifegraph_public`, com só os seus dados.

## Uma vez

1. **Domínio no Cloudflare** (pré-requisito do usuário no E3): o DNS do domínio
   aponta para o Cloudflare.
2. **Túnel:** em Zero Trust > Networks > Tunnels, crie um túnel do tipo
   *Cloudflared* e copie o token.
3. **Hostname público com regras por caminho:** no túnel, adicione três
   *public hostnames* para o mesmo host (ex.: `agents.seudominio.com`), todos
   com o serviço `http://lifegraph-app-dev:5000`:
   - caminho `^/mcp(/.*)?$`;
   - caminho `^/\.well-known/.*`;
   - caminho `^/connect/.*`.

   Sem uma regra que case, o túnel responde 404. Opcional: Cloudflare Access
   **só** em `^/connect/(login|authorize)` (páginas de navegador), nunca em
   `/connect/token`, `/mcp` ou `/.well-known/*`, que os agentes chamam da
   nuvem deles.
4. **Configuração local** (dentro do devcontainer):

   ```bash
   cp .devcontainer/.env.public.example .devcontainer/.env.public
   ```

   Preencha `TUNNEL_TOKEN`, `LIFEGRAPH_PUBLIC_HOSTNAME`, `LIFEGRAPH_PUBLIC_EMAIL`
   e confira os redirect URIs dos clientes pré-registrados na documentação de
   cada um (o emissor compara exatamente). O arquivo é ignorado pelo git; nunca
   o versione.
5. **Banco `dev-public` e sua conta:**

   ```bash
   scripts/public-host.sh init-db          # cria lifegraph_public e aplica as migrations
   scripts/public-host.sh create-account   # e-mail de definir senha no Mailpit (porta 8025)
   ```

## Em cada sessão

1. No devcontainer, suba o host apontado para o banco `dev-public`:

   ```bash
   scripts/public-host.sh run
   ```

   As chaves do emissor ficam em `~/.lifegraph/issuer-keys` (fora do repo,
   permissão 600) e sobrevivem a reinícios, então os agentes conectados
   continuam valendo. Rebuild do container gera chaves novas: basta conectar de
   novo.
2. Na máquina host, ligue o túnel:

   ```bash
   docker compose -f .devcontainer/compose.public.yml --profile public up -d
   ```

3. Confira de fora:
   - `https://agents.seudominio.com/.well-known/oauth-authorization-server`
     responde o documento do emissor;
   - `https://agents.seudominio.com/api/sessions/current` responde 404;
   - um `POST` em `/mcp` sem token responde 401 com `resource_metadata`.
4. Faça o aceite:
   - **Claude.ai:** conector personalizado com a URL
     `https://agents.seudominio.com/mcp` e, nas opções avançadas, o client ID
     `LIFEGRAPH_CLAUDE_CLIENT_ID` (sem secret).
   - **ChatGPT (modo desenvolvedor):** conector com a mesma URL e o client ID
     `LIFEGRAPH_CHATGPT_CLIENT_ID`.
   - **Claude Code:** `claude mcp add --transport http lifegraph https://agents.seudominio.com/mcp`.
     Ele se identifica pelo documento de metadados (CIMD), sem registro prévio.

   Em cada um: o login do emissor pede a senha (o cookie da sessão não viaja no
   redirect vindo de outro site, DA-120), a tela de consentimento mostra o
   hostname de retorno e o que o agente pode fazer, e a ferramenta `whoami`
   responde o nome da conexão.
5. Revogue o que não for manter em **Agentes conectados** (SPA local em
   `http://localhost:5173`, que fala com o mesmo host).
6. **Desligue o túnel** ao terminar:

   ```bash
   docker compose -f .devcontainer/compose.public.yml --profile public down
   ```

## Se algo falhar

- **`invalid_target` ou o agente não acha o emissor:** o `Issuer` precisa ser
  exatamente `https://<hostname público>/`; o script deriva do
  `LIFEGRAPH_PUBLIC_HOSTNAME`.
- **Cookies sem efeito no login:** o host só confia no `X-Forwarded-Proto` do
  `cloudflared` vindo da rede do devcontainer (`LIFEGRAPH_TUNNEL_NETWORK`,
  detectada do `eth0`).
- **Redirect recusado:** o redirect URI do cliente não bate com o configurado;
  confira o `.env.public`. Loopback (`http://localhost`, `127.0.0.1`) vale em
  qualquer porta.
- **Muitas tentativas (429):** os limites de `/connect/*` são por endereço de
  cliente (`Accounts:OAuthRateLimits`). Espere o tempo do `Retry-After`.
