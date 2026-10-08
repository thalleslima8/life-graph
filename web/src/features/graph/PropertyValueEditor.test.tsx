import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import type { PropertyValueKind } from "./graphData";
import { PropertyValueDisplay, PropertyValueEditor } from "./PropertyValueEditor";
import { emptyValueOf, type PropertyField, type PropertyValue } from "./propertyValues";

const OPTIONS = [
  { id: "01920000-0000-7000-8000-0000000000e1", label: "Lendo" },
  { id: "01920000-0000-7000-8000-0000000000e2", label: "Lido" },
];
const REMOVED = "01920000-0000-7000-8000-0000000000ef";

/** A controlled editor with its own state, as a form's Controller would hold it. */
function Harness({ definition, initial, error }: { definition: PropertyField; initial: PropertyValue; error?: string }) {
  const [value, setValue] = useState(initial);
  return (
    <>
      <PropertyValueEditor id="prop" definition={definition} value={value} onChange={setValue} error={error} />
      <output aria-label="valor">{JSON.stringify(value)}</output>
    </>
  );
}

const ALL_KINDS: PropertyValueKind[] = ["text", "number", "boolean", "date", "date_time", "url", "select", "multi_select"];

describe("PropertyValueEditor", () => {
  it.each(ALL_KINDS)("labels a %s field with the property name and has no accessibility violations", async (valueKind) => {
    const { container } = render(<Harness definition={{ name: "Status", valueKind, options: OPTIONS }} initial={emptyValueOf(valueKind)} error="Revise." />);

    const field = valueKind === "boolean" || valueKind === "multi_select" ? screen.getByRole("group", { name: "Status" }) : screen.getByLabelText("Status");
    expect(field).toHaveAccessibleDescription("Revise.");
    expect(await axe(container)).toHaveNoViolations();
  });

  it("edits a number as a number and clears it to null", async () => {
    render(<Harness definition={{ name: "Nota", valueKind: "number" }} initial={null} />);

    await userEvent.type(screen.getByLabelText("Nota"), "4.5");
    expect(screen.getByLabelText("valor")).toHaveTextContent("4.5");

    await userEvent.clear(screen.getByLabelText("Nota"));
    expect(screen.getByLabelText("valor")).toHaveTextContent("null");
  });

  it("chooses and clears a boolean", async () => {
    render(<Harness definition={{ name: "Lido", valueKind: "boolean" }} initial={null} />);

    await userEvent.click(screen.getByRole("radio", { name: "Não" }));
    expect(screen.getByLabelText("valor")).toHaveTextContent("false");

    await userEvent.click(screen.getByRole("button", { name: "Limpar Lido" }));
    expect(screen.getByLabelText("valor")).toHaveTextContent("null");
    expect(screen.getByRole("radio", { name: "Não" })).not.toBeChecked();
  });

  it("chooses a Select option by its label and stores its id", async () => {
    render(<Harness definition={{ name: "Status", valueKind: "select", options: OPTIONS }} initial={null} />);

    await userEvent.click(screen.getByRole("combobox", { name: "Status" }));
    await userEvent.click(await screen.findByRole("option", { name: "Lido" }));

    expect(screen.getByLabelText("valor")).toHaveTextContent(OPTIONS[1]?.id ?? "");
  });

  it("keeps a removed Select option visible until it is cleared", async () => {
    render(<Harness definition={{ name: "Status", valueKind: "select", options: OPTIONS }} initial={REMOVED} />);

    expect(screen.getByRole("combobox", { name: "Status" })).toHaveTextContent("(opção removida)");

    await userEvent.click(screen.getByRole("button", { name: "Limpar Status" }));
    expect(screen.getByLabelText("valor")).toHaveTextContent("null");
  });

  it("keeps a removed MultiSelect option checked, and lets it be unchecked", async () => {
    render(<Harness definition={{ name: "Tags", valueKind: "multi_select", options: OPTIONS }} initial={[REMOVED]} />);

    await userEvent.click(screen.getByRole("checkbox", { name: "Lendo" }));
    expect(screen.getByLabelText("valor")).toHaveTextContent(JSON.stringify([REMOVED, OPTIONS[0]?.id]));

    await userEvent.click(screen.getByRole("checkbox", { name: "(opção removida)" }));
    expect(screen.getByLabelText("valor")).toHaveTextContent(JSON.stringify([OPTIONS[0]?.id]));
    expect(screen.queryByRole("checkbox", { name: "(opção removida)" })).not.toBeInTheDocument();
  });
});

describe("PropertyValueDisplay", () => {
  it("shows option labels, and a removed option as such", () => {
    render(<PropertyValueDisplay definition={{ name: "Tags", valueKind: "multi_select", options: OPTIONS }} value={[OPTIONS[0]?.id, REMOVED]} />);

    expect(screen.getByText("Lendo, Opção removida")).toBeInTheDocument();
  });

  it("shows a missing value as a dash and a boolean in words", () => {
    const { rerender } = render(<PropertyValueDisplay definition={{ name: "Nota", valueKind: "number" }} value={null} />);
    expect(screen.getByText("—")).toBeInTheDocument();

    rerender(<PropertyValueDisplay definition={{ name: "Lido", valueKind: "boolean" }} value={true} />);
    expect(screen.getByText("Sim")).toBeInTheDocument();
  });

  it("links only http and https addresses", () => {
    render(<PropertyValueDisplay definition={{ name: "Site", valueKind: "url" }} value="https://example.test/" />);

    expect(screen.getByRole("link", { name: "https://example.test/" })).toHaveAttribute("rel", "noopener noreferrer");
  });
});
