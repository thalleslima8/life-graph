import { QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { delay, http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { createQueryClient } from "@/app/queryClient";
import { server } from "@/test/msw/server";
import { aPlan } from "@/test/planFixtures";
import { PlanUsageSection } from "./PlanUsageSection";

function renderSection() {
  return render(
    <QueryClientProvider client={createQueryClient()}>
      <PlanUsageSection />
    </QueryClientProvider>,
  );
}

describe("PlanUsageSection", () => {
  it("shows the plan and what the Account uses of each limit, with no price or offer", async () => {
    server.use(http.get("/api/account/plan", () => HttpResponse.json(aPlan({ used: { nodes: 12 } }))));
    const { container } = renderSection();

    const section = await screen.findByRole("region", { name: "Plano e uso" });
    expect(await screen.findByText("Free")).toBeInTheDocument();
    expect(section).toHaveTextContent("12 de 500");
    expect(section).toHaveTextContent("0 de 3");
    expect(section).not.toHaveTextContent(/R\$|assine|upgrade/i);
    expect(await axe(container)).toHaveNoViolations();
  });

  it("shows a limit without maximum as unlimited", async () => {
    server.use(
      http.get("/api/account/plan", () => HttpResponse.json(aPlan({ plan: "premium", used: { nodes: 7 }, max: { nodes: null } }))),
    );
    renderSection();

    expect(await screen.findByText("Premium")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Plano e uso" })).toHaveTextContent("7 (sem limite)");
  });

  it("says it is loading while the plan is read", () => {
    server.use(http.get("/api/account/plan", async () => {
      await delay("infinite");
      return HttpResponse.json(aPlan());
    }));
    renderSection();

    expect(screen.getByText("Carregando…")).toBeInTheDocument();
  });

  it("offers to try again when the plan fails to load (after the client's one retry)", async () => {
    let reads = 0;
    server.use(
      http.get("/api/account/plan", () => {
        reads += 1;
        return reads <= 2 ? HttpResponse.json({ code: "boom" }, { status: 500 }) : HttpResponse.json(aPlan());
      }),
    );
    const user = userEvent.setup();
    renderSection();

    expect(await screen.findByRole("alert")).toHaveTextContent("Não foi possível carregar o plano agora.");
    await user.click(screen.getByRole("button", { name: "Tentar de novo" }));
    expect(await screen.findByText("Free")).toBeInTheDocument();
  });
});
