import type { QueryClient } from "@tanstack/react-query";
import { createContext, RouterContextProvider } from "react-router";

/** The QueryClient for loaders, so `routes` stays plain data and router and provider share one client. */
export const queryClientContext = createContext<QueryClient>();

/** The router's `getContext`: the same client the QueryClientProvider receives. */
export function createRouterContext(queryClient: QueryClient): RouterContextProvider {
  const context = new RouterContextProvider();
  context.set(queryClientContext, queryClient);
  return context;
}
