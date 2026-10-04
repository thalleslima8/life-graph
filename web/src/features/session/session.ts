import { queryOptions, useQuery } from "@tanstack/react-query";
import { api, unwrap } from "@/api/client";

export type Session = { email: string };

export const sessionQueryKey = ["session"] as const;

/** Who is signed in. A 401 is an answer here (nobody), so it never reaches the unauthorized handler. */
export const sessionQuery = queryOptions({
  queryKey: sessionQueryKey,
  queryFn: async ({ signal }): Promise<Session | null> => {
    const result = await api.GET("/api/sessions/current", { signal });
    if (result.response.status === 401) {
      return null;
    }

    return { email: unwrap(result).email };
  },
  staleTime: 5 * 60_000,
});

export function useSession(): Session | null {
  return useQuery(sessionQuery).data ?? null;
}
