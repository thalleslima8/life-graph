import { setupServer } from "msw/node";

// Tests register their own handlers with server.use(...).
export const server = setupServer();
