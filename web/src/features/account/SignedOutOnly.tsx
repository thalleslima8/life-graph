import { Outlet, useLoaderData } from "react-router";
import { Button } from "@/components/ui/button";
import type { blockIfAuthenticated } from "@/features/session/loaders";
import { useLogout } from "@/features/session/useLogout";

/**
 * Wraps the pages opened from an e-mailed link. Signed in, it says who and offers to sign
 * out, leaving the link unread so it still works afterwards.
 */
export function SignedOutOnly() {
  const session = useLoaderData<typeof blockIfAuthenticated>();
  const logout = useLogout("stay");

  return (
    <>
      {/* The link's token is in the fragment, which is never sent; this keeps the page out of any Referer too. */}
      <meta name="referrer" content="no-referrer" />
      {session ? (
        <section aria-labelledby="signed-in-title" className="mx-auto max-w-sm space-y-4">
          <h1 id="signed-in-title" className="text-2xl font-semibold">
            Você já está conectado
          </h1>
          <p className="text-sm">
            Você está conectado como <strong>{session.email}</strong>. Para usar este link, saia primeiro.
          </p>
          <Button type="button" onClick={() => logout.mutate()} disabled={logout.isPending}>
            Sair
          </Button>
        </section>
      ) : (
        <Outlet />
      )}
    </>
  );
}
