import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { axe } from "vitest-axe";
import { aLocalGraphModel } from "@/test/fakeGraphRenderer";
import { NODE_ID, OTHER_NODE_ID } from "@/test/graphFixtures";
import { GraphList, type GraphListProps } from "./GraphList";
import { graphKeyboardHelp } from "./graphMessages";
import type { LocalGraph } from "./localGraph";

const THIRD_ID = "01920000-0000-7000-8000-000000000003";
const labels = { canvas: "Grafo local de Leituras", agentUnreviewed: "mudança de agente não revisada", nodeSummary: () => "Livro, 1 Relation" };

/** Leituras → cita → Meditações (Hard), Meditações → cita → Estoicismo (Soft, changed by an agent). */
function aDeeperGraph(): LocalGraph {
  const graph = aLocalGraphModel();
  const [relation] = graph.edges;
  if (!relation) {
    throw new Error("The model graph has a Relation.");
  }

  return {
    ...graph,
    nodes: [...graph.nodes, { id: THIRD_ID, title: "Estoicismo", typeId: null, distance: 2, degree: 1, agentUnreviewed: true }],
    edges: [...graph.edges, { ...relation, id: "01920000-0000-7000-8000-0000000000f2", sourceNodeId: OTHER_NODE_ID, targetNodeId: THIRD_ID, assertion: "soft", agentUnreviewed: true }],
  };
}

function listProps(overrides: Partial<GraphListProps> = {}): GraphListProps {
  return { graph: aDeeperGraph(), rootId: NODE_ID, highlightAgent: true, onSelect: vi.fn(), onReroot: vi.fn(), labels, ...overrides };
}

const nodeButton = (title: string) => screen.getByRole("button", { name: title });

describe("graph list", () => {
  it("lists the Nodes by distance from the root, each with its Relations in words", () => {
    render(<GraphList {...listProps()} />);

    const list = screen.getByRole("region", { name: labels.canvas });
    expect(list).toHaveAccessibleDescription(graphKeyboardHelp({ canReroot: true, canZoom: false }));
    expect(within(list).getAllByRole("heading", { level: 2 }).map((heading) => heading.textContent)).toEqual(["Raiz", "A 1 Relation da raiz", "A 2 Relations da raiz"]);
    expect(within(list).getAllByRole("button", { name: /^(Leituras|Meditações|Estoicismo)$/ }).map((button) => button.textContent)).toEqual(["Leituras", "Meditações", "Estoicismo"]);
    expect(within(screen.getByRole("list", { name: "Relations de Meditações" })).getAllByRole("listitem").map((item) => item.textContent)).toEqual([
      "Leituras → cita → Meditações · Hard",
      "Meditações → cita → Estoicismo · Soft · ◎ mudança de agente não revisada",
    ]);
    expect(within(nodeButton("Estoicismo").closest("li") as HTMLElement).getAllByText("mudança de agente não revisada")).toHaveLength(2);
  });

  it("says nothing of agents when the view does not ask for the highlight", () => {
    render(<GraphList {...listProps({ highlightAgent: false })} />);

    expect(screen.queryByText(/mudança de agente/)).not.toBeInTheDocument();
  });

  it("selects a Node on click or Enter and marks the selected one as current", async () => {
    const user = userEvent.setup();
    const props = listProps({ selectedId: OTHER_NODE_ID });
    render(<GraphList {...props} />);

    await user.click(nodeButton("Estoicismo"));
    nodeButton("Leituras").focus();
    await user.keyboard("{Enter}");

    expect(props.onSelect).toHaveBeenNthCalledWith(1, THIRD_ID);
    expect(props.onSelect).toHaveBeenNthCalledWith(2, NODE_ID);
    expect(props.onSelect).toHaveBeenCalledTimes(2);
    expect(nodeButton("Meditações")).toHaveAttribute("aria-current", "true");
    expect(nodeButton("Leituras")).not.toHaveAttribute("aria-current");
  });

  it("re-roots on a Node from its button or Shift+Enter, never on the root itself", async () => {
    const user = userEvent.setup();
    const props = listProps();
    render(<GraphList {...props} />);

    await user.click(screen.getByRole("button", { name: "Centralizar aqui: Estoicismo" }));
    nodeButton("Meditações").focus();
    await user.keyboard("{Shift>}{Enter}{/Shift}");
    nodeButton("Leituras").focus();
    await user.keyboard("{Shift>}{Enter}{/Shift}");

    expect(props.onReroot).toHaveBeenNthCalledWith(1, THIRD_ID);
    expect(props.onReroot).toHaveBeenNthCalledWith(2, OTHER_NODE_ID);
    expect(props.onReroot).toHaveBeenCalledTimes(2);
    expect(props.onSelect).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: "Centralizar aqui: Leituras" })).not.toBeInTheDocument();
  });

  it("offers no re-root when the graph cannot be re-rooted", async () => {
    const user = userEvent.setup();
    render(<GraphList {...listProps({ onReroot: undefined })} />);

    nodeButton("Meditações").focus();
    await user.keyboard("{Shift>}{Enter}{/Shift}");

    expect(screen.queryByRole("button", { name: /^Centralizar aqui/ })).not.toBeInTheDocument();
    expect(screen.getByText(/^Teclado:/)).not.toHaveTextContent("Shift+Enter");
  });

  it("moves between the Nodes with the arrows and goes back to the selected Node on Escape", async () => {
    const user = userEvent.setup();
    render(<GraphList {...listProps({ selectedId: OTHER_NODE_ID })} />);
    nodeButton("Leituras").focus();

    await user.keyboard("{ArrowRight}");
    expect(nodeButton("Meditações")).toHaveFocus();
    await user.keyboard("{ArrowDown}");
    expect(nodeButton("Estoicismo")).toHaveFocus();
    await user.keyboard("{ArrowLeft}");
    expect(nodeButton("Meditações")).toHaveFocus();
    await user.keyboard("{ArrowUp}");
    expect(nodeButton("Leituras")).toHaveFocus();
    await user.keyboard("{ArrowDown}{ArrowDown}{Escape}");
    expect(nodeButton("Meditações")).toHaveFocus();
  });

  it("reaches every Node and its re-root with Tab alone", async () => {
    const user = userEvent.setup();
    render(<GraphList {...listProps()} />);

    await user.tab();
    await user.tab();
    await user.tab();

    expect(screen.getByRole("button", { name: "Centralizar aqui: Meditações" })).toHaveFocus();
  });

  it("has no accessibility violations", async () => {
    const { container } = render(<GraphList {...listProps({ selectedId: OTHER_NODE_ID })} />);

    expect(await axe(container)).toHaveNoViolations();
  });
});

describe("graph list with a Soft Relation from the root", () => {
  it("says Soft and explains the inference in words, never by colour", () => {
    const graph = aLocalGraphModel();
    const softEdge = {
      id: "01920000-0000-7000-8000-0000000000f3",
      sourceNodeId: NODE_ID,
      targetNodeId: OTHER_NODE_ID,
      kind: "related_to",
      assertion: "soft" as const,
      origin: "system" as const,
      strength: 0.82,
      agentUnreviewed: false,
      explanation: { method: "embedding", modelId: "multilingual-e5-small", score: 0.82 },
    };
    render(<GraphList {...listProps({ graph: { ...graph, edges: [softEdge] } })} />);

    expect(within(screen.getByRole("list", { name: "Relations de Leituras" })).getByRole("listitem")).toHaveTextContent(
      "Leituras → related_to → Meditações · Soft · embedding · score 0,82 · multilingual-e5-small",
    );
  });
});
