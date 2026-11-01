import { QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import { delay, http, HttpResponse } from "msw";
import type { ReactNode } from "react";
import { describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { ApiError } from "@/api/client";
import { createQueryClient } from "@/app/queryClient";
import { server } from "@/test/msw/server";
import { aPlan } from "@/test/planFixtures";
import { LimitNotice } from "./LimitNotice";
import { PLAN_LIMIT_REACHED_CODE } from "./planData";

function renderNotice(notice: ReactNode) {
  const queryClient = createQueryClient();
  return render(<QueryClientProvider client={queryClient}>{notice}</QueryClientProvider>);
}

const answeringPlan = (plan: ReturnType<typeof aPlan>) => server.use(http.get("/api/account/plan", () => HttpResponse.json(plan)));

describe("LimitNotice", () => {
  it("announces a refused creation as an alert, with the way out", async () => {
    const refusal = new ApiError(422, PLAN_LIMIT_REACHED_CODE, "Refused", { details: { kind: ["collections"], limit: ["10"] } });
    answeringPlan(aPlan({ used: { collections: 10 } }));
    const { container } = renderNotice(<LimitNotice kind="collections" error={refusal} />);

    const alert = await screen.findByRole("alert");
    await waitFor(() => expect(alert).toHaveTextContent("Você usa 10 de 10 Collections"));
    expect(alert).toHaveTextContent("Nada foi bloqueado");
    expect(alert).toHaveTextContent("Exclua o que não usa mais para liberar espaço.");
    expect(await axe(container)).toHaveNoViolations();
  });

  it("says a reached limit as a status before any attempt", async () => {
    answeringPlan(aPlan({ used: { agent_connections: 1 } }));
    const { container } = renderNotice(<LimitNotice kind="agent_connections" />);

    const status = await screen.findByRole("status");
    expect(status).toHaveTextContent("Você usa 1 de 1 agentes conectados");
    // A connection is revoked, never deleted (DA-262).
    expect(status).toHaveTextContent('Revogue uma conexão que não usa mais em "Agentes conectados"');
    expect(status).not.toHaveTextContent("Exclua");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();
  });

  it("says nothing below the limit, on a plan without limit, or for another refusal", async () => {
    answeringPlan(aPlan({ used: { nodes: 400, custom_types: 99 }, max: { custom_types: null } }));
    const other = new ApiError(422, "graph.node_title_required", "Refused");
    const { container } = renderNotice(
      <>
        <LimitNotice kind="nodes" error={other} />
        <LimitNotice kind="custom_types" />
      </>,
    );

    await waitFor(() => expect(container).toBeEmptyDOMElement());
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(container).toBeEmptyDOMElement();
  });

  it("says nothing while the plan loads or when it fails to load: the server decides", async () => {
    server.use(http.get("/api/account/plan", async () => {
      await delay("infinite");
      return HttpResponse.json(aPlan());
    }));
    const loading = renderNotice(<LimitNotice kind="nodes" />);
    expect(loading.container).toBeEmptyDOMElement();
    loading.unmount();

    server.use(http.get("/api/account/plan", () => HttpResponse.json({ code: "boom" }, { status: 500 })));
    const failed = renderNotice(<LimitNotice kind="nodes" />);
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(failed.container).toBeEmptyDOMElement();
  });
});
