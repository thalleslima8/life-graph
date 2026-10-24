import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { axe } from "vitest-axe";
import { aChangeSetDetail, anOperation, aProposalDetail, NODE_ID } from "@/test/graphFixtures";
import type { ProposalDetail } from "./agentChanges";
import { ChangeDiff } from "./ChangeDiff";

// 0: create X (ref x), 1: relate x → Leituras, 2: edit Leituras.
const withDependency = aProposalDetail([
  anOperation({ index: 0, op: "create_node", nodeId: null, nodeTitle: null, expectedVersion: null, currentVersion: null, ref: "x", fields: [{ field: "title", before: null, after: "Novo", overwritesYourValue: false }] }),
  anOperation({
    index: 1,
    op: "create_relation",
    nodeId: null,
    nodeTitle: null,
    expectedVersion: null,
    currentVersion: null,
    source: { nodeId: null, ref: "x", title: null },
    target: { nodeId: NODE_ID, ref: null, title: "Leituras" },
    kind: "sobre",
    fields: [],
    dependsOn: [0],
  }),
  anOperation({ index: 2 }),
]);

function Selectable({ detail, onChange }: { detail: ProposalDetail; onChange: (next: Set<number>) => void }) {
  const [selected, setSelected] = useState(() => new Set([0, 1, 2]));
  return (
    <ChangeDiff
      source={{ kind: "proposal", detail }}
      selection={{
        selected,
        onChange: (next) => {
          setSelected(next);
          onChange(next);
        },
      }}
    />
  );
}

describe("ChangeDiff", () => {
  it("shows each operation as a section with Antes/Depois and the overwrite warning in words", async () => {
    const { container } = render(<ChangeDiff source={{ kind: "proposal", detail: aProposalDetail() }} />);

    const operation = screen.getByRole("region", { name: "Operação 1: Alterar Node «Leituras»" });
    expect(within(operation).getByText("Atenção: sobrescreveu um valor seu")).toBeInTheDocument();
    // FE-063: by role, as a screen reader meets the description list of the field.
    expect(within(operation).getAllByRole("term").map((term) => term.textContent)).toEqual(["Antes", "Depois"]);
    expect(within(operation).getAllByRole("definition").map((definition) => definition.textContent)).toEqual(["Leituras", "Leituras de 2026"]);
    expect(within(operation).queryByRole("checkbox")).not.toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();
  });

  it("unticking a new node also unticks the relation that uses it; ticking the relation ticks the node back", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    const { container } = render(<Selectable detail={withDependency} onChange={onChange} />);

    await user.click(screen.getByRole("checkbox", { name: "Operação 1: Criar Node «Novo»" }));
    expect(onChange).toHaveBeenLastCalledWith(new Set([2]));
    expect(screen.getByRole("checkbox", { name: /Operação 2: Relacionar o novo Node «x» → «Leituras» \(sobre\)/ })).not.toBeChecked();

    await user.click(screen.getByRole("checkbox", { name: /Operação 2/ }));
    expect(onChange).toHaveBeenLastCalledWith(new Set([2, 1, 0]));
    expect(screen.getByText("Depende da operação 1")).toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();
  });

  it("announces the operations the server refused by text", () => {
    render(<ChangeDiff source={{ kind: "proposal", detail: withDependency }} highlight={[1]} />);

    expect(within(screen.getByRole("region", { name: /Operação 2/ })).getByText("Esta operação impediu a aprovação.")).toBeInTheDocument();
    expect(within(screen.getByRole("region", { name: /Operação 1/ })).queryByText("Esta operação impediu a aprovação.")).not.toBeInTheDocument();
  });

  it("says when the agent's Node changed since it read it, or is no longer there", () => {
    render(
      <ChangeDiff
        source={{
          kind: "proposal",
          detail: aProposalDetail([
            anOperation({ index: 0, expectedVersion: 1, currentVersion: 2 }),
            anOperation({ index: 1, expectedVersion: 1, currentVersion: null, nodeTitle: null, fields: [] }),
            anOperation({ index: 2, expectedVersion: 1, currentVersion: 1 }),
          ]),
        }}
      />,
    );

    const changed = screen.getByRole("region", { name: /Operação 1/ });
    const gone = screen.getByRole("region", { name: /Operação 2/ });
    const unchanged = screen.getByRole("region", { name: /Operação 3/ });
    expect(within(changed).getByText("Mudou desde que o agente leu")).toBeInTheDocument();
    expect(within(changed).queryByText("O Node não está mais disponível")).not.toBeInTheDocument();
    expect(within(gone).getByText("O Node não está mais disponível")).toBeInTheDocument();
    expect(within(gone).queryByText("Mudou desde que o agente leu")).not.toBeInTheDocument();
    expect(within(unchanged).queryByText("Mudou desde que o agente leu")).not.toBeInTheDocument();
    expect(within(unchanged).queryByText("O Node não está mais disponível")).not.toBeInTheDocument();
  });

  it("marks the operations the person left out, and shows the Type the agent suggested", () => {
    render(
      <ChangeDiff
        source={{
          kind: "proposal",
          detail: aProposalDetail([
            anOperation({ index: 0, isRejected: true }),
            anOperation({
              index: 1,
              op: "create_node",
              nodeId: null,
              nodeTitle: null,
              expectedVersion: null,
              currentVersion: null,
              suggestedType: "Pessoa",
              fields: [{ field: "title", before: null, after: "Sêneca", overwritesYourValue: false }],
            }),
          ]),
        }}
      />,
    );

    const rejected = screen.getByRole("region", { name: /Operação 1/ });
    const created = screen.getByRole("region", { name: /Operação 2/ });
    expect(within(rejected).getByText("Recusada")).toBeInTheDocument();
    expect(within(created).queryByText("Recusada")).not.toBeInTheDocument();
    expect(within(created).getByText("Type sugerido pelo agente: Pessoa")).toBeInTheDocument();
    expect(within(rejected).queryByText(/Type sugerido pelo agente/)).not.toBeInTheDocument();
  });

  it("says when purge removed the content", () => {
    const { rerender } = render(<ChangeDiff source={{ kind: "proposal", detail: aProposalDetail([], { contentPurged: true }) }} />);
    expect(screen.getByText(/conteúdo removido por purge/)).toBeInTheDocument();

    const purged = aChangeSetDetail();
    purged.entries = purged.entries.map((entry) => ({ ...entry, isPurged: true, label: null, fields: [] }));
    rerender(<ChangeDiff source={{ kind: "changeset", detail: purged }} />);
    expect(screen.getByText("conteúdo removido por purge")).toBeInTheDocument();
  });

  it("shows a GraphChangeSet's entries field by field", () => {
    render(<ChangeDiff source={{ kind: "changeset", detail: aChangeSetDetail() }} />);

    const entry = screen.getByRole("region", { name: "Alterou Node «Leituras de 2026»" });
    expect(within(entry).getByText("Título")).toBeInTheDocument();
    expect(within(entry).getByText("Atenção: sobrescreveu um valor seu")).toBeInTheDocument();
  });
});
