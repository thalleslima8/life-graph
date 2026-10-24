import { toSchemaRefusal } from "./graphData";
import { schemaRefusalMessage, writeErrorMessage } from "./graphMessages";

/** Why a change to the ontology failed: a schema refusal explained, any other error as a write error. */
export function RefusalOrError({ error }: { error: unknown }) {
  if (error === null || error === undefined) {
    return null;
  }

  const refusal = toSchemaRefusal(error);
  return (
    <p role="alert" className="text-sm text-destructive">
      {refusal ? schemaRefusalMessage(refusal) : writeErrorMessage(error)}
    </p>
  );
}

/** System entries keep their name and place (DA-057): the mark stands where the edit controls would be. */
export function SystemMark() {
  return <span className="rounded border px-1.5 text-xs font-normal">Sistema, travado</span>;
}
