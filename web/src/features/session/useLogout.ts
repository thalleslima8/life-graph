import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useNavigate, useRevalidator } from "react-router";
import { api, resetCsrfToken, unwrap } from "@/api/client";
import { sessionQueryKey } from "./session";

const LOGIN_PATH = "/login";

/**
 * Signs out and drops every cached response, even when the call fails: the browser must
 * not keep showing data of an identity it may no longer hold.
 * @param after "login" goes to the login page; "stay" reloads the current page, so a page
 * opened from an e-mailed link can use the link once signed out.
 */
export function useLogout(after: "login" | "stay" = "login") {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const revalidator = useRevalidator();

  return useMutation({
    mutationFn: async () => {
      unwrap(await api.DELETE("/api/sessions/current"));
    },
    onSettled: async () => {
      await queryClient.cancelQueries();
      queryClient.clear();
      resetCsrfToken();
      queryClient.setQueryData(sessionQueryKey, null);
      if (after === "stay") {
        await revalidator.revalidate();
      } else {
        await navigate(LOGIN_PATH, { replace: true });
      }
    },
  });
}
