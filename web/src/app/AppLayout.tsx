import { Link, Outlet } from "react-router";

export function AppLayout() {
  return (
    <div className="min-h-screen">
      <header className="border-b">
        <nav aria-label="Principal" className="mx-auto flex max-w-5xl items-center px-4 py-3">
          <Link to="/" className="font-semibold">
            Life Graph
          </Link>
        </nav>
      </header>
      <main className="mx-auto max-w-5xl px-4 py-8">
        <Outlet />
      </main>
    </div>
  );
}
