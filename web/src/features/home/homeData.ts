import { queryOptions, useQuery } from "@tanstack/react-query";
import { useMemo } from "react";
import { api, unwrap } from "@/api/client";
import { accountRootQuery, shownUnanchoredCount, unanchoredCountOptions } from "@/features/graph/anchoringData";
import { graphKeys } from "@/features/graph/graphData";
import { localGraphQuery, type GraphDepth, type LocalGraph } from "@/features/graph/localGraph";
import { HOME_ROOT_LABEL } from "./homeMessages";

/**
 * The Home's reads (DA-270, DA-274): the Raiz da Conta, its local graph one level deep, and a
 * two-item page of Nodes that tells an empty Account from one whose Nodes the root does not reach
 * (DA-312). Each is a GET through the central read filter; none of them calls a model.
 */

/** One level around the root: the server keeps at most 20 neighbors and reports the rest (DA-272). */
export const HOME_GRAPH_DEPTH: GraphDepth = 1;

/** Where the Home's entry panel and empty state lead (DA-271, DA-313). */
export const HOME_PATHS = {
  inbox: "/nodes?inbox=1",
  nodes: "/nodes",
  recentChanges: "/changes",
  agentChanges: "/agent-changes",
  agents: "/agentes",
} as const;

/**
 * The Desancorados count as a counter shows it, or `undefined` while it loads or after it fails:
 * the link it labels never waits for it (DA-314). The server leaves the root out of the count.
 */
export function useShownUnanchoredCount(): string | undefined {
  const count = useQuery(unanchoredCountOptions());
  return count.isSuccess ? shownUnanchoredCount(Number(count.data)) : undefined;
}

/** What the Home shows; only `graph` carries a graph, so no state reads data it does not have (DA-318). */
export type HomeGraph =
  | { state: "loading" }
  | { state: "error"; retry: () => void }
  | { state: "empty" }
  | { state: "unlinked" }
  | { state: "graph"; rootId: string; rootTitle: string; graph: LocalGraph };

/**
 * Whether the Account has a Node besides the Raiz da Conta: two items are enough, since at most
 * one of them is the root (DA-312). A graph read, so every write of the SPA refreshes it.
 */
export const hasNodeBesidesRootOptions = () =>
  queryOptions({
    queryKey: [...graphKeys.all, "home", "has-node-besides-root"] as const,
    queryFn: async ({ signal }) => unwrap(await api.GET("/api/nodes", { params: { query: { limit: 2 } }, signal })),
    select: (page) => page.data.some((node) => !node.isAccountRoot),
  });

/** The local graph with the Raiz da Conta named "Seu grafo" (DA-272). */
function withRootLabelled(graph: LocalGraph): LocalGraph {
  return {
    ...graph,
    nodes: graph.nodes.map((node) => (node.id === graph.rootNodeId ? { ...node, title: HOME_ROOT_LABEL } : node)),
  };
}

/**
 * The Home's state. A graph with a Node besides the root is shown as soon as it arrives, and the
 * Nodes page is never read for it; a root alone reads that page and waits for it before saying
 * empty or unlinked, and a failure of any read the
 * state depends on is an error, never a false empty (DA-312). A missing root is an error too (DA-318).
 */
export function useHomeGraph(): HomeGraph {
  const root = useQuery(accountRootQuery);
  const rootId = root.data?.id;
  const graph = useQuery({ ...localGraphQuery(rootId ?? "", HOME_GRAPH_DEPTH), enabled: rootId !== undefined });
  const labelled = useMemo(() => (graph.data ? withRootLabelled(graph.data) : undefined), [graph.data]);
  // Only a root alone needs the probe: a graph with a neighbor already says the Account is not empty.
  const nodesProbe = useQuery({ ...hasNodeBesidesRootOptions(), enabled: labelled?.nodes.length === 1 });

  const retry = () => {
    if (root.isError) {
      void root.refetch();
    }

    if (graph.isError) {
      void graph.refetch();
    }

    if (nodesProbe.isError) {
      void nodesProbe.refetch();
    }
  };

  if (root.isError || graph.isError) {
    return { state: "error", retry };
  }

  if (rootId === undefined || labelled === undefined) {
    return { state: "loading" };
  }

  if (labelled.nodes.length > 1) {
    return { state: "graph", rootId, rootTitle: HOME_ROOT_LABEL, graph: labelled };
  }

  if (nodesProbe.isError) {
    return { state: "error", retry };
  }

  if (nodesProbe.isPending) {
    return { state: "loading" };
  }

  return nodesProbe.data ? { state: "unlinked" } : { state: "empty" };
}
