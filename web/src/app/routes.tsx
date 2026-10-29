import type { RouteObject } from "react-router";
import { CollectionPage } from "@/features/collections/CollectionPage";
import { CollectionsPage } from "@/features/collections/CollectionsPage";
import { ConfirmEmailPage } from "@/features/account/ConfirmEmailPage";
import { ForgotPasswordPage } from "@/features/account/ForgotPasswordPage";
import { LoginPage } from "@/features/account/LoginPage";
import { ResetPasswordPage } from "@/features/account/ResetPasswordPage";
import { SignedOutOnly } from "@/features/account/SignedOutOnly";
import { GraphEntryPage } from "@/features/graph/GraphEntryPage";
import { NodeInspectorPage } from "@/features/graph/NodeInspectorPage";
import { NodeListPage } from "@/features/graph/NodeListPage";
import { OntologyPage } from "@/features/graph/OntologyPage";
import { RecentChangesPage } from "@/features/graph/RecentChangesPage";
import { UnanchoredPage } from "@/features/graph/UnanchoredPage";
import { HomePage } from "@/features/home/HomePage";
import { NotFoundPage } from "@/features/not-found/NotFoundPage";
import { TagsPage } from "@/features/tags/TagsPage";
import {
  blockIfAuthenticated,
  linkTokenLoader,
  redirectIfAuthenticated,
  requireSession,
  shouldReadLinkToken,
} from "@/features/session/loaders";
import { AppLayout } from "./AppLayout";
import { RouteErrorPage } from "./RouteErrorPage";

// Exported as data so tests mount the same tree with a memory router. Loaders read the
// QueryClient from the router context (DA-097).
export const routes: RouteObject[] = [
  {
    path: "/",
    element: <AppLayout />,
    errorElement: <RouteErrorPage />,
    children: [
      { index: true, loader: requireSession, element: <HomePage /> },
      { path: "nodes", loader: requireSession, element: <NodeListPage /> },
      { path: "nodes/:nodeId", loader: requireSession, element: <NodeInspectorPage /> },
      { path: "unanchored", loader: requireSession, element: <UnanchoredPage /> },
      { path: "graph", loader: requireSession, element: <GraphEntryPage /> },
      {
        path: "nodes/:nodeId/graph",
        loader: requireSession,
        lazy: async () => ({ Component: (await import("@/features/graph/LocalGraphPage")).LocalGraphPage }),
      },
      {
        path: "search",
        loader: requireSession,
        lazy: async () => ({ Component: (await import("@/features/search/SearchPage")).SearchPage }),
      },
      { path: "changes", loader: requireSession, element: <RecentChangesPage /> },
      { path: "types", loader: requireSession, element: <OntologyPage /> },
      { path: "tags", loader: requireSession, element: <TagsPage /> },
      { path: "collections", loader: requireSession, element: <CollectionsPage /> },
      { path: "collections/:collectionId", loader: requireSession, element: <CollectionPage /> },
      {
        path: "agent-changes",
        loader: requireSession,
        lazy: async () => ({ Component: (await import("@/features/agents/AgentChangesPage")).AgentChangesPage }),
      },
      {
        path: "agent-changes/proposals/:proposalId",
        loader: requireSession,
        lazy: async () => ({ Component: (await import("@/features/agents/ProposalPage")).ProposalPage }),
      },
      {
        path: "changesets/:changeSetId",
        loader: requireSession,
        lazy: async () => ({ Component: (await import("@/features/agents/ChangeSetPage")).ChangeSetPage }),
      },
      {
        path: "agentes",
        loader: requireSession,
        lazy: async () => ({ Component: (await import("@/features/agents/AgentsPage")).AgentsPage }),
      },
      {
        path: "shares",
        loader: requireSession,
        lazy: async () => ({ Component: (await import("@/features/sharing/SharesPage")).SharesPage }),
      },
      {
        path: "shares/new",
        loader: requireSession,
        lazy: async () => ({ Component: (await import("@/features/sharing/NewSharePage")).NewSharePage }),
      },
      { path: "login", loader: redirectIfAuthenticated, element: <LoginPage /> },
      { path: "forgot-password", loader: redirectIfAuthenticated, element: <ForgotPasswordPage /> },
      {
        path: "confirm-email",
        loader: blockIfAuthenticated,
        element: <SignedOutOnly />,
        children: [{ index: true, loader: linkTokenLoader, shouldRevalidate: shouldReadLinkToken, element: <ConfirmEmailPage /> }],
      },
      {
        path: "reset-password",
        loader: blockIfAuthenticated,
        element: <SignedOutOnly />,
        children: [{ index: true, loader: linkTokenLoader, shouldRevalidate: shouldReadLinkToken, element: <ResetPasswordPage /> }],
      },
      { path: "*", element: <NotFoundPage /> },
    ],
  },
];
