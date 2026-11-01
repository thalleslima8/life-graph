import { queryOptions, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback } from "react";
import { api, isApiError, unwrap } from "@/api/client";
import type { components } from "@/api/schema.gen";

/**
 * The Account's plan, its limits and what the Account uses of each (E13, DA-266). The limit in
 * the client is UX only: the server decides every creation (FE-020). A limit stops only new
 * creation; viewing, editing, deleting and exporting never consult it (DA-086). No price and no
 * offer of Premium (DA-260).
 */

type Schemas = components["schemas"];
export type AccountPlanView = Schemas["AccountPlanView"];
export type LimitKind = Schemas["PlanLimitKind"];

/** The refusal of a creation past the plan's limit; its details name the kind and the limit. */
export const PLAN_LIMIT_REACHED_CODE = "plan.limit_reached";

export const planKeys = {
  all: ["account", "plan"] as const,
};

/** Where the Account stands. Read when a screen needs it, and again after each creation settles. */
export const planOptions = () =>
  queryOptions({
    queryKey: planKeys.all,
    queryFn: async ({ signal }) => unwrap(await api.GET("/api/account/plan", { signal })),
  });

/**
 * One limit as a screen shows it. `max: null` is a plan with no limit of that kind (never
 * reached), apart from `undefined`, which {@link limitStatus} answers while the plan loads.
 */
export type LimitStatus = {
  kind: LimitKind;
  used: number;
  max: number | null;
  reached: boolean;
};

/** Whether `error` refuses a creation past the plan's limit, of `kind` when one is given. */
export function isPlanLimitReached(error: unknown, kind?: LimitKind): boolean {
  if (!isApiError(error, PLAN_LIMIT_REACHED_CODE)) {
    return false;
  }

  return kind === undefined || error.details.kind?.[0] === kind;
}

/** The limit the refusal carries, when it carries one. */
export function refusedLimitOf(error: unknown): number | undefined {
  if (!isApiError(error, PLAN_LIMIT_REACHED_CODE)) {
    return undefined;
  }

  const limit = Number(error.details.limit?.[0]);
  return Number.isInteger(limit) ? limit : undefined;
}

/** The status of `kind` in `plan`; `undefined` while the plan is not loaded. */
export function limitStatus(plan: AccountPlanView | undefined, kind: LimitKind): LimitStatus | undefined {
  const limit = plan?.limits.find((entry) => entry.kind === kind);
  if (!limit) {
    return undefined;
  }

  const used = Number(limit.used);
  const max = limit.max === null ? null : Number(limit.max);
  return { kind, used, max, reached: max !== null && used >= max };
}

/** The status of one limit for a creation point; `undefined` while loading or when the plan fails to load. */
export function usePlanLimit(kind: LimitKind): LimitStatus | undefined {
  return useQuery({ ...planOptions(), select: (plan) => limitStatus(plan, kind) }).data;
}

/** Reads the plan again: called in the `onSettled` of every creation, which may have taken or freed a place. */
export function useInvalidatePlan(): () => Promise<void> {
  const queryClient = useQueryClient();
  return useCallback(() => queryClient.invalidateQueries({ queryKey: planKeys.all }), [queryClient]);
}
