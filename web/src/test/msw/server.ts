import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

// Tests register their own handlers with server.use(...). The menu reads the pinned
// Collections on every signed-in screen (DA-173), and every Node Inspector reads the Node's
// Soft Relations (DA-179), the Raiz da Conta (DA-208) and the shared Nodes behind the
// "compartilhado" badge (DA-078), so those reads answer empty, or with ACCOUNT_ROOT_ID, unless a
// test says otherwise; resetHandlers keeps them.
export const ACCOUNT_ROOT_ID = "01a10492-43b8-7176-9da5-49f6f6240000";
const emptyPage = () => HttpResponse.json({ data: [], page: { nextCursor: null } });
export const server = setupServer(
  http.get("/api/collections", emptyPage),
  http.get("/api/nodes/:nodeId/soft-relations", emptyPage),
  http.get("/api/account-root", () => HttpResponse.json({ id: ACCOUNT_ROOT_ID })),
  http.get("/api/shared-graph-views/node-ids", emptyPage),
);
