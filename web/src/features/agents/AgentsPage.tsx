import { zodResolver } from "@hookform/resolvers/zod";
import { useInfiniteQuery } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { isApiError } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { formatDateTime, writeErrorMessage } from "@/features/graph/graphMessages";
import {
  AGENT_NAME_MAX_LENGTH,
  agentIdentitiesQuery,
  agentNameSchema,
  isAgentIdentityGone,
  SCOPE_LABELS,
  useRenameAgentIdentity,
  useRevokeAgentIdentity,
  type AgentIdentityView,
} from "./agentIdentities";

const TITLE_ID = "agents-title";
const LOAD_ERROR_MESSAGE = "Não foi possível carregar as conexões.";
const GONE_MESSAGE = "Esta conexão já não está conectada. A lista foi atualizada.";

const renameSchema = z.object({ name: agentNameSchema });

/**
 * "Agentes conectados" (DA-030, DA-125): every connection of the Account that is not revoked,
 * with what it may do and when it was last used; rename and revoke each one. The page owns all
 * its states; a 401 is the app shell's.
 */
export function AgentsPage() {
  const connections = useInfiniteQuery(agentIdentitiesQuery);
  const [announcement, setAnnouncement] = useState("");
  const titleRef = useRef<HTMLHeadingElement>(null);
  const rowButtons = useRef(new Map<string, HTMLButtonElement>());
  // After a revocation, once the list is read again: the next connection's first button, or the title.
  const focusAfterRevoke = useRef<string | null | undefined>(undefined);
  useEffect(() => {
    if (focusAfterRevoke.current === undefined || connections.isFetching) {
      return;
    }

    const nextId = focusAfterRevoke.current;
    focusAfterRevoke.current = undefined;
    const next = nextId === null ? undefined : rowButtons.current.get(nextId);
    (next ?? titleRef.current)?.focus();
  }, [connections.isFetching, connections.data, announcement]);

  const onRevoked = (agent: AgentIdentityView) => {
    const list = connections.data ?? [];
    const index = list.findIndex((item) => item.id === agent.id);
    const next = list[index + 1] ?? list[index - 1];
    focusAfterRevoke.current = next?.id ?? null;
    setAnnouncement(`${agent.name} foi desconectado.`);
  };

  return (
    <section aria-labelledby={TITLE_ID} className="space-y-6">
      <h1 id={TITLE_ID} ref={titleRef} tabIndex={-1} className="text-2xl font-semibold">
        Agentes conectados
      </h1>
      <p role="status" className="sr-only">
        {announcement}
      </p>

      {connections.isPending ? (
        <p>Carregando…</p>
      ) : connections.isError ? (
        <div className="space-y-2">
          <p role="alert" className="text-sm text-destructive">
            {LOAD_ERROR_MESSAGE}
          </p>
          <Button type="button" variant="secondary" size="sm" onClick={() => void connections.refetch()}>
            Tentar de novo
          </Button>
        </div>
      ) : connections.data.length === 0 ? (
        <EmptyState />
      ) : (
        <>
          <ul aria-labelledby={TITLE_ID} className="divide-y rounded-md border">
            {connections.data.map((agent) => (
              <AgentRow
                key={agent.id}
                agent={agent}
                registerButton={(button) => {
                  if (button) {
                    rowButtons.current.set(agent.id, button);
                  } else {
                    rowButtons.current.delete(agent.id);
                  }
                }}
                onRevoked={() => onRevoked(agent)}
                onGone={() => setAnnouncement(GONE_MESSAGE)}
              />
            ))}
          </ul>
          {connections.hasNextPage && (
            <Button type="button" variant="secondary" onClick={() => void connections.fetchNextPage()} disabled={connections.isFetchingNextPage}>
              Carregar mais
            </Button>
          )}
        </>
      )}
    </section>
  );
}

function EmptyState() {
  return (
    <div className="space-y-2 rounded-md border p-4 text-sm">
      <p>Nenhum agente conectado.</p>
      <p>
        Para conectar um agente (Claude, ChatGPT, Claude Code…), adicione o Life Graph como conector MCP no aplicativo do agente. Ele vai abrir
        uma página do Life Graph para você entrar e autorizar; a conexão aparece aqui.
      </p>
    </div>
  );
}

