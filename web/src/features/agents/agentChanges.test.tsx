import { act, renderHook } from "@testing-library/react";
import type { ReactNode } from "react";
import { createMemoryRouter, RouterProvider, useLocation } from "react-router";
import { describe, expect, it } from "vitest";
import { ApiError } from "@/api/client";
import { toApproval, toProposalRefusal, useFeedFilters, type FeedFilters } from "./agentChanges";

const AGENT = "01920000-0000-7000-8000-0000000000e1";

describe("toApproval", () => {
  it("sends nothing when there is no operation or none is chosen", () => {
    expect(toApproval(new Set(), 3)).toBeNull();
    expect(toApproval(new Set([0]), 0)).toBeNull();
  });

  it("approves everything without a list when every operation is chosen", () => {
    expect(toApproval(new Set([2, 0, 1]), 3)).toBeUndefined();
  });

  it("lists the chosen operations in order otherwise", () => {
    expect(toApproval(new Set([2, 0]), 3)).toEqual([0, 2]);
  });
});

describe("toProposalRefusal", () => {
  it("reads the operations a conflict or a missing dependency names", () => {
    const conflict = new ApiError(409, "graph.node_version_conflict", "changed", { details: { "operations[2].version": ["3"], "operations[0].version": ["1"] } });
    const dependency = new ApiError(422, "agents.operation_dependency", "left out", { fieldErrors: { "operations[1]": ["uses 0"] } });

    expect(toProposalRefusal(conflict)).toEqual({ kind: "version_conflict", operations: [0, 2] });
    expect(toProposalRefusal(dependency)).toEqual({ kind: "dependency", operations: [1] });
  });

  // DA-212: a loose anchor names its operations; details missing or malformed name none.
  it("reads the operations a loose anchor names, and knows a missing root", () => {
    const loose = new ApiError(422, "graph.anchor_required", "loose", { details: { "operations[3].anchor": ["x"], "operations[1].anchor.nodeId": ["y"] } });
    const malformed = new ApiError(422, "graph.anchor_required", "loose", { details: { "operations[x].anchor": ["x"], anchor: ["y"] } });
    const bare = new ApiError(422, "graph.anchor_required", "loose");

    expect(toProposalRefusal(loose)).toEqual({ kind: "anchor_required", operations: [1, 3] });
    expect(toProposalRefusal(malformed)).toEqual({ kind: "anchor_required", operations: [] });
    expect(toProposalRefusal(bare)).toEqual({ kind: "anchor_required", operations: [] });
    expect(toProposalRefusal(new ApiError(422, "graph.root_missing", "no root"))).toEqual({ kind: "root_missing" });
  });

  it("knows a decided and a missing proposal, and nothing else", () => {
    expect(toProposalRefusal(new ApiError(422, "agents.proposal_not_pending", "decided"))).toEqual({ kind: "not_pending" });
    expect(toProposalRefusal(new ApiError(404, "agents.proposal_not_found", "gone"))).toEqual({ kind: "not_found" });
    expect(toProposalRefusal(new ApiError(500, "unexpected", "boom"))).toBeNull();
    expect(toProposalRefusal(new Error("network"))).toBeNull();
    expect(toProposalRefusal(null)).toBeNull();
  });
});

function withRouter(path: string) {
  const location = { current: "" };
  function Probe() {
    const current = useLocation();
    location.current = current.search;
    return null;
  }

  const wrapper = ({ children }: { children: ReactNode }) => {
    const router = createMemoryRouter(
      [
        {
          path: "/agent-changes",
          element: (
            <>
              {children}
              <Probe />
            </>
          ),
        },
      ],
      { initialEntries: [path] },
    );
    return <RouterProvider router={router} />;
  };
  return { wrapper, location };
}

describe("useFeedFilters", () => {
  it("reads the filters from the URL", () => {
    const { wrapper } = withRouter(`/agent-changes?agentIdentityId=${AGENT}&unreviewed=1&status=rejected`);
    const { result } = renderHook(() => useFeedFilters(), { wrapper });

    expect(result.current[0]).toEqual<FeedFilters>({ agentIdentityId: AGENT, unreviewed: true, status: "rejected" });
  });

  it("falls back to the default for each invalid value alone", () => {
    const { wrapper } = withRouter(`/agent-changes?agentIdentityId=not-a-uuid&unreviewed=yes&status=rejected`);
    const { result } = renderHook(() => useFeedFilters(), { wrapper });

    expect(result.current[0]).toEqual<FeedFilters>({ unreviewed: false, status: "rejected" });
  });

  it("writes a change to the URL and keeps the other filters", () => {
    const { wrapper, location } = withRouter(`/agent-changes?status=proposed`);
    const { result } = renderHook(() => useFeedFilters(), { wrapper });

    act(() => result.current[1]({ unreviewed: true, agentIdentityId: AGENT }));

    expect(result.current[0]).toEqual<FeedFilters>({ agentIdentityId: AGENT, unreviewed: true, status: "proposed" });
    expect(new URLSearchParams(location.current).get("status")).toBe("proposed");

    act(() => result.current[1]({ status: undefined, unreviewed: false }));

    expect(location.current).toBe(`?agentIdentityId=${AGENT}`);
  });
});
