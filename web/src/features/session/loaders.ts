import { redirect, useLoaderData, type LoaderFunctionArgs } from "react-router";
import { queryClientContext } from "./queryClientContext";
import { safeReturnTo } from "./safeReturnTo";
import { sessionQuery, type Session } from "./session";

function currentSession({ context }: LoaderFunctionArgs): Promise<Session | null> {
  return context.get(queryClientContext).ensureQueryData(sessionQuery);
}

/** Pages of the app: without a session, go to login and come back afterwards. */
export async function requireSession(args: LoaderFunctionArgs): Promise<Session> {
  const session = await currentSession(args);
  if (!session) {
    const { pathname, search, hash } = args.url;
    const returnTo = safeReturnTo(`${pathname}${search}${hash}`);
    throw redirect(`/login?returnTo=${encodeURIComponent(returnTo)}`);
  }

  return session;
}

/** Login and forgot-password: someone already signed in goes into the app. */
export async function redirectIfAuthenticated(args: LoaderFunctionArgs): Promise<null> {
  if (await currentSession(args)) {
    throw redirect(safeReturnTo(args.url.searchParams.get("returnTo")));
  }

  return null;
}

/**
 * Pages opened from an e-mailed link: when someone is signed in, the page shows who and
 * offers to sign out, without reading or consuming the link.
 */
export function blockIfAuthenticated(args: LoaderFunctionArgs): Promise<Session | null> {
  return currentSession(args);
}

export type LinkToken = { userId: string; token: string };

/**
 * Reads the user id and token from the URL fragment (never sent to a server) once per
 * navigation and strips it from the address bar and history. Signed in, it leaves the
 * fragment alone so the link still works after signing out.
 */
export async function linkTokenLoader(args: LoaderFunctionArgs): Promise<LinkToken | null> {
  if (await currentSession(args)) {
    return null;
  }

  const fragment = new URLSearchParams(window.location.hash.slice(1));
  const userId = fragment.get("userId");
  const token = fragment.get("token");
  if (window.location.hash) {
    window.history.replaceState(window.history.state, "", `${window.location.pathname}${window.location.search}`);
  }

  return userId && token ? { userId, token } : null;
}

/**
 * Once read, the fragment is gone: a revalidation keeps the token already loaded. Only a
 * fragment still in place (left alone while signed in) is read again.
 */
export function shouldReadLinkToken(): boolean {
  return window.location.hash !== "";
}

export function useLinkToken(): LinkToken | null {
  return useLoaderData<typeof linkTokenLoader>();
}
