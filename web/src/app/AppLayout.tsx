import { useEffect } from "react";
import { Link, Outlet, useLocation } from "react-router";
import { Button } from "@/components/ui/button";
import { CollectionNavLinks } from "@/features/collections/CollectionNavLinks";
import { SearchBox } from "@/features/search/SearchBox";
import { useSession } from "@/features/session/session";
import { useLogout } from "@/features/session/useLogout";

const GRAPH_LINKS = [
  { to: "/nodes", label: "Nodes" },
  { to: "/nodes?inbox=1", label: "Inbox" },
  { to: "/graph", label: "Grafo" },
  { to: "/changes", label: "Recent Changes" },
  { to: "/agent-changes", label: "Agent Changes" },
  { to: "/types", label: "Types" },
  { to: "/tags", label: "Tags" },
  { to: "/agentes", label: "Agentes conectados" },
  { to: "/shares", label: "Links compartilhados" },
];

export function AppLayout() {
  const session = useSession();
  const logout = useLogout();
  const { pathname } = useLocation();

  // A route change moves focus to the new page's title, as a page load would (FE-032).
  useEffect(() => {
    const title = document.querySelector<HTMLElement>("main h1");
    if (title) {
      title.tabIndex = -1;
      title.focus();
    }
  }, [pathname]);

  return (
    <div className="min-h-screen">
      <header className="border-b">
        <nav aria-label="Principal" className="mx-auto flex max-w-5xl items-center justify-between gap-4 px-4 py-3">
          <div className="flex flex-wrap items-center gap-4">
            <Link to="/" className="font-semibold">
              Life Graph
            </Link>
            {session && (
              <ul className="flex flex-wrap gap-3 text-sm">
                {GRAPH_LINKS.map(({ to, label }) => (
                  <li key={to}>
                    <Link to={to} className="underline-offset-4 hover:underline">
                      {label}
                    </Link>
                  </li>
                ))}
                <CollectionNavLinks />
              </ul>
            )}
          </div>
          {session && (
            <div className="flex flex-wrap items-center gap-3 text-sm">
              <SearchBox />
              <span className="text-muted-foreground">{session.email}</span>
              <Link to="/conta" className="underline-offset-4 hover:underline">
                Conta
              </Link>
              <Button type="button" variant="secondary" size="sm" onClick={() => logout.mutate()} disabled={logout.isPending}>
                Sair
              </Button>
            </div>
          )}
        </nav>
      </header>
      <main className="mx-auto max-w-5xl px-4 py-8">
        <Outlet />
      </main>
    </div>
  );
}
