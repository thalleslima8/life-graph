import type { RouteObject } from "react-router";
import { ConfirmEmailPage } from "@/features/account/ConfirmEmailPage";
import { ForgotPasswordPage } from "@/features/account/ForgotPasswordPage";
import { LoginPage } from "@/features/account/LoginPage";
import { ResetPasswordPage } from "@/features/account/ResetPasswordPage";
import { SignedOutOnly } from "@/features/account/SignedOutOnly";
import { NodeInspectorPage } from "@/features/graph/NodeInspectorPage";
import { NodeListPage } from "@/features/graph/NodeListPage";
import { OntologyPage } from "@/features/graph/OntologyPage";
import { RecentChangesPage } from "@/features/graph/RecentChangesPage";
import { HomePage } from "@/features/home/HomePage";
import { NotFoundPage } from "@/features/not-found/NotFoundPage";
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
      { path: "changes", loader: requireSession, element: <RecentChangesPage /> },
      { path: "types", loader: requireSession, element: <OntologyPage /> },
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
