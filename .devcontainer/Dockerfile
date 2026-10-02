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

USER root

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl gnupg \
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

# psql client, for inspecting the compose database by hand.
RUN if [ "$INSTALL_PSQL" = "true" ]; then \
        apt-get update \
        && apt-get install -y --no-install-recommends postgresql-client \
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
