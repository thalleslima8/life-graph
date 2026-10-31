import { useQuery } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Link, Navigate, useSearchParams } from "react-router";
import { Button } from "@/components/ui/button";
import { UNANCHORED_PATH, useAccountRoot } from "./anchoringData";
import { isNotFound, nodeQuery } from "./graphData";
import { LOAD_ERROR_MESSAGE, RETRY_LABEL } from "./graphMessages";
import { graphPath } from "./graphView";
import { forgetLastFocus, readLastFocus } from "./localGraph";

export const GRAPH_ENTRY_LOADING_MESSAGE = "Carregando o grafo…";
export const ROOT_NOT_FOUND_TITLE = "Sua Raiz não foi encontrada";

/**
 * The way into the local graph with no Node in the URL (DA-197, DA-208): `?focus=` first, then the
 * last focus of this browser, then the Raiz da Conta. A named focus opens without waiting for the
 * root; a focus that answers NotFound opens the root instead, and a last focus that does is also
 * forgotten.
 */
export function GraphEntryPage() {
  const [params] = useSearchParams();
  const requested = params.get("focus") ?? undefined;
  const [lastFocus] = useState(readLastFocus);

  if (requested) {
    return <FocusEntry key={requested} nodeId={requested} remembered={false} />;
  }

  return lastFocus ? <FocusEntry nodeId={lastFocus} remembered /> : <RootEntry />;
}

function FocusEntry({ nodeId, remembered }: { nodeId: string; remembered: boolean }) {
  const node = useQuery(nodeQuery(nodeId));
  const gone = node.isError && isNotFound(node.error);

  useEffect(() => {
    if (gone && remembered) {
      forgetLastFocus();
    }
  }, [gone, remembered]);

  if (gone) {
    return <RootEntry />;
  }

  // Any other failure is the graph page's to show, with its own retry.
  if (node.isSuccess || node.isError) {
    return <Navigate to={graphPath(nodeId)} replace />;
  }

  return <EntryLoading />;
}

function RootEntry() {
  const root = useAccountRoot();

  if (root.rootId) {
    return <Navigate to={graphPath(root.rootId)} replace />;
  }

  if (root.isRootMissing) {
    return (
      <section aria-labelledby="graph-entry-title" className="space-y-2">
        <h1 id="graph-entry-title" className="text-2xl font-semibold">
          {ROOT_NOT_FOUND_TITLE}
        </h1>
        <p className="text-muted-foreground">Ela é criada na próxima vez que você entrar. Enquanto isso, seus Nodes continuam acessíveis.</p>
        <Link to={UNANCHORED_PATH} className="underline underline-offset-4">
          Ver os Nós soltos
        </Link>
      </section>
    );
  }

  if (root.isError) {
    return (
      <div className="flex items-center gap-2">
        <p role="alert" className="text-sm text-destructive">
          {LOAD_ERROR_MESSAGE}
        </p>
        <Button type="button" variant="secondary" onClick={() => void root.refetch()}>
          {RETRY_LABEL}
        </Button>
      </div>
    );
  }

  return <EntryLoading />;
}

function EntryLoading() {
  return (
    <p role="status" className="text-muted-foreground">
      {GRAPH_ENTRY_LOADING_MESSAGE}
    </p>
  );
}
