import { useId } from "react";
import { entryDescription } from "@/features/graph/graphMessages";
import type { ChangeEntryDetail, ChangeSetDetail, FieldChange, ProposalDetail, ProposedOperation } from "./agentChanges";
import { fieldLabel, fieldValue, operationTitle } from "./agentChangeMessages";

export type ChangeDiffSource = { kind: "proposal"; detail: ProposalDetail } | { kind: "changeset"; detail: ChangeSetDetail };

export type ChangeDiffSelection = {
  selected: ReadonlySet<number>;
  onChange(next: Set<number>): void;
  disabled?: boolean;
};

export type ChangeDiffProps = {
  source: ChangeDiffSource;
  /** Only for a pending proposal: which operations the person approves. */
  selection?: ChangeDiffSelection;
  /** Operations refused by the server (a conflict, a missing dependency), announced by text. */
  highlight?: number[];
};

export const CONTENT_PURGED_TEXT = "conteúdo removido por purge";
export const OVERWRITES_TEXT = "sobrescreveu um valor seu";

/**
 * A proposal or a GraphChangeSet field by field (DA-135). Presentational: it never fetches,
 * and the page owns the actions, the focus and the live region. It owns the accessibility
 * of the diff: a section and a title per operation, Antes/Depois in a description list, a
 * labelled checkbox, and the "sobrescreveu um valor seu" warning as icon and text, never
 * colour alone.
 */
export function ChangeDiff({ source, selection, highlight = [] }: ChangeDiffProps) {
  if (source.kind === "proposal") {
    if (source.detail.contentPurged) {
      return <p className="rounded-md border p-3 text-sm">Esta proposta não tem mais o que mostrar: {CONTENT_PURGED_TEXT}.</p>;
    }

    const operations = source.detail.operations;
    return (
      <ol className="space-y-3" aria-label="Operações">
        {operations.map((operation) => (
          <ProposedOperationView
            key={String(operation.index)}
            operation={operation}
            operations={operations}
            selection={selection}
            isHighlighted={highlight.includes(Number(operation.index))}
          />
        ))}
      </ol>
    );
  }

  return (
    <ol className="space-y-3" aria-label="Itens da alteração">
      {source.detail.entries.map((entry) => (
        <EntryView key={String(entry.sequence)} entry={entry} />
      ))}
    </ol>
  );
}

// Ticking Y ticks what it depends on; unticking X unticks what depends on it: the server
// refuses a relation kept without its new Node.
function withTicked(operations: ProposedOperation[], selected: ReadonlySet<number>, index: number): Set<number> {
  const next = new Set(selected);
  next.add(index);
  for (const dependency of operations[index]?.dependsOn ?? []) {
    next.add(Number(dependency));
  }

  return next;
}

function withUnticked(operations: ProposedOperation[], selected: ReadonlySet<number>, index: number): Set<number> {
  const next = new Set(selected);
  next.delete(index);
  for (const operation of operations) {
    if (operation.dependsOn.map(Number).includes(index)) {
      next.delete(Number(operation.index));
    }
  }

  return next;
}

