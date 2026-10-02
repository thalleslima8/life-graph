import type { RouteObject } from "react-router";
import { HomePage } from "@/features/home/HomePage";
import { NotFoundPage } from "@/features/not-found/NotFoundPage";
import { AppLayout } from "./AppLayout";

// Exported as data so tests mount the same tree with a memory router.
export const routes: RouteObject[] = [
  {
    path: "/",
    element: <AppLayout />,
    children: [
      { index: true, element: <HomePage /> },
      { path: "*", element: <NotFoundPage /> },
    ],
  },
];
