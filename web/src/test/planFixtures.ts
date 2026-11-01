import type { AccountPlanView, LimitKind } from "@/features/account/planData";

/** The Free plan's defaults (docs/global_v2.md §55), every limit with nothing used yet. */
const FREE_LIMITS: Record<LimitKind, number> = {
  nodes: 500,
  relations: 2000,
  resources: 100,
  collections: 10,
  custom_types: 5,
  custom_properties: 20,
  agent_connections: 1,
  shared_graph_views: 3,
};

/** An Account on Free below every limit; `used` and `max` override single kinds. */
export function aPlan({
  plan = "free",
  used = {},
  max = {},
}: {
  plan?: AccountPlanView["plan"];
  used?: Partial<Record<LimitKind, number>>;
  max?: Partial<Record<LimitKind, number | null>>;
} = {}): AccountPlanView {
  return {
    plan,
    limits: (Object.keys(FREE_LIMITS) as LimitKind[]).map((kind) => ({
      kind,
      used: used[kind] ?? 0,
      max: kind in max ? (max[kind] ?? null) : FREE_LIMITS[kind],
    })),
    nodesPerSharedGraphView: 25,
    agentWrites: { used: 0, max: 100, resetsAt: "2026-12-01T00:00:00Z" },
  };
}
