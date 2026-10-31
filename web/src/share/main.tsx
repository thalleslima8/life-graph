import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { VisitorPage } from "./VisitorPage";
import "@/index.css";

const rootElement = document.getElementById("root");
if (!rootElement) {
  throw new Error("visitor.html has no #root element.");
}

// The visitor's own cache: nothing of the app's session, client or router (DA-216).
const queryClient = new QueryClient({
  defaultOptions: { queries: { refetchOnWindowFocus: false } },
});

createRoot(rootElement).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <VisitorPage />
    </QueryClientProvider>
  </StrictMode>,
);
