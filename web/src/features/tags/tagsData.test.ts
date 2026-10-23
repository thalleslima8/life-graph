import { describe, expect, it } from "vitest";
import { createdTagIdOf, tagKeyOf, tagNameSchema } from "./tagsData";

describe("tagNameSchema", () => {
  it("trims, drops the leading # and collapses inner spaces, as the server does", () => {
    expect(tagNameSchema.parse("  #  leitura   lenta  ")).toBe("leitura lenta");
  });

  it.each([
    ["", "Informe um nome."],
    ["#", "Informe um nome."],
    ["a#b", "Use # só no início."],
    ["x".repeat(51), "Use no máximo 50 caracteres."],
  ])("refuses %j", (name, message) => {
    const result = tagNameSchema.safeParse(name);
    expect(result.success).toBe(false);
    expect(result.error?.issues[0]?.message).toBe(message);
  });

  it("accepts exactly 50 characters", () => {
    expect(tagNameSchema.safeParse("x".repeat(50)).success).toBe(true);
  });
});

describe("tagKeyOf", () => {
  it("folds case and accents, composed or not", () => {
    expect(tagKeyOf("Ação")).toBe("acao");
    expect(tagKeyOf("Água")).toBe(tagKeyOf("água"));
  });
});

describe("createdTagIdOf", () => {
  it("reads the created Tag from the receipt", () => {
    expect(
      createdTagIdOf({
        changeSetId: "cs",
        changes: [
          { kind: "node_tag", entityId: "link", operation: "created", version: null },
          { kind: "tag", entityId: "tag", operation: "created", version: null },
        ],
      }),
    ).toBe("tag");
  });
});
