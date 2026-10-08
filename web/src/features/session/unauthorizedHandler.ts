import type { QueryClient } from "@tanstack/react-query";
import { resetCsrfToken } from "@/api/client";
import { safeReturnTo } from "./safeReturnTo";
import { sessionQueryKey, type Session } from "./session";

type Navigator = {
  readonly state: { readonly location: { pathname: string; search: string; hash: string } };
  navigate(to: string, options?: { replace?: boolean }): Promise<void>;
};

/**
 * What a 401 from an authenticated call means: the session expired. It only acts when the
 * cache still holds a session, so anonymous pages never bounce, and a burst of 401s
 * leads to a single redirect.
 */
export function createUnauthorizedHandler(queryClient: QueryClient, router: Navigator): () => void {
  let isHandling = false;

  return () => {
    if (isHandling || !queryClient.getQueryData<Session | null>(sessionQueryKey)) {
      return;
    }

    isHandling = true;
    const { pathname, search, hash } = router.state.location;
    const returnTo = encodeURIComponent(safeReturnTo(`${pathname}${search}${hash}`));

    void (async () => {
      try {
        await queryClient.cancelQueries();
        queryClient.clear();
        resetCsrfToken();
        queryClient.setQueryData(sessionQueryKey, null);
        await router.navigate(`/login?returnTo=${returnTo}&notice=session_expired`, { replace: true });
      } finally {
        isHandling = false;
      }
    })();
  };
}
