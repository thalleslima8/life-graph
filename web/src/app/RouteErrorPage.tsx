import { Button } from "@/components/ui/button";

/** Shown when a page cannot load (e.g. the API is unreachable); a reload starts over. */
export function RouteErrorPage() {
  return (
    <main className="mx-auto max-w-5xl space-y-4 px-4 py-8">
      <h1 className="text-2xl font-semibold">Algo deu errado</h1>
      <p className="text-muted-foreground">Não foi possível carregar esta página. Tente novamente em instantes.</p>
      <Button type="button" variant="secondary" onClick={() => window.location.reload()}>
        Tentar novamente
      </Button>
    </main>
  );
}
