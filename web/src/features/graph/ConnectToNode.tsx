import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useId, useRef, useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { SEARCH_MAX_LENGTH, SEARCH_MIN_LENGTH, isSearchable, searchQuery } from "@/features/search/searchData";
import { CONNECT_KIND_DEFAULT, useAccountRoot } from "./anchoringData";
import { isNotFound, nodeRelationsQuery, useCreateRelation } from "./graphData";
import { LOAD_ERROR_MESSAGE, RETRY_LABEL, ROOT_MISSING_MESSAGE, writeErrorMessage } from "./graphMessages";

type ConnectToNodeProps = {
  nodeId: string;
  /** The root connects nothing to itself: no form for it. */
  isAccountRoot?: boolean;
  /** After the Relation is written, with the Node it points to. */
  onConnected?: (targetId: string) => void;
};

export const CONNECTED_MESSAGE = "Conectado.";
export const ALREADY_CONNECTED_MESSAGE = "Já conectado.";
export const TARGET_GONE_MESSAGE = "Esse Node não existe mais. A busca foi refeita.";

/**
 * "Conectar a…" (DA-197, DA-208): a Relation from this Node to the root, in one click, or to a
 * Node found by a search that runs on submit (DA-183). It is the same create-Relation write as
 * the Inspector's form (DA-013), kind related_to, this Node to the target (DA-193).
 */
export function ConnectToNode({ nodeId, isAccountRoot = false, onConnected }: ConnectToNodeProps) {
  if (isAccountRoot) {
    return null;
  }

  return <ConnectToNodeForm nodeId={nodeId} onConnected={onConnected} />;
}

function ConnectToNodeForm({ nodeId, onConnected }: Omit<ConnectToNodeProps, "isAccountRoot">) {
  const id = useId();
  const fieldRef = useRef<HTMLInputElement>(null);
  const queryClient = useQueryClient();
  const root = useAccountRoot();
  const create = useCreateRelation();
  const [text, setText] = useState("");
  const [submitted, setSubmitted] = useState("");
  const [tooShort, setTooShort] = useState(false);
  const [outcome, setOutcome] = useState<string>();
  const checking = useRef(false);
  const search = useQuery(searchQuery({ q: submitted }));
  const candidates = (search.data?.items ?? []).filter((item) => item.id !== nodeId && item.id !== root.rootId);

  const onSearch = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setOutcome(undefined);
    create.reset();
    if (!isSearchable(text)) {
      setTooShort(true);
      return;
    }

    setTooShort(false);
    if (text.trim() === submitted.trim()) {
      void search.refetch();
    } else {
      setSubmitted(text);
    }
  };

  // "Já conectado" (DA-208): a Hard Relation of any kind between the two Nodes, in either
  // direction, already anchors this one through the other, so a second one adds nothing. The
  // Node's Relations are read when nothing is cached yet (the Nós soltos page shows none), from
  // the first page the Inspector reads too. A failed read leaves the decision to the write.
  const isConnectedTo = async (targetId: string) => {
    try {
      const relations = await queryClient.ensureQueryData(nodeRelationsQuery(nodeId));
      return relations.data.some(
        (relation) => relation.assertion === "hard" && (relation.sourceNodeId === targetId || relation.targetNodeId === targetId),
      );
    } catch {
      return false;
    }
  };

  const connect = async (targetId: string) => {
    if (create.isPending || checking.current) {
      return;
    }

    setOutcome(undefined);
    checking.current = true;
    try {
      if (await isConnectedTo(targetId)) {
        setOutcome(ALREADY_CONNECTED_MESSAGE);
        return;
      }
    } finally {
      checking.current = false;
    }

    create.mutate(
      { sourceNodeId: nodeId, targetNodeId: targetId, kind: CONNECT_KIND_DEFAULT },
      {
        onSuccess: () => {
          setOutcome(CONNECTED_MESSAGE);
          fieldRef.current?.focus();
          onConnected?.(targetId);
        },
        onError: (error) => {
          if (isNotFound(error) && isSearchable(submitted)) {
            void search.refetch();
          }
        },
      },
    );
  };

  const fieldId = `${id}-connect`;
  const hintId = `${id}-connect-hint`;

  return (
    <div className="space-y-2">
      <form noValidate role="search" aria-label="Conectar a…" onSubmit={onSearch} className="flex flex-wrap items-end gap-2">
        <div className="space-y-2">
          <Label htmlFor={fieldId}>Conectar a…</Label>
          <Input
            id={fieldId}
            ref={fieldRef}
            type="search"
            value={text}
            maxLength={SEARCH_MAX_LENGTH}
            onChange={(event) => setText(event.target.value)}
            aria-describedby={tooShort ? hintId : undefined}
          />
        </div>
        <Button type="submit" variant="secondary">
          Buscar
        </Button>
        <RootShortcut root={root} disabled={create.isPending} onConnect={(rootId) => void connect(rootId)} />
      </form>
      {tooShort && (
        <p id={hintId} className="text-sm text-muted-foreground">
          Digite ao menos {SEARCH_MIN_LENGTH} letras para buscar.
        </p>
      )}
      <SearchState search={search} isEmpty={candidates.length === 0} onRetry={() => void search.refetch()} enabled={isSearchable(submitted)} />
      {candidates.length > 0 && (
        <ul aria-label="Nodes para conectar" className="divide-y rounded-md border">
          {candidates.map((candidate) => (
            <li key={candidate.id} className="flex items-center justify-between gap-2 px-3 py-2">
              <span className="min-w-0">
                <span className="font-medium">{candidate.title}</span>
                {candidate.typeName && <span className="ml-2 text-sm text-muted-foreground">{candidate.typeName}</span>}
              </span>
              <Button
                type="button"
                size="sm"
                variant="secondary"
                disabled={create.isPending}
                onClick={() => void connect(candidate.id)}
                aria-label={`Conectar a ${candidate.title}`}
              >
                Conectar
              </Button>
            </li>
          ))}
        </ul>
      )}
      {create.isError && (
        <p role="alert" className="text-sm text-destructive">
          {isNotFound(create.error) ? TARGET_GONE_MESSAGE : writeErrorMessage(create.error)}
        </p>
      )}
      {outcome && (
        <p role="status" className="text-sm">
          {outcome}
        </p>
      )}
    </div>
  );
}

