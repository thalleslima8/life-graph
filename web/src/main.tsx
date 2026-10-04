import { QueryClientProvider } from "@tanstack/react-query";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { createBrowserRouter, RouterProvider } from "react-router";
import { setUnauthorizedHandler } from "./api/client";
import { createQueryClient } from "./app/queryClient";
import { routes } from "./app/routes";
import { createRouterContext } from "./features/session/queryClientContext";
import { createUnauthorizedHandler } from "./features/session/unauthorizedHandler";
import "./index.css";

const rootElement = document.getElementById("root");
if (!rootElement) {
  throw new Error("index.html has no #root element.");
}

// One QueryClient for the router's loaders and for the components.
const queryClient = createQueryClient();
const router = createBrowserRouter(routes, { getContext: () => createRouterContext(queryClient) });
setUnauthorizedHandler(createUnauthorizedHandler(queryClient, router));

createRoot(rootElement).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
);
