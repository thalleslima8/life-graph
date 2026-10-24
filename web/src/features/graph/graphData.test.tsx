import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, renderHook, waitFor } from "@testing-library/react";
import { http } from "msw";
import type { ReactNode } from "react";
import { MemoryRouter, useLocation } from "react-router";
import { describe, expect, it } from "vitest";
import { ApiError } from "@/api/client";
import { aNode, NODE_ID } from "@/test/graphFixtures";
import { csrfTokenHandler, problem } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { ANY_TYPE, NO_TYPE, graphKeys, toSchemaRefusal, useEditBaseline, useNodeListFilter, useUpdateNode, type NodeDetail } from "./graphData";

function wrapperWith(queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } }), path = "/") {
  return function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[path]}>{children}</MemoryRouter>
      </QueryClientProvider>
    );
  };
}

function apiError(status: number, code: string, details: Record<string, string[]> = {}) {
  return new ApiError(status, code, code, { details });
}

describe("useEditBaseline", () => {
  it("keeps the base and turns stale when a refetch brings a newer version into a changed form", () => {
    const { result, rerender } = renderHook(({ node, isDirty }: { node: NodeDetail; isDirty: boolean }) => useEditBaseline(node, isDirty), {
      wrapper: wrapperWith(),
      initialProps: { node: aNode({ version: 3 }), isDirty: true },
    });

    rerender({ node: aNode({ version: 4 }), isDirty: true });

    expect(result.current.baseVersion).toBe(3);
    expect(result.current.isStale).toBe(true);
  });

  it("follows the Node while the form is pristine", () => {
    const { result, rerender } = renderHook(({ node, isDirty }: { node: NodeDetail; isDirty: boolean }) => useEditBaseline(node, isDirty), {
      wrapper: wrapperWith(),
      initialProps: { node: aNode({ version: 3 }), isDirty: false },
    });

    rerender({ node: aNode({ version: 4 }), isDirty: false });

    expect(result.current.baseVersion).toBe(4);
    expect(result.current.isStale).toBe(false);
  });

  it("takes the current Node as the base on rebase", () => {
    const { result, rerender } = renderHook(({ node, isDirty }: { node: NodeDetail; isDirty: boolean }) => useEditBaseline(node, isDirty), {
      wrapper: wrapperWith(),
      initialProps: { node: aNode({ version: 3 }), isDirty: true },
    });
    rerender({ node: aNode({ version: 5 }), isDirty: true });

    act(() => result.current.rebase());

    expect(result.current.baseVersion).toBe(5);
    expect(result.current.isStale).toBe(false);
  });

  it("coerces a version the API sent as text", () => {
    const { result } = renderHook(() => useEditBaseline(aNode({ version: "7" }), false), { wrapper: wrapperWith() });

    expect(result.current.baseVersion).toBe(7);
  });
});

describe("useUpdateNode", () => {
  it("sends the given version and reads the Node again on a version conflict, without retrying", async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    queryClient.setQueryData(graphKeys.node(NODE_ID), aNode());
    const sent: unknown[] = [];
    server.use(
      csrfTokenHandler().handler,
      http.patch("/api/nodes/:nodeId", async ({ request }) => {
        sent.push(await request.json());
        return problem(409, "graph.node_version_conflict", { details: { version: ["2"] } });
      }),
    );
    const { result } = renderHook(() => useUpdateNode(NODE_ID), { wrapper: wrapperWith(queryClient) });

    act(() => result.current.mutate({ version: 1, title: "Minha" }));

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(sent).toEqual([{ version: 1, title: "Minha" }]);
    expect(queryClient.getQueryState(graphKeys.node(NODE_ID))?.isInvalidated).toBe(true);
  });
});

describe("useNodeListFilter", () => {
  it("reads and writes the filter in the URL", () => {
    const { result } = renderHook(
      () => {
        const [filter, setFilter] = useNodeListFilter();
        return { filter, setFilter, search: useLocation().search };
      },
      { wrapper: wrapperWith(undefined, "/nodes?inbox=1") },
    );
    expect(result.current.filter).toEqual({ type: ANY_TYPE, inbox: true });

    act(() => result.current.setFilter({ type: NO_TYPE }));

    expect(result.current.filter).toEqual({ type: NO_TYPE, inbox: true });
    expect(result.current.search).toBe("?type=none&inbox=1");
  });
});

describe("toSchemaRefusal", () => {
  it.each([
    ["graph.type_in_use", { kind: "in_use", entity: "type" }],
    ["graph.property_in_use", { kind: "in_use", entity: "property" }],
    ["graph.property_has_values", { kind: "has_values" }],
  ])("maps %s", (code, refusal) => {
    expect(toSchemaRefusal(apiError(422, code))).toEqual(refusal);
  });

  it.each([
    ["graph.type_in_use_by_deleted_nodes", "type"],
    ["graph.property_in_use_by_deleted_nodes", "property"],
  ] as const)("maps %s with the count and the last purge date", (code, entity) => {
    const refusal = toSchemaRefusal(apiError(422, code, { deletedNodeCount: ["2"], lastPurgeAt: ["2026-11-04T00:00:00Z"] }));

    expect(refusal).toEqual({ kind: "in_use", entity, tombstoneCount: 2, lastPurgeAt: "2026-11-04T00:00:00Z" });
  });

  it("works without the details", () => {
    expect(toSchemaRefusal(apiError(422, "graph.type_in_use_by_deleted_nodes"))).toEqual({ kind: "in_use", entity: "type" });
  });

  it("is null for any other error", () => {
    expect(toSchemaRefusal(apiError(409, "graph.type_name_taken"))).toBeNull();
    expect(toSchemaRefusal(new Error("boom"))).toBeNull();
  });
});
