import { useState } from "react";
import { Button } from "@/components/ui/button";
import { entryDescription, formatDateTime, LOAD_ERROR_MESSAGE, provenanceOf, undoRefusalMessage, writeErrorMessage } from "./graphMessages";
import { toUndoRefusal, undoChain, useRecentChanges, useUndoChangeSet, type ChangeSetItem } from "./recentChanges";

const UNLABELED_RELATION = "Relation sem rótulo";

/** Every GraphChangeSet with its Provenance, newest first, each one undoable (DA-020, DA-024). */
export function RecentChangesPage() {
  const { feed, hasNewer, showNewer } = useRecentChanges();
  const loaded = feed.data ?? [];

  return (
    <section aria-labelledby="changes-title" className="space-y-6">
      <h1 id="changes-title" className="text-2xl font-semibold">
        Recent Changes
      </h1>

      <div role="status" className="min-h-9">
        {hasNewer && (
          <div className="flex items-center gap-2 text-sm">
            <span>Há alterações novas.</span>
            <Button type="button" variant="secondary" size="sm" onClick={showNewer}>
              Mostrar
            </Button>
          </div>
        )}
      </div>

      {feed.isPending && <p role="status">Carregando…</p>}
      {feed.isError && (
        <p role="alert" className="text-sm text-destructive">
          {LOAD_ERROR_MESSAGE}
        </p>
      )}
      {feed.isSuccess &&
        (loaded.length === 0 ? (
          <p className="text-muted-foreground">Nenhuma alteração ainda.</p>
        ) : (
          <ol className="space-y-3" aria-label="Alterações">
            {loaded.map((changeSet) => (
              <ChangeSetCard key={changeSet.id} changeSet={changeSet} loaded={loaded} />
            ))}
          </ol>
        ))}
      {feed.hasNextPage && (
        <Button type="button" variant="secondary" onClick={() => void feed.fetchNextPage()} disabled={feed.isFetchingNextPage}>
          {feed.isFetchingNextPage ? "Carregando…" : "Carregar mais antigas"}
        </Button>
      )}
    </section>
  );
}

function ChangeSetCard({ changeSet, loaded }: { changeSet: ChangeSetItem; loaded: ChangeSetItem[] }) {
  const undo = useUndoChangeSet();
  const [outcome, setOutcome] = useState<string>();
  const chain = undoChain(changeSet, loaded);
  const titleId = `changeset-${changeSet.id}`;
  const refusal = undo.isError ? toUndoRefusal(undo.error) : null;

  const onUndo = () => {
    if (undo.isPending) {
      return;
    }

    setOutcome(undefined);
    undo.mutate(changeSet, {
      onSuccess: ({ relationsLeftDeleted }) =>
        setOutcome(
          relationsLeftDeleted.length > 0
            ? `Desfeito. Não voltaram: ${relationsLeftDeleted.map((relation) => relation.label ?? UNLABELED_RELATION).join(", ")}.`
            : "Desfeito.",
        ),
    });
  };

  return (
    <li id={titleId} aria-labelledby={`${titleId}-when`} className="space-y-2 rounded-md border p-3">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <p className="text-sm">
          <span id={`${titleId}-when`} className="font-medium">
            {formatDateTime(changeSet.createdAt)}
          </span>
          <span className="text-muted-foreground"> · {provenanceOf(changeSet)}</span>
        </p>
        {chain.isReverted ? (
          <span className="rounded border px-1.5 text-xs">Desfeita</span>
        ) : (
          <Button type="button" variant="secondary" size="sm" onClick={onUndo} disabled={undo.isPending} aria-describedby={`${titleId}-when`}>
            {undo.isPending ? "Desfazendo…" : "Desfazer"}
          </Button>
        )}
      </div>

      <ul className="space-y-1 text-sm">
        {changeSet.entries.map((entry) => (
          <li key={String(entry.sequence)} className={entry.cascadeOf !== null ? "pl-4 text-muted-foreground" : undefined}>
            {entryDescription(entry)}
            {entry.cascadeOf !== null && " (em cascata)"}
          </li>
        ))}
      </ul>

      {(chain.reverts || chain.revertedBy) && (
        <p className="text-sm text-muted-foreground">
          {chain.reverts && (
            <a href={`#changeset-${chain.reverts.id}`} className="underline underline-offset-4">
              Desfaz a alteração de {formatDateTime(chain.reverts.createdAt)}
            </a>
          )}
          {chain.revertedBy && (
            <a href={`#changeset-${chain.revertedBy.id}`} className="underline underline-offset-4">
              Desfeita em {formatDateTime(chain.revertedBy.createdAt)}
            </a>
          )}
        </p>
      )}

      {outcome && (
        <p role="status" className="text-sm">
          {outcome}
        </p>
      )}
      {undo.isError && (
        <p role="alert" className="text-sm text-destructive">
          {refusal ? undoRefusalMessage(refusal, changeSet) : writeErrorMessage(undo.error)}
        </p>
      )}
    </li>
  );
}
