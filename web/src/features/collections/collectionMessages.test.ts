import { describe, expect, it } from "vitest";
import { reviewText } from "./collectionMessages";

describe("reviewText", () => {
  it("names the criterion and why it needs review, numbered from 1 (DA-171)", () => {
    expect(reviewText({ criterionIndex: 0, reason: "option_missing" })).toBe("Critério 1: a opção não existe mais.");
    expect(reviewText({ criterionIndex: 2, reason: "value_not_valid_for_kind" })).toBe(
      "Critério 3: o valor não vale mais para o tipo da propriedade.",
    );
  });

  it("falls back to a generic reason the client does not know", () => {
    expect(reviewText({ criterionIndex: 1, reason: "something_new" })).toBe("Critério 2: não pode ser avaliado.");
  });
});
