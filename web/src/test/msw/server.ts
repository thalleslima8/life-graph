import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

// Tests register their own handlers with server.use(...). The menu reads the pinned
// Collections on every signed-in screen (DA-173), and every Node Inspector reads the Node's
// Soft Relations (DA-179), so those reads answer empty unless a test says otherwise;
// resetHandlers keeps them.
const emptyPage = () => HttpResponse.json({ data: [], page: { nextCursor: null } });
export const server = setupServer(http.get("/api/collections", emptyPage), http.get("/api/nodes/:nodeId/soft-relations", emptyPage));
