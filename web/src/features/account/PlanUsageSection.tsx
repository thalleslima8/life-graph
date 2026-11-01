import { useQuery } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { LIMIT_KIND_LABELS } from "./limitMessages";
import { limitStatus, planOptions, type AccountPlanView } from "./planData";

const TITLE_ID = "account-plan-title";

const PLAN_LABELS: Record<AccountPlanView["plan"], string> = {
  free: "Free",
  premium: "Premium",
};

/** The first day of the next quota period, in the person's locale, as a date only (UTC, DA-261). */
function resetDateOf(resetsAt: string): string {
  return new Intl.DateTimeFormat("pt-BR", { dateStyle: "long", timeZone: "UTC" }).format(new Date(resetsAt));
}

function usageText(used: number, max: number | null): string {
  return max === null ? `${used} (sem limite)` : `${used} de ${max}`;
}

/**
 * "Plano e uso" on the Account page (DA-266): the plan, what the Account uses of each limit and
 * the agents' monthly writes with the date they start again, which is display only. No price and
 * no offer (DA-260); a limit stops only new creation (DA-086).
 */
export function PlanUsageSection() {
  const plan = useQuery(planOptions());

  return (
    <section aria-labelledby={TITLE_ID} className="space-y-4 rounded-md border p-4">
      <h2 id={TITLE_ID} className="text-lg font-semibold">
        Plano e uso
      </h2>
      {plan.isPending ? (
        <p>Carregando…</p>
      ) : plan.isError ? (
        <div className="space-y-2">
          <p role="alert" className="text-sm text-destructive">
            Não foi possível carregar o plano agora.
          </p>
          <Button type="button" variant="secondary" size="sm" onClick={() => void plan.refetch()}>
            Tentar de novo
          </Button>
        </div>
      ) : (
        <PlanUsage view={plan.data} />
      )}
    </section>
  );
}

function PlanUsage({ view }: { view: AccountPlanView }) {
  const nodesPerShare = view.nodesPerSharedGraphView === null ? null : Number(view.nodesPerSharedGraphView);
  const agentWritesMax = view.agentWrites.max === null ? null : Number(view.agentWrites.max);

  return (
    <div className="space-y-3">
      <p className="text-sm">
        Seu plano: <strong>{PLAN_LABELS[view.plan]}</strong>. Ao atingir um limite, só a criação de algo novo daquele tipo
        para; o que já existe continua visível, editável, exportável e pode ser excluído.
      </p>
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        {view.limits.map((entry) => {
          const status = limitStatus(view, entry.kind);
          return status ? (
            <div key={entry.kind} className="contents">
              <dt>{LIMIT_KIND_LABELS[entry.kind]}</dt>
              <dd>
                {usageText(status.used, status.max)}
                {status.reached && <span className="ml-2 font-medium">(limite atingido)</span>}
              </dd>
            </div>
          ) : null;
        })}
        <dt>Nodes por link compartilhado</dt>
        <dd>{nodesPerShare === null ? "sem limite" : `até ${nodesPerShare}`}</dd>
        <dt>Escritas de agentes neste mês</dt>
        <dd>
          {usageText(Number(view.agentWrites.used), agentWritesMax)}; recomeça em {resetDateOf(view.agentWrites.resetsAt)}
        </dd>
      </dl>
    </div>
  );
}