function AgentRow({
  agent,
  registerButton,
  onRevoked,
  onGone,
}: {
  agent: AgentIdentityView;
  registerButton: (button: HTMLButtonElement | null) => void;
  onRevoked: () => void;
  onGone: () => void;
}) {
  const [renaming, setRenaming] = useState(false);
  const [confirmingRevoke, setConfirmingRevoke] = useState(false);
  const renameButton = useRef<HTMLButtonElement | null>(null);
  const revokeButton = useRef<HTMLButtonElement>(null);
  const confirmButton = useRef<HTMLButtonElement>(null);
  const revoke = useRevokeAgentIdentity(agent.id);
  const returnFocusToRevoke = useRef(false);
  const returnFocusToRename = useRef(false);
  const scopes = agent.scopes.filter((scope) => scope in SCOPE_LABELS);

  useEffect(() => {
    if (confirmingRevoke) {
      confirmButton.current?.focus();
    } else if (returnFocusToRevoke.current) {
      returnFocusToRevoke.current = false;
      revokeButton.current?.focus();
    }
  }, [confirmingRevoke]);

  // The form focuses its own field when it opens; closing it returns focus to "Renomear".
  useEffect(() => {
    if (!renaming && returnFocusToRename.current) {
      returnFocusToRename.current = false;
      renameButton.current?.focus();
    }
  }, [renaming]);

  const closeRename = () => {
    returnFocusToRename.current = true;
    setRenaming(false);
  };

  const cancelRevoke = () => {
    returnFocusToRevoke.current = true;
    setConfirmingRevoke(false);
  };

  const confirmRevoke = () =>
    revoke.mutate(undefined, {
      onSuccess: onRevoked,
      onError: (error) => {
        if (isAgentIdentityGone(error)) {
          onGone();
        }
      },
    });

  return (
    <li aria-label={agent.name} className="space-y-3 px-4 py-4">
      <div className="flex flex-wrap items-baseline gap-2">
        <h2 className="font-medium break-words">{agent.name}</h2>
        {agent.status === "discontinued" && <span className="rounded border px-2 py-0.5 text-xs">Cliente descontinuado</span>}
      </div>

      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        <dt className="text-muted-foreground">Nome informado pelo cliente</dt>
        <dd className="break-words">{agent.clientName}</dd>
        <dt className="text-muted-foreground">Pode</dt>
        <dd>
          <ul className="list-disc pl-5">
            {scopes.map((scope) => (
              <li key={scope}>{SCOPE_LABELS[scope]}</li>
            ))}
          </ul>
        </dd>
        <dt className="text-muted-foreground">Conectado em</dt>
        <dd>{formatDateTime(agent.connectedAt)}</dd>
        <dt className="text-muted-foreground">Último uso</dt>
        <dd>{agent.lastUsedAt ? formatDateTime(agent.lastUsedAt) : "Nunca usado"}</dd>
      </dl>

      {renaming ? (
        <RenameForm agent={agent} onDone={closeRename} onGone={onGone} />
      ) : (
        <div className="flex flex-wrap gap-2">
          <Button
            ref={(button) => {
              renameButton.current = button;
              registerButton(button);
            }}
            type="button"
            variant="secondary"
            size="sm"
            onClick={() => setRenaming(true)}
          >
            Renomear {agent.name}
          </Button>
          {confirmingRevoke ? (
            <>
              <Button ref={confirmButton} type="button" size="sm" onClick={confirmRevoke} disabled={revoke.isPending}>
                Confirmar revogação
              </Button>
              <Button type="button" variant="ghost" size="sm" onClick={cancelRevoke}>
                Cancelar
              </Button>
            </>
          ) : (
            <Button ref={revokeButton} type="button" variant="secondary" size="sm" onClick={() => setConfirmingRevoke(true)}>
              Revogar {agent.name}
            </Button>
          )}
        </div>
      )}
      {confirmingRevoke && (
        <p className="text-sm text-muted-foreground">
          Revogar corta o acesso deste agente na hora e não pode ser desfeito. Para usá-lo de novo, conecte-o outra vez pelo aplicativo do agente.
        </p>
      )}
      {revoke.error !== null && (
        <p role="alert" className="text-sm text-destructive">
          {isAgentIdentityGone(revoke.error) ? GONE_MESSAGE : writeErrorMessage(revoke.error)}
        </p>
      )}
    </li>
  );
}

function RenameForm({ agent, onDone, onGone }: { agent: AgentIdentityView; onDone: () => void; onGone: () => void }) {
  const rename = useRenameAgentIdentity(agent.id);
  const {
    register,
    handleSubmit,
    setError,
    setFocus,
    formState: { errors },
  } = useForm<{ name: string }>({ resolver: zodResolver(renameSchema), defaultValues: { name: agent.name } });
  const fieldId = `rename-${agent.id}`;

  useEffect(() => {
    setFocus("name");
  }, [setFocus]);

  const onSubmit = handleSubmit(({ name }) => {
    if (rename.isPending) {
      return;
    }

    rename.mutate(
      { name },
      {
        onSuccess: onDone,
        onError: (error) => {
          const nameErrors = isApiError(error) ? error.fieldErrors.name : undefined;
          if (nameErrors?.length) {
            setError("name", { message: nameErrors.join(" ") });
          } else if (isAgentIdentityGone(error)) {
            onGone();
          }
        },
      },
    );
  });

  const otherError = rename.error && !(isApiError(rename.error) && rename.error.fieldErrors.name?.length) ? rename.error : null;

  return (
    <form noValidate onSubmit={onSubmit} aria-label={`Renomear ${agent.name}`} className="space-y-2">
      <Label htmlFor={fieldId}>Nome</Label>
      <div className="flex flex-wrap gap-2">
        <Input
          id={fieldId}
          maxLength={AGENT_NAME_MAX_LENGTH}
          aria-invalid={errors.name ? true : undefined}
          aria-describedby={errors.name ? `${fieldId}-error` : undefined}
          {...register("name")}
        />
        <Button type="submit" variant="secondary" size="sm" disabled={rename.isPending}>
          Salvar
        </Button>
        <Button type="button" variant="ghost" size="sm" onClick={onDone}>
          Cancelar
        </Button>
      </div>
      {errors.name && (
        <p id={`${fieldId}-error`} role="alert" className="text-sm text-destructive">
          {errors.name.message}
        </p>
      )}
      {otherError && (
        <p role="alert" className="text-sm text-destructive">
          {isAgentIdentityGone(otherError) ? GONE_MESSAGE : writeErrorMessage(otherError)}
        </p>
      )}
    </form>
  );
}
