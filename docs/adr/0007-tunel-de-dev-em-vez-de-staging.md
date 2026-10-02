---
status: accepted
---

# Túnel de dev (Cloudflare) em vez de ambiente de staging

ChatGPT e Claude.ai chamam o MCP e o OAuth a partir da nuvem deles, então
validar o fluxo central (§63) exige HTTPS público com hostname estável. O
usuário decidiu não fazer deploy antes do fim do roadmap. Por isso usamos um
Cloudflare named tunnel, num domínio do usuário, apontando para a máquina de
dev. Ele é ligado só em sessões de aceite e expõe apenas MCP, OAuth e as
páginas de login/consentimento. O banco exposto tem só os dados do próprio
usuário. Isso é exposição de dev, não deploy. Os testes automatizados rodam o
fluxo inteiro no processo, sem túnel.

## Considered Options

- **Cloudflare Quick Tunnel:** URL aleatória e sem SSE.
- **ngrok free:** interstitial na tela de consentimento e cotas.
- **MS Dev Tunnels:** a URL muda.
- **Staging antecipado:** contradiz a decisão do usuário de deixar infra para o
  fim.

Origem: DA-027 e DA-028, épico `docs/epics/backlog/e03-conexao-de-agentes.md`.
