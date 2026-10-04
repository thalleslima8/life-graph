import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useNavigate, useSearchParams } from "react-router";
import { api, resetCsrfToken, unwrap } from "@/api/client";
import { safeReturnTo } from "./safeReturnTo";
import { sessionQuery } from "./session";

export type Credentials = { email: string; password: string };

/**
 * Signs in, then goes back to the page that asked for it (`?returnTo=`). It starts from an
 * empty cache: nothing cached for a previous identity survives.
 */
export function useLogin() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const returnTo = searchParams.get("returnTo");

  return useMutation({
    mutationFn: async (credentials: Credentials) => {
      unwrap(await api.POST("/api/sessions", { body: credentials }));
    },
    onSuccess: async () => {
      await queryClient.cancelQueries();
      queryClient.clear();
      resetCsrfToken();
      await queryClient.fetchQuery(sessionQuery);
      await navigate(safeReturnTo(returnTo), { replace: true });
    },
  });
}
