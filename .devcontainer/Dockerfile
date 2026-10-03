# Image of the devcontainer `app` service, derived from the limaj-framework template
# (template-backend/.devcontainer). See .devcontainer/README.md.
#
# Base: devcontainer .NET 10 (Debian, SDK 10.0 + user `vscode` uid 1000).
# Template layers dropped for this product: Azure Functions Core Tools (INSTALL_FUNC=false)
# and mssql-tools (Postgres only).
#
# The Docker socket is NOT mounted (isolation): integration tests use the compose
# postgres service instead of Testcontainers here (DA-093).

FROM mcr.microsoft.com/devcontainers/dotnet:10.0

ARG INSTALL_NODE=true
ARG INSTALL_PSQL=true
ARG INSTALL_GH=true
# Must match the major of the server image in compose.postgres.yml (pgvector:pg17):
# pg_dump refuses to dump a server newer than itself.
ARG PG_MAJOR=17

USER root

# jq: post-create.sh merges the Claude allowlist into ~/.claude/settings.json.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl gnupg jq \
    && mkdir -p /etc/apt/keyrings \
    && rm -rf /var/lib/apt/lists/*

# Node 24 LTS via NodeSource: Vite frontend and npm tooling.
RUN if [ "$INSTALL_NODE" = "true" ]; then \
        curl -fsSL https://deb.nodesource.com/gpgkey/nodesource-repo.gpg.key \
            | gpg --dearmor -o /etc/apt/keyrings/nodesource.gpg \
        && echo "deb [signed-by=/etc/apt/keyrings/nodesource.gpg] https://deb.nodesource.com/node_24.x nodistro main" \
            > /etc/apt/sources.list.d/nodesource.list \
        && apt-get update \
        && apt-get install -y --no-install-recommends nodejs \
        && rm -rf /var/lib/apt/lists/* ; \
    fi

# psql/pg_dump from the PGDG repo, pinned to the server major (the distro package
# lags behind it).
RUN if [ "$INSTALL_PSQL" = "true" ]; then \
        curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc \
            | gpg --dearmor -o /etc/apt/keyrings/pgdg.gpg \
        && echo "deb [signed-by=/etc/apt/keyrings/pgdg.gpg] https://apt.postgresql.org/pub/repos/apt $(. /etc/os-release && echo "$VERSION_CODENAME")-pgdg main" \
            > /etc/apt/sources.list.d/pgdg.list \
        && apt-get update \
        && apt-get install -y --no-install-recommends "postgresql-client-${PG_MAJOR}" \
        && rm -rf /var/lib/apt/lists/* ; \
    fi

# GitHub CLI: the container allowlist permits Bash(gh:*). No host credentials are
# mounted, so authenticate inside the container (`gh auth login`) when needed.
RUN if [ "$INSTALL_GH" = "true" ]; then \
        curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg \
            -o /etc/apt/keyrings/githubcli-archive-keyring.gpg \
        && chmod go+r /etc/apt/keyrings/githubcli-archive-keyring.gpg \
        && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" \
            > /etc/apt/sources.list.d/github-cli.list \
        && apt-get update \
        && apt-get install -y --no-install-recommends gh \
        && rm -rf /var/lib/apt/lists/* ; \
    fi

# Claude Code: npm when Node is present, native installer otherwise.
RUN if [ "$INSTALL_NODE" = "true" ]; then \
        npm install -g --unsafe-perm @anthropic-ai/claude-code ; \
    else \
        curl -fsSL https://claude.ai/install.sh | bash || true ; \
    fi

# dotnet-ef is a LOCAL tool (.config/dotnet-tools.json), restored by post-create.sh.
# The global tools dir stays on PATH for ad-hoc tools.
RUN echo 'export PATH="$PATH:/home/vscode/.dotnet/tools"' > /etc/profile.d/dotnet-tools.sh
ENV PATH="${PATH}:/home/vscode/.dotnet/tools"

USER vscode
