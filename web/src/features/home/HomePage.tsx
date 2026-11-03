import { lazy, Suspense } from "react";
import { Button } from "@/components/ui/button";
import { LOAD_ERROR_MESSAGE, LOADING_MESSAGE, RETRY_LABEL } from "@/features/graph/graphMessages";
import { HomeEmptyState } from "./HomeEmptyState";
import { HomeEntryPanel } from "./HomeEntryPanel";
import { useHomeGraph, type HomeGraph } from "./homeData";
import { HOME_TITLE } from "./homeMessages";

// The drawing library loads with the first graph to draw, as the graph page's does: an empty
// Account and the first paint of the Home do not pay for it.
const HomeGraphView = lazy(async () => ({ default: (await import("./HomeGraphView")).HomeGraphView }));

/**
 * The Home: the local graph of the Raiz da Conta, one level deep, as the entry to the graph
 * (DA-270, DA-272, DA-315), beside the entry panel, which stays whatever the graph's state
 * (DA-271, DA-318). Opening it only reads; it never becomes the graph page's last focus.
 */
export function HomePage() {
  const home = useHomeGraph();
  return (
    <section aria-labelledby="home-title" className="space-y-4">
      <h1 id="home-title" className="text-2xl font-semibold">
        {HOME_TITLE}
      </h1>
      <div className="grid gap-6 lg:grid-cols-4">
        <div aria-busy={home.state === "loading"} className="min-w-0 space-y-4 lg:col-span-3">
          <HomeContent home={home} />
        </div>
        <HomeEntryPanel />
      </div>
    </section>
  );
}

function HomeContent({ home }: { home: HomeGraph }) {
  switch (home.state) {
    case "loading":
      return <HomeLoading />;
    case "error":
      return (
        <>
          <p role="alert" className="text-sm text-destructive">
            {LOAD_ERROR_MESSAGE}
          </p>
          <Button type="button" variant="secondary" onClick={home.retry}>
            {RETRY_LABEL}
          </Button>
        </>
      );
    case "empty":
    case "unlinked":
      return <HomeEmptyState variant={home.state} />;
    case "graph":
      return (
        <Suspense fallback={<HomeLoading />}>
          <HomeGraphView rootId={home.rootId} rootTitle={home.rootTitle} graph={home.graph} />
        </Suspense>
      );
  }
}

function HomeLoading() {
  return (
    <>
      <p role="status">{LOADING_MESSAGE}</p>
      <div aria-hidden="true" className="h-[60vh] min-h-80 w-full animate-pulse rounded-md bg-muted" />
    </>
  );
}