function RootShortcut({
  root,
  disabled,
  onConnect,
}: {
  root: ReturnType<typeof useAccountRoot>;
  disabled: boolean;
  onConnect: (rootId: string) => void;
}) {
  if (root.isError && !root.isRootMissing) {
    return (
      <span className="flex items-center gap-2">
        <span role="alert" className="text-sm text-destructive">
          Não foi possível carregar a Raiz.
        </span>
        <Button type="button" variant="ghost" size="sm" onClick={() => void root.refetch()}>
          {RETRY_LABEL}
        </Button>
      </span>
    );
  }

  return (
    <span className="flex items-center gap-2">
      <Button
        type="button"
        disabled={disabled || root.rootId === undefined}
        aria-busy={root.isLoading || undefined}
        onClick={() => root.rootId && onConnect(root.rootId)}
      >
        Conectar à Raiz
      </Button>
      {root.isRootMissing && <span className="text-sm text-muted-foreground">{ROOT_MISSING_MESSAGE}</span>}
    </span>
  );
}

function SearchState({
  search,
  isEmpty,
  enabled,
  onRetry,
}: {
  search: { isFetching: boolean; isError: boolean; isSuccess: boolean };
  isEmpty: boolean;
  enabled: boolean;
  onRetry: () => void;
}) {
  if (!enabled) {
    return null;
  }

  if (search.isFetching) {
    return (
      <p role="status" className="text-sm text-muted-foreground">
        Buscando…
      </p>
    );
  }

  if (search.isError) {
    return (
      <p className="flex items-center gap-2 text-sm">
        <span role="alert" className="text-destructive">
          {LOAD_ERROR_MESSAGE}
        </span>
        <Button type="button" variant="ghost" size="sm" onClick={onRetry}>
          {RETRY_LABEL}
        </Button>
      </p>
    );
  }

  if (search.isSuccess && isEmpty) {
    return (
      <p role="status" className="text-sm text-muted-foreground">
        Nenhum Node encontrado.
      </p>
    );
  }

  return null;
}
