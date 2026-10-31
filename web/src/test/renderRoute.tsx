import { QueryClientProvider } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import { createMemoryRouter, RouterProvider, type InitialEntry } from "react-router";
import { setUnauthorizedHandler } from "@/api/client";
import { createQueryClient } from "@/app/queryClient";
import { routes } from "@/app/routes";
import { createRouterContext } from "@/features/session/queryClientContext";
import { createUnauthorizedHandler } from "@/features/session/unauthorizedHandler";

/** Renders the real route tree at `path` (a URL, or a location with router state), wired like main.tsx: one QueryClient for router and provider. */
export function renderRoute(path: InitialEntry) {
  const queryClient = createQueryClient();
  // Tests fail fast instead of retrying; a query's or a mutation's own retry policy still runs, with no real wait (GEN-072).
  const defaults = queryClient.getDefaultOptions();
  queryClient.setDefaultOptions({
    queries: { ...defaults.queries, retry: false, retryDelay: 0 },
    mutations: { ...defaults.mutations, retryDelay: 0 },
  });
  const router = createMemoryRouter(routes, {
    initialEntries: [path],
    getContext: () => createRouterContext(queryClient),
  });
  setUnauthorizedHandler(createUnauthorizedHandler(queryClient, router));

  const view = render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );

  return { ...view, router, queryClient };
}
