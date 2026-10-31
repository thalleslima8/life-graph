import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { act } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { aRenderableGraphModel, fakeGraphRenderer as fake } from "@/test/fakeGraphRenderer";
import { NODE_ID, OTHER_NODE_ID } from "@/test/graphFixtures";
import { INITIAL_ZOOM, steppedZoom } from "./graphView";
import { GraphCanvas, type GraphCanvasProps } from "./GraphCanvas";
import { graphKeyboardHelp } from "./graphMessages";

// Sigma reads WebGL globals as it loads, and jsdom has none: the default adapter is never loaded here.
vi.mock("./graphRenderer", () => ({ createSigmaRenderer: () => undefined }));

const labels = { canvas: "Grafo local de Leituras", agentUnreviewed: "mudança de agente não revisada", nodeSummary: () => "Livro, 1 Relation" };

function canvasProps(overrides: Partial<GraphCanvasProps> = {}): GraphCanvasProps {
  return {
    graph: aRenderableGraphModel(),
    highlightAgent: true,
    zoom: 1,
    onZoomChange: vi.fn(),
    onSelect: vi.fn(),
    labels,
    rendererFactory: fake.factory,
    ...overrides,
  };
}

describe("graph canvas", () => {
  beforeEach(() => fake.reset());

  it("mounts the renderer once into its drawing, updates it when the props change and disposes of it", () => {
    const props = canvasProps();
    const { rerender, unmount } = render(<GraphCanvas {...props} />);

    expect(fake.recorded.mounts.map((mount) => [mount.container, mount.view.rootId])).toEqual([
      [screen.getByRole("application", { name: labels.canvas }), NODE_ID],
    ]);
    expect(fake.recorded.updates).toEqual([]);

    rerender(<GraphCanvas {...props} selectedId={OTHER_NODE_ID} zoom={2} />);
    unmount();

    expect(fake.recorded.mounts).toHaveLength(1);
    expect(fake.recorded.updates.map((view) => [view.selectedId, view.zoom])).toEqual([[OTHER_NODE_ID, 2]]);
    expect(fake.recorded.disposed).toBe(1);
  });

  it("hands what the person does to the latest callbacks, without drawing again", () => {
    const first = canvasProps();
    const latest = { onSelect: vi.fn(), onReroot: vi.fn(), onZoomChange: vi.fn() };
    const { rerender } = render(<GraphCanvas {...first} />);
    rerender(<GraphCanvas {...first} {...latest} />);

    act(() => {
      fake.events().onSelect(OTHER_NODE_ID);
      fake.events().onReroot(OTHER_NODE_ID);
      fake.events().onZoomChange(1.5);
    });

    expect(first.onSelect).not.toHaveBeenCalled();
    expect([latest.onSelect.mock.calls, latest.onReroot.mock.calls, latest.onZoomChange.mock.calls]).toEqual([[[OTHER_NODE_ID]], [[OTHER_NODE_ID]], [[1.5]]]);
    expect(fake.recorded.updates).toEqual([]);
  });

  it("does nothing on a re-root, nor offers one in its help, when the graph cannot be re-rooted", () => {
    const props = canvasProps();
    render(<GraphCanvas {...props} />);

    act(() => fake.events().onReroot(OTHER_NODE_ID));

    expect(props.onSelect).not.toHaveBeenCalled();
    expect(screen.getByRole("application", { name: labels.canvas })).toHaveAccessibleDescription(graphKeyboardHelp({ canReroot: false, canZoom: true }));
  });

  it("takes the keyboard: the arrows move a boxed, announced Node; Enter selects, Shift+Enter re-roots, + − 0 zoom", async () => {
    const user = userEvent.setup();
    const props = canvasProps({ zoom: 2, onReroot: vi.fn() });
    render(<GraphCanvas {...props} />);
    const drawing = screen.getByRole("application", { name: labels.canvas });
    const focusedNow = () => fake.recorded.updates.at(-1)?.focusedId;

    await user.tab();
    expect([drawing, focusedNow()]).toEqual([document.activeElement, NODE_ID]);
    await user.keyboard("{ArrowRight}");
    expect(focusedNow()).toBe(OTHER_NODE_ID);
    expect(screen.getByRole("status")).toHaveTextContent("Meditações, Livro, 1 Relation, A 1 Relation da raiz");
    await user.keyboard("{Enter}{Shift>}{Enter}{/Shift}+-0");
    await user.keyboard("{ArrowLeft}{Shift>}{Enter}{/Shift}");

    expect(props.onSelect).toHaveBeenCalledExactlyOnceWith(OTHER_NODE_ID);
    expect(props.onReroot).toHaveBeenCalledExactlyOnceWith(OTHER_NODE_ID);
    expect((props.onZoomChange as ReturnType<typeof vi.fn>).mock.calls).toEqual([[steppedZoom(2, "in")], [steppedZoom(2, "out")], [INITIAL_ZOOM]]);
    expect(drawing).toHaveAccessibleDescription(graphKeyboardHelp({ canReroot: true, canZoom: true }));
  });

  it("goes back to the selected Node on Escape and boxes nothing once the drawing loses focus", async () => {
    const user = userEvent.setup();
    render(<GraphCanvas {...canvasProps({ selectedId: OTHER_NODE_ID })} />);
    const focusedNow = () => fake.recorded.updates.at(-1)?.focusedId;

    await user.tab();
    await user.keyboard("{ArrowLeft}");
    expect(focusedNow()).toBe(NODE_ID);
    await user.keyboard("{Escape}");
    expect([focusedNow(), screen.getByRole("status").textContent]).toEqual([OTHER_NODE_ID, "Meditações, Livro, 1 Relation, A 1 Relation da raiz, selecionado"]);
    await user.tab();

    expect(focusedNow()).toBeUndefined();
  });

  it("says when the browser cannot draw, and when drawing fails later", () => {
    const onUnavailable = vi.fn();
    fake.recorded.failMount = true;
    render(<GraphCanvas {...canvasProps({ onUnavailable })} />);
    expect(onUnavailable).toHaveBeenCalledOnce();

    fake.reset();
    const onRenderError = vi.fn();
    const props = canvasProps({ onRenderError });
    const { rerender } = render(<GraphCanvas {...props} />);
    fake.recorded.failUpdate = true;
    rerender(<GraphCanvas {...props} zoom={2} />);

    expect(onRenderError).toHaveBeenCalledWith(new Error("The WebGL context was lost."));
  });
});
