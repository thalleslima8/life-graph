import { useQuery } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router";
import { isApiError, type ApiError } from "@/api/client";
import { Button } from "@/components/ui/button";
import { formatDateTime, LOAD_ERROR_MESSAGE, writeErrorMessage } from "@/features/graph/graphMessages";
import { proposalQuery, toApproval, toProposalRefusal, useApproveProposal, useRejectProposal, type ProposalDetail } from "./agentChanges";
import { agentLabel, PROPOSAL_STATUS_LABELS, reasonLabels, refusalMessage } from "./agentChangeMessages";
import { ChangeDiff } from "./ChangeDiff";

const GONE_MESSAGE = "Esta proposta não está mais disponível.";

/**
 * One proposal of an agent (DA-135): what it would change, field by field, approved in whole
 * or in part, or rejected. Approving applies it as the agent's change and is final for the
 * proposal; the person can still undo the GraphChangeSet it becomes.
 */
export function ProposalPage() {
  const { proposalId = "" } = useParams();
  const proposal = useQuery(proposalQuery(proposalId));
  const titleRef = useRef<HTMLHeadingElement>(null);

  return (
    <section aria-labelledby="proposal-title" className="space-y-6">
      <p className="text-sm">
        <Link to="/agent-changes" className="underline underline-offset-4">
          Voltar para Agent Changes
        </Link>
      </p>
      <h1 id="proposal-title" ref={titleRef} tabIndex={-1} className="text-2xl font-semibold">
        Proposta de agente
      </h1>
      {proposal.isPending && <p role="status">Carregando…</p>}
      {proposal.isError && (
        <p role="alert" className="text-sm text-destructive">
          {isApiError(proposal.error) && proposal.error.status === 404 ? GONE_MESSAGE : LOAD_ERROR_MESSAGE}
        </p>
      )}
      {proposal.isSuccess && (
        <ProposalView key={proposal.data.proposal.id} detail={proposal.data} onGone={() => titleRef.current?.focus()} />
      )}
    </section>
  );
}

/** `onGone`: the proposal no longer exists, and this view is about to go with it. */
function ProposalView({ detail, onGone }: { detail: ProposalDetail; onGone: () => void }) {
  const { proposal, agent, operations } = detail;
  const approve = useApproveProposal(proposal.id);
  const reject = useRejectProposal(proposal.id);
  const [selected, setSelected] = useState<Set<number>>(() => new Set(operations.map((operation) => Number(operation.index))));
  const [decision, setDecision] = useState<string>();
  const decisionRef = useRef<HTMLParagraphElement>(null);
  const isPending = proposal.status === "proposed";
  const isBusy = approve.isPending || reject.isPending;
  const refusal = toProposalRefusal(approve.error);
  const approval = toApproval(selected, operations.length);

  // After a decision the buttons go away: focus moves to what happened (FE-032).
  useEffect(() => {
    if (decision) {
      decisionRef.current?.focus();
    }
  }, [decision]);

  // A refusal that ends the decision takes the buttons away too (the proposal is read again):
  // focus moves to what happened, or, when the proposal is gone with this view, to the page
  // title (FE-032). Any other refusal leaves the buttons, and the focus, where they are.
  const onRefused = (error: ApiError) => {
    const ending = toProposalRefusal(error);
    if (ending?.kind === "not_pending") {
      setDecision(refusalMessage(ending));
    } else if (ending?.kind === "not_found") {
      onGone();
    }
  };

  const onApprove = () => {
    if (approval === null || isBusy) {
      return;
    }

    const onSuccess = () => setDecision(approval ? "Aprovada em parte. As operações desmarcadas foram recusadas." : "Aprovada e aplicada.");
    if (approval === undefined) {
      approve.mutate(undefined, { onSuccess, onError: onRefused });
    } else {
      approve.mutate({ operations: approval }, { onSuccess, onError: onRefused });
    }
  };

  const onReject = () => {
    if (isBusy) {
      return;
    }

    reject.mutate(undefined, { onSuccess: () => setDecision("Proposta recusada. Nada foi aplicado."), onError: onRefused });
  };

  const otherApproveError = approve.isError && refusal === null ? writeErrorMessage(approve.error) : null;
  // A refusal of the rejection is told where the focus goes (onRefused); this is any other failure.
  const otherRejectError = reject.isError && toProposalRefusal(reject.error) === null ? writeErrorMessage(reject.error) : null;

  return (
    <div className="space-y-6">
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        <dt className="text-muted-foreground">Agente</dt>
        <dd>{agentLabel(agent)}</dd>
        <dt className="text-muted-foreground">Situação</dt>
        <dd>{PROPOSAL_STATUS_LABELS[proposal.status]}</dd>
        <dt className="text-muted-foreground">Por quê</dt>
        <dd>{reasonLabels(proposal.reasons)}</dd>
        <dt className="text-muted-foreground">Criada em</dt>
        <dd>{formatDateTime(proposal.createdAt)}</dd>
        {isPending && (
          <>
            <dt className="text-muted-foreground">Expira em</dt>
            <dd>{formatDateTime(proposal.expiresAt)}</dd>
          </>
        )}
        {proposal.source && (
          <>
            <dt className="text-muted-foreground">Fonte</dt>
            <dd className="break-words">{proposal.source}</dd>
          </>
        )}
      </dl>

      {proposal.changeSetId && (
        <p className="text-sm">
          <Link to={`/changesets/${proposal.changeSetId}`} className="underline underline-offset-4">
            Ver a alteração aplicada
          </Link>
        </p>
      )}

      <h2 className="text-xl font-semibold">O que muda</h2>
      <ChangeDiff
        source={{ kind: "proposal", detail }}
        selection={isPending && !detail.contentPurged ? { selected, onChange: setSelected, disabled: isBusy } : undefined}
        highlight={refusal !== null && "operations" in refusal ? refusal.operations : undefined}
      />

      {isPending && (
        <div className="space-y-2">
          <p className="text-sm text-muted-foreground">
            {selected.size} de {operations.length} operações marcadas. As desmarcadas são recusadas.
          </p>
          <div className="flex flex-wrap gap-2">
            <Button type="button" onClick={onApprove} disabled={approval === null || isBusy || detail.contentPurged}>
              {approve.isPending ? "Aprovando…" : "Aprovar"}
            </Button>
            <Button type="button" variant="secondary" onClick={onReject} disabled={isBusy}>
              {reject.isPending ? "Recusando…" : "Recusar tudo"}
            </Button>
          </div>
        </div>
      )}

      <p ref={decisionRef} tabIndex={-1} role="status" aria-live="polite" className="text-sm">
        {decision ?? (refusal && refusalMessage(refusal))}
      </p>
      {otherApproveError && (
        <p role="alert" className="text-sm text-destructive">
          {otherApproveError}
        </p>
      )}
      {otherRejectError && (
        <p role="alert" className="text-sm text-destructive">
          {otherRejectError}
        </p>
      )}
    </div>
  );
}
