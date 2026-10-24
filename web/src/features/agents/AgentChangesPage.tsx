import { useInfiniteQuery } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { Link } from "react-router";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { entryDescription, formatDateTime, LOAD_ERROR_MESSAGE, undoRefusalMessage, writeErrorMessage } from "@/features/graph/graphMessages";
import { toUndoRefusal, useUndoChangeSet, type ChangeSetItem } from "@/features/graph/recentChanges";
import {
  agentChangeSetsQuery,
  PROPOSAL_STATUSES,
  proposalsQuery,
  useFeedFilters,
  useMarkReviewed,
  type FeedFilters,
  type ProposalItem,
  type ProposalStatus,
} from "./agentChanges";
import { agentLabel, PROPOSAL_STATUS_LABELS, reasonLabels, REMOVED_AGENT_LABEL, UNAVAILABLE_UNDO_LABELS } from "./agentChangeMessages";
import { agentIdentitiesQuery } from "./agentIdentities";

const ANY = "all";
const NOT_CONNECTED_LABEL = `${REMOVED_AGENT_LABEL} ou revogado`;
const AGENTS_LOADING_LABEL = "Carregando agentes…";
const AGENTS_ERROR_MESSAGE = "Não foi possível carregar a lista de agentes para o filtro.";
const LOAD_MORE_ERROR_MESSAGE = "Não foi possível carregar mais. Tente de novo.";

/**
 * Agent Changes (v2 §65, DA-135): what agents asked for and what they did, by agent. Two
 * lists: the proposals waiting for (or decided by) the person, and the agents' GraphChangeSets,
 * "não revisado" until the person marks them. Filters live in the URL (FE-012).
 */
export function AgentChangesPage() {
  const [filters, setFilters] = useFeedFilters();

  return (
    <section aria-labelledby="agent-changes-title" className="space-y-8">
      <h1 id="agent-changes-title" className="text-2xl font-semibold">
        Agent Changes
      </h1>

      <Filters filters={filters} setFilters={setFilters} />
      <Proposals filters={filters} />
      <AgentChangeSets filters={filters} />
    </section>
  );
}

