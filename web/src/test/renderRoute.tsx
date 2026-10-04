import { QueryClientProvider } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { setUnauthorizedHandler } from "@/api/client";
import { createQueryClient } from "@/app/queryClient";
import { routes } from "@/app/routes";
import { createRouterContext } from "@/features/session/queryClientContext";
import { createUnauthorizedHandler } from "@/features/session/unauthorizedHandler";

/** Renders the real route tree at `path`, wired like main.tsx: one QueryClient for router and provider. */
export function renderRoute(path: string) {
  const queryClient = createQueryClient();
  // Tests fail fast instead of retrying.
  queryClient.setDefaultOptions({ queries: { ...queryClient.getDefaultOptions().queries, retry: false } });
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
