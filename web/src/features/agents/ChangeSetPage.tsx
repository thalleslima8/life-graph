import { useQuery } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router";
import { isApiError } from "@/api/client";
import { Button } from "@/components/ui/button";
import { formatDateTime, LOAD_ERROR_MESSAGE, provenanceOf, undoRefusalMessage, writeErrorMessage } from "@/features/graph/graphMessages";
import { toUndoRefusal, useUndoChangeSet } from "@/features/graph/recentChanges";
import { changeSetQuery, useMarkReviewed, type ChangeSetDetail } from "./agentChanges";
import { agentLabel, UNAVAILABLE_UNDO_LABELS } from "./agentChangeMessages";
import { ChangeDiff } from "./ChangeDiff";

const GONE_MESSAGE = "Esta alteração não está mais disponível.";

/**
 * The GraphChangeSet screen (v2 §66, DA-135): who changed what, by which channel and from
 * which source, each entry field by field with the "sobrescreveu um valor seu" warning, and
 * Undo while it is possible. An agent's change can be marked as reviewed here too.
 */
export function ChangeSetPage() {
  const { changeSetId = "" } = useParams();
  const changeSet = useQuery(changeSetQuery(changeSetId));

  return (
    <section aria-labelledby="changeset-title" className="space-y-6">
      <p className="text-sm">
        <Link to="/changes" className="underline underline-offset-4">
          Voltar para Recent Changes
        </Link>
      </p>
      <h1 id="changeset-title" className="text-2xl font-semibold">
        Alteração
      </h1>
      {changeSet.isPending && <p role="status">Carregando…</p>}
      {changeSet.isError && (
        <p role="alert" className="text-sm text-destructive">
          {isApiError(changeSet.error) && changeSet.error.status === 404 ? GONE_MESSAGE : LOAD_ERROR_MESSAGE}
        </p>
      )}
      {changeSet.isSuccess && <ChangeSetView detail={changeSet.data} />}
    </section>
  );
}

function ChangeSetView({ detail }: { detail: ChangeSetDetail }) {
  const { changeSet, agent, undo: undoState } = detail;
  const undo = useUndoChangeSet();
  const review = useMarkReviewed(changeSet.id);
  // A new object per outcome, so the same message twice still moves focus.
  const [outcome, setOutcome] = useState<{ text: string }>();
  const outcomeRef = useRef<HTMLParagraphElement>(null);
  const refusal = undo.isError ? toUndoRefusal(undo.error) : null;
  const isByAgent = changeSet.actorKind === "agent_identity";

  // After Undo or the review the button goes away: focus moves to what happened (FE-032).
  useEffect(() => {
    if (outcome) {
      outcomeRef.current?.focus();
    }
  }, [outcome]);

  const onUndo = () => {
    if (undo.isPending) {
      return;
    }

    setOutcome(undefined);
    undo.mutate(changeSet, { onSuccess: () => setOutcome({ text: "Desfeito." }) });
  };

  const onReview = () => {
    if (review.isPending) {
      return;
    }

    setOutcome(undefined);
    review.mutate(undefined, { onSuccess: () => setOutcome({ text: "Marcada como revisada." }) });
  };

  return (
    <div className="space-y-6">
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        <dt className="text-muted-foreground">Quando</dt>
        <dd>{formatDateTime(changeSet.createdAt)}</dd>
        <dt className="text-muted-foreground">Quem</dt>
        <dd>{isByAgent ? `Agente: ${agentLabel(agent)}` : "Você"}</dd>
        <dt className="text-muted-foreground">Provenance</dt>
        <dd className="break-words">{provenanceOf(changeSet)}</dd>
        {isByAgent && (
          <>
            <dt className="text-muted-foreground">Revisão</dt>
            <dd>{changeSet.reviewedAt ? `Revisada em ${formatDateTime(changeSet.reviewedAt)}` : "Não revisada"}</dd>
          </>
        )}
      </dl>

      {(changeSet.proposalId || changeSet.revertsChangeSetId || undoState.byChangeSetId) && (
        <ul className="space-y-1 text-sm">
          {changeSet.proposalId && (
            <li>
              <Link to={`/agent-changes/proposals/${changeSet.proposalId}`} className="underline underline-offset-4">
                Ver a proposta que originou esta alteração
              </Link>
            </li>
          )}
          {changeSet.revertsChangeSetId && (
            <li>
              <Link to={`/changesets/${changeSet.revertsChangeSetId}`} className="underline underline-offset-4">
                Ver a alteração que esta desfaz
              </Link>
            </li>
          )}
          {undoState.byChangeSetId && (
            <li>
              <Link to={`/changesets/${undoState.byChangeSetId}`} className="underline underline-offset-4">
                Ver o Undo desta alteração
              </Link>
            </li>
          )}
        </ul>
      )}

      <div className="flex flex-wrap items-center gap-2">
        {undoState.state === "available" && (
          <Button type="button" variant="secondary" onClick={onUndo} disabled={undo.isPending}>
            {undo.isPending ? "Desfazendo…" : "Desfazer"}
          </Button>
        )}
        {undoState.state === "undone" && <span className="rounded border px-1.5 text-sm">Desfeita</span>}
        {undoState.state === "unavailable" && undoState.reason && <p className="text-sm text-muted-foreground">{UNAVAILABLE_UNDO_LABELS[undoState.reason]}</p>}
        {isByAgent && changeSet.reviewedAt === null && (
          <Button type="button" variant="secondary" onClick={onReview} disabled={review.isPending}>
            Marcar como revisada
          </Button>
        )}
      </div>

      <p ref={outcomeRef} tabIndex={-1} role="status" aria-live="polite" className="text-sm">
        {outcome?.text}
      </p>
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

      <h2 className="text-xl font-semibold">O que muda</h2>
      <ChangeDiff source={{ kind: "changeset", detail }} />
    </div>
  );
}
