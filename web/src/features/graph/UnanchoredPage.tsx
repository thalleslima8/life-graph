import { useInfiniteQuery } from "@tanstack/react-query";
import { useEffect, useMemo, useRef, useState } from "react";
import { Link } from "react-router";
import { Button } from "@/components/ui/button";
import { unanchoredListOptions } from "./anchoringData";
import { ConnectToNode } from "./ConnectToNode";
import type { NodeSummary } from "./graphData";
import { LOAD_ERROR_MESSAGE, RETRY_LABEL } from "./graphMessages";

/** Where focus goes once a connected Node leaves the list: the row now in its place. */
type LeavingRow = { nodeId: string; index: number };

/**
 * Desancorados (DA-197): the Nodes no Hard path links to the Raiz da Conta, a system View that is
 * a query on the server (DA-206). A Node also in the Inbox says so. Each row connects in place;
 * once connected to the anchored graph, the row leaves on the next read and focus moves to the
 * row after it, or to the title when the list empties (FE-032).
 */
export function UnanchoredPage() {
  const nodes = useInfiniteQuery(unanchoredListOptions());
  const titleRef = useRef<HTMLHeadingElement>(null);
  const listRef = useRef<HTMLUListElement>(null);
  // The row that just connected; read once it has left the list, so no render waits on it.
  const leaving = useRef<LeavingRow>(undefined);
  const pages = nodes.data?.pages;
  const items = useMemo(() => pages?.flatMap((page) => page.data) ?? [], [pages]);

  useEffect(() => {
    const row = leaving.current;
    if (!row || !nodes.isSuccess || items.some((node) => node.id === row.nodeId)) {
      return;
    }

    leaving.current = undefined;
    const links = listRef.current?.querySelectorAll<HTMLElement>("[data-row-link]") ?? [];
    const next = links[Math.min(row.index, links.length - 1)];
    (next ?? titleRef.current)?.focus();
  }, [items, nodes.isSuccess]);

  return (
    <section aria-labelledby="unanchored-title" className="space-y-6">
      <div className="space-y-1">
        <h1 id="unanchored-title" ref={titleRef} tabIndex={-1} className="text-2xl font-semibold">
          Nós soltos
        </h1>
        <p className="text-sm text-muted-foreground">Nodes que nenhuma Relation liga à sua Raiz. Conecte cada um para encontrá-lo pelo grafo.</p>
      </div>

      {nodes.isPending && (
        <div role="status" className="space-y-2">
          <span className="sr-only">Carregando…</span>
          <div aria-hidden="true" className="h-10 animate-pulse rounded-md bg-muted" />
          <div aria-hidden="true" className="h-10 animate-pulse rounded-md bg-muted" />
        </div>
      )}
      {nodes.isError && (
        <div className="flex items-center gap-2">
          <p role="alert" className="text-sm text-destructive">
            {LOAD_ERROR_MESSAGE}
          </p>
          <Button type="button" variant="secondary" onClick={() => void nodes.refetch()}>
            {RETRY_LABEL}
          </Button>
        </div>
      )}
      {nodes.isSuccess &&
        (items.length === 0 ? (
          <p className="text-muted-foreground">Nenhum Node solto.</p>
        ) : (
          <ul ref={listRef} className="divide-y rounded-md border" aria-label="Nós soltos">
            {items.map((node, index) => (
              <UnanchoredRow key={node.id} node={node} onConnected={() => (leaving.current = { nodeId: node.id, index })} />
            ))}
          </ul>
        ))}
      {nodes.hasNextPage && (
        <Button type="button" variant="secondary" onClick={() => void nodes.fetchNextPage()} disabled={nodes.isFetchingNextPage}>
          {nodes.isFetchingNextPage ? <span role="status">Carregando…</span> : "Carregar mais"}
        </Button>
      )}
    </section>
  );
}

function UnanchoredRow({ node, onConnected }: { node: NodeSummary; onConnected: () => void }) {
  const [connecting, setConnecting] = useState(false);

  return (
    <li className="space-y-2 px-3 py-2">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="min-w-0 space-x-2">
          <Link to={`/nodes/${node.id}`} data-row-link className="font-medium underline-offset-4 hover:underline">
            {node.title}
          </Link>
          {node.inInbox && <span className="rounded border px-1.5 text-xs">Na Inbox</span>}
        </div>
        <Button
          type="button"
          variant="secondary"
          size="sm"
          aria-expanded={connecting}
          aria-label={`Conectar a… ${node.title}`}
          onClick={() => setConnecting((open) => !open)}
        >
          Conectar a…
        </Button>
      </div>
      {connecting && <ConnectToNode nodeId={node.id} isAccountRoot={node.isAccountRoot} onConnected={onConnected} />}
    </li>
  );
}