function Filters({ filters, setFilters }: { filters: FeedFilters; setFilters: (next: Partial<FeedFilters>) => void }) {
  return (
    <div className="flex flex-wrap items-end gap-4" role="group" aria-label="Filtros">
      <AgentFilter selected={filters.agentIdentityId} onSelect={(agentIdentityId) => setFilters({ agentIdentityId })} />
      <div className="w-56 space-y-2">
        <Label htmlFor="filter-status">Situação das propostas</Label>
        <Select
          value={filters.status ?? ANY}
          onValueChange={(value) => setFilters({ status: value === ANY ? undefined : (value as ProposalStatus) })}
        >
          <SelectTrigger id="filter-status">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ANY}>Todas</SelectItem>
            {PROPOSAL_STATUSES.map((status) => (
              <SelectItem key={status} value={status}>
                {PROPOSAL_STATUS_LABELS[status]}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <label className="flex h-9 items-center gap-2 text-sm">
        <input type="checkbox" checked={filters.unreviewed} onChange={(event) => setFilters({ unreviewed: event.target.checked })} />
        Só alterações não revisadas
      </label>
    </div>
  );
}

/**
 * The agent filter's options: every connected agent, all pages of the list read in turn (an
 * Account has few). The cards name their agent themselves, so this list only feeds the filter.
 */
function AgentFilter({ selected, onSelect }: { selected: string | undefined; onSelect: (agentIdentityId: string | undefined) => void }) {
  const agents = useInfiniteQuery(agentIdentitiesQuery);
  const { hasNextPage, isFetchingNextPage, isFetchNextPageError, fetchNextPage } = agents;
  useEffect(() => {
    if (hasNextPage && !isFetchingNextPage && !isFetchNextPageError) {
      void fetchNextPage();
    }
  }, [hasNextPage, isFetchingNextPage, isFetchNextPageError, fetchNextPage]);

  const options = agents.data ?? [];
  // A filter by an agent no longer listed (revoked, or past the list) still reads as chosen.
  const isSelectionUnlisted = agents.isSuccess && !hasNextPage && selected !== undefined && !options.some((agent) => agent.id === selected);

  return (
    <div className="w-56 space-y-2">
      <Label htmlFor="filter-agent">Agente</Label>
      <Select
        value={selected ?? ANY}
        onValueChange={(value) => onSelect(value === ANY ? undefined : value)}
        disabled={agents.isPending}
      >
        <SelectTrigger id="filter-agent" aria-describedby={agents.isError ? "filter-agent-error" : undefined}>
          <SelectValue placeholder={agents.isPending ? AGENTS_LOADING_LABEL : undefined} />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value={ANY}>Todos</SelectItem>
          {options.map((agent) => (
            <SelectItem key={agent.id} value={agent.id}>
              {agent.name}
            </SelectItem>
          ))}
          {isSelectionUnlisted && <SelectItem value={selected}>{NOT_CONNECTED_LABEL}</SelectItem>}
        </SelectContent>
      </Select>
      {agents.isPending && (
        <p role="status" className="text-xs text-muted-foreground">
          {AGENTS_LOADING_LABEL}
        </p>
      )}
      {agents.isError && (
        <p id="filter-agent-error" role="alert" className="flex flex-wrap items-center gap-2 text-xs text-destructive">
          {AGENTS_ERROR_MESSAGE}
          <Button type="button" variant="ghost" size="sm" className="h-auto px-1 text-xs underline underline-offset-4" onClick={() => void agents.refetch()}>
            Tentar de novo
          </Button>
        </p>
      )}
    </div>
  );
}

function Proposals({ filters }: { filters: FeedFilters }) {
  const proposals = useInfiniteQuery(proposalsQuery(filters));

  return (
    <section aria-labelledby="proposals-title" className="space-y-3">
      <h2 id="proposals-title" className="text-xl font-semibold">
        Propostas
      </h2>
      {proposals.isPending && <p role="status">Carregando…</p>}
      {proposals.isError && !proposals.isFetchNextPageError && (
        <p role="alert" className="text-sm text-destructive">
          {LOAD_ERROR_MESSAGE}
        </p>
      )}
      {/* The pages already read stay when the next one fails. */}
      {proposals.data !== undefined &&
        (proposals.data.length === 0 ? (
          <p className="text-muted-foreground">Nenhuma proposta.</p>
        ) : (
          <ol className="space-y-3" aria-labelledby="proposals-title">
            {proposals.data.map((proposal) => (
              <ProposalCard key={proposal.id} proposal={proposal} />
            ))}
          </ol>
        ))}
      {proposals.hasNextPage && (
        <Button type="button" variant="secondary" onClick={() => void proposals.fetchNextPage()} disabled={proposals.isFetchingNextPage}>
          {proposals.isFetchingNextPage ? "Carregando…" : "Carregar mais propostas"}
        </Button>
      )}
      {proposals.isFetchNextPageError && (
        <p role="alert" className="text-sm text-destructive">
          {LOAD_MORE_ERROR_MESSAGE}
        </p>
      )}
    </section>
  );
}

function ProposalCard({ proposal }: { proposal: ProposalItem }) {
  const count = Number(proposal.operationCount);
  const agentName = agentLabel(proposal.agent);
  return (
    <li className="space-y-1 rounded-md border p-3 text-sm">
      <p className="flex flex-wrap items-baseline gap-2">
        <Link to={`/agent-changes/proposals/${proposal.id}`} className="font-medium underline underline-offset-4">
          {agentName} · {count === 1 ? "1 operação" : `${count} operações`}
        </Link>
        <span className="rounded border px-1.5 text-xs">{PROPOSAL_STATUS_LABELS[proposal.status]}</span>
      </p>
      <p className="text-muted-foreground">
        {formatDateTime(proposal.createdAt)} · {reasonLabels(proposal.reasons)}
      </p>
    </li>
  );
}

function AgentChangeSets({ filters }: { filters: FeedFilters }) {
  const changeSets = useInfiniteQuery(agentChangeSetsQuery(filters));
  // What the last action on a card did. It lives above the list: after Undo or the review the
  // card's button goes away, and under "só não revisadas" the whole card does, so focus moves
  // here (FE-032). A new object per outcome, so the same message twice still moves focus.
  const [outcome, setOutcome] = useState<{ text: string }>();
  const outcomeRef = useRef<HTMLParagraphElement>(null);

  useEffect(() => {
    if (outcome) {
      outcomeRef.current?.focus();
    }
  }, [outcome]);

  return (
    <section aria-labelledby="agent-changesets-title" className="space-y-3">
      <h2 id="agent-changesets-title" className="text-xl font-semibold">
        Alterações feitas por agentes
      </h2>
      <p ref={outcomeRef} tabIndex={-1} role="status" aria-live="polite" className="text-sm">
        {outcome?.text}
      </p>
      {changeSets.isPending && <p role="status">Carregando…</p>}
      {changeSets.isError && !changeSets.isFetchNextPageError && (
        <p role="alert" className="text-sm text-destructive">
          {LOAD_ERROR_MESSAGE}
        </p>
      )}
      {/* The pages already read stay when the next one fails. */}
      {changeSets.data !== undefined &&
        (changeSets.data.length === 0 ? (
          <p className="text-muted-foreground">{filters.unreviewed ? "Nada para revisar." : "Nenhuma alteração de agente."}</p>
        ) : (
          <ol className="space-y-3" aria-labelledby="agent-changesets-title">
            {changeSets.data.map((changeSet) => (
              <AgentChangeSetCard key={changeSet.id} changeSet={changeSet} onOutcome={(text) => setOutcome({ text })} />
            ))}
          </ol>
        ))}
      {changeSets.hasNextPage && (
        <Button type="button" variant="secondary" onClick={() => void changeSets.fetchNextPage()} disabled={changeSets.isFetchingNextPage}>
          {changeSets.isFetchingNextPage ? "Carregando…" : "Carregar mais alterações"}
        </Button>
      )}
      {changeSets.isFetchNextPageError && (
        <p role="alert" className="text-sm text-destructive">
          {LOAD_MORE_ERROR_MESSAGE}
        </p>
      )}
    </section>
  );
}

function AgentChangeSetCard({ changeSet, onOutcome }: { changeSet: ChangeSetItem; onOutcome: (text: string) => void }) {
  const undo = useUndoChangeSet();
  const review = useMarkReviewed(changeSet.id);
  const titleId = `agent-changeset-${changeSet.id}`;
  const agentName = agentLabel(changeSet.agent);
  const undoState = changeSet.undo;
  const refusal = undo.isError ? toUndoRefusal(undo.error) : null;

  const onUndo = () => {
    if (undo.isPending) {
      return;
    }

    undo.mutate(changeSet, { onSuccess: () => onOutcome(`Desfeita: ${agentName} · ${formatDateTime(changeSet.createdAt)}.`) });
  };

  const onReview = () => {
    if (review.isPending) {
      return;
    }

    review.mutate(undefined, { onSuccess: () => onOutcome(`Marcada como revisada: ${agentName} · ${formatDateTime(changeSet.createdAt)}.`) });
  };

  return (
    <li aria-labelledby={titleId} className="space-y-2 rounded-md border p-3 text-sm">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <p className="flex flex-wrap items-baseline gap-2">
          <Link id={titleId} to={`/changesets/${changeSet.id}`} className="font-medium underline underline-offset-4">
            {agentName} · {formatDateTime(changeSet.createdAt)}
          </Link>
          {changeSet.reviewedAt === null && <span className="rounded border px-1.5 text-xs">Não revisada</span>}
          {undoState.state === "undone" && <span className="rounded border px-1.5 text-xs">Desfeita</span>}
        </p>
        <div className="flex flex-wrap gap-2">
          {changeSet.reviewedAt === null && (
            <Button type="button" variant="secondary" size="sm" onClick={onReview} disabled={review.isPending} aria-describedby={titleId}>
              Marcar como revisada
            </Button>
          )}
          {undoState.state === "available" && (
            <Button type="button" variant="secondary" size="sm" onClick={onUndo} disabled={undo.isPending} aria-describedby={titleId}>
              {undo.isPending ? "Desfazendo…" : "Desfazer"}
            </Button>
          )}
        </div>
      </div>
      {undoState.state === "unavailable" && undoState.reason && <p className="text-muted-foreground">{UNAVAILABLE_UNDO_LABELS[undoState.reason]}</p>}
      <ul className="space-y-1">
        {changeSet.entries.map((entry) => (
          <li key={String(entry.sequence)} className={entry.cascadeOf !== null ? "pl-4 text-muted-foreground" : undefined}>
            {entryDescription(entry)}
          </li>
        ))}
      </ul>
      {undo.isError && (
        <p role="alert" className="text-sm text-destructive">
          {refusal ? undoRefusalMessage(refusal, changeSet) : writeErrorMessage(undo.error)}
        </p>
      )}
      {review.isError && (
        <p role="alert" className="text-sm text-destructive">
          {writeErrorMessage(review.error)}
        </p>
      )}
    </li>
  );
}