function ProposedOperationView({
  operation,
  operations,
  selection,
  isHighlighted,
}: {
  operation: ProposedOperation;
  operations: ProposedOperation[];
  selection?: ChangeDiffSelection;
  isHighlighted: boolean;
}) {
  const id = useId();
  const index = Number(operation.index);
  const title = operationTitle(operation);
  const hasChangedSinceRead =
    operation.expectedVersion !== null && operation.currentVersion !== null && Number(operation.expectedVersion) !== Number(operation.currentVersion);
  const isNodeGone = (operation.op === "update_node" || operation.op === "delete_node") && operation.currentVersion === null;

  return (
    <li>
      <section aria-labelledby={`${id}-title`} className={`space-y-2 rounded-md border p-3 ${isHighlighted ? "border-destructive border-2" : ""}`}>
        <div className="flex flex-wrap items-start gap-2">
          {selection && (
            <input
              id={`${id}-include`}
              type="checkbox"
              className="mt-1 size-4"
              checked={selection.selected.has(index)}
              disabled={selection.disabled}
              onChange={(event) =>
                selection.onChange(
                  event.target.checked ? withTicked(operations, selection.selected, index) : withUnticked(operations, selection.selected, index),
                )
              }
            />
          )}
          <div className="space-y-1">
            <h3 id={`${id}-title`} className="font-medium break-words">
              {selection ? (
                <label htmlFor={`${id}-include`}>
                  Operação {index + 1}: {title}
                </label>
              ) : (
                <>
                  Operação {index + 1}: {title}
                </>
              )}
            </h3>
            <ul className="flex flex-wrap gap-2 text-xs">
              {operation.isSensitive && <li className="rounded border px-1.5">Sensível: só você aprova</li>}
              {operation.isRejected && <li className="rounded border px-1.5">Recusada</li>}
              {operation.dependsOn.length > 0 && (
                <li className="rounded border px-1.5">Depende da operação {operation.dependsOn.map((dependency) => Number(dependency) + 1).join(", ")}</li>
              )}
              {hasChangedSinceRead && <li className="rounded border px-1.5">Mudou desde que o agente leu</li>}
              {isNodeGone && <li className="rounded border px-1.5">O Node não está mais disponível</li>}
            </ul>
          </div>
        </div>
        {isHighlighted && (
          <p className="text-sm font-medium text-destructive">Esta operação impediu a aprovação.</p>
        )}
        {operation.suggestedType && <p className="text-sm text-muted-foreground">Type sugerido pelo agente: {operation.suggestedType}</p>}
        <Fields fields={operation.fields} />
      </section>
    </li>
  );
}

function EntryView({ entry }: { entry: ChangeEntryDetail }) {
  const id = useId();
  return (
    <li className={entry.cascadeOf !== null ? "pl-4" : undefined}>
      <section aria-labelledby={`${id}-title`} className="space-y-2 rounded-md border p-3">
        <h3 id={`${id}-title`} className="font-medium break-words">
          {entryDescription(entry)}
          {entry.cascadeOf !== null && " (em cascata)"}
        </h3>
        {entry.isPurged ? <p className="text-sm text-muted-foreground">{CONTENT_PURGED_TEXT}</p> : <Fields fields={entry.fields} />}
      </section>
    </li>
  );
}

function Fields({ fields }: { fields: FieldChange[] }) {
  if (fields.length === 0) {
    return null;
  }

  return (
    <ul className="space-y-2">
      {fields.map((field) => (
        <li key={field.field} className="space-y-1 rounded bg-muted/40 p-2 text-sm">
          <h4 className="font-medium">{fieldLabel(field.field)}</h4>
          {field.overwritesYourValue && (
            <p className="flex items-center gap-1 font-medium">
              <WarningIcon />
              <span>Atenção: {OVERWRITES_TEXT}</span>
            </p>
          )}
          <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
            <dt className="text-muted-foreground">Antes</dt>
            <dd className="break-words whitespace-pre-wrap">{fieldValue(field.before)}</dd>
            <dt className="text-muted-foreground">Depois</dt>
            <dd className="break-words whitespace-pre-wrap">{fieldValue(field.after)}</dd>
          </dl>
        </li>
      ))}
    </ul>
  );
}

function WarningIcon() {
  return (
    <svg aria-hidden="true" viewBox="0 0 16 16" className="size-4 shrink-0" fill="currentColor">
      <path d="M8 1 15 14H1L8 1Zm0 4a.75.75 0 0 0-.75.75v3.5a.75.75 0 0 0 1.5 0v-3.5A.75.75 0 0 0 8 5Zm0 7.25a.9.9 0 1 0 0-1.8.9.9 0 0 0 0 1.8Z" />
    </svg>
  );
}
