import { describe, expect, it } from "vitest";
import type { ProposedOperation } from "./agentChanges";
import { anOperation } from "../../test/graphFixtures";
import { fieldLabel, operationTitle } from "./agentChangeMessages";

function capture(overrides: Partial<ProposedOperation> = {}): ProposedOperation {
  return {
    index: 0,
    op: "capture_resource",
    isSensitive: false,
    isRejected: false,
    dependsOn: [],
    fields: [
      { field: "title", before: null, after: "The paper", overwritesYourValue: false },
      { field: "url", before: null, after: "https://example.test/paper.pdf", overwritesYourValue: false },
    ],
    ...overrides,
  } as ProposedOperation;
}

describe("operationTitle of a Tag change", () => {
  it("names the Tag and the Node it is applied to", () => {
    expect(operationTitle(anOperation({ op: "tag_node", nodeTitle: "Duna", tag: "Ficção", fields: [] }))).toBe("Aplicar Tag «Ficção» em Node «Duna»");
  });

  it("names the Tag and the Node it is removed from", () => {
    expect(operationTitle(anOperation({ op: "untag_node", nodeTitle: "Duna", tag: "Ficção", fields: [] }))).toBe("Remover Tag «Ficção» de Node «Duna»");
  });
});

describe("operationTitle of a capture", () => {
  it("names the Resource by its title", () => {
    expect(operationTitle(capture())).toBe("Capturar Resource «The paper»");
  });

  it("names the related Node and the kind when the capture relates it", () => {
    const related = capture({ target: { nodeId: "n1", ref: null, title: "Meditations" }, kind: "about" });

    expect(operationTitle(related)).toContain("→");
    expect(operationTitle(related)).toContain("(about)");
  });

  it("labels the link field", () => {
    expect(fieldLabel("url")).toBe("Link");
  });
});
