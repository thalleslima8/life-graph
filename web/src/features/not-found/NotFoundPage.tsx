import { Link } from "react-router";
import { Button } from "@/components/ui/button";

export function NotFoundPage() {
  return (
    <section aria-labelledby="not-found-title" className="space-y-4">
      <h1 id="not-found-title" className="text-2xl font-semibold">
        Página não encontrada
      </h1>
      <Button asChild variant="secondary">
        <Link to="/">Voltar para o início</Link>
      </Button>
    </section>
  );
}
