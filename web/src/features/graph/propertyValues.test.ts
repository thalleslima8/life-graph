import { describe, expect, it } from "vitest";
import { fromWire, propertyValueSchema, toWire } from "./propertyValues";

const OPTIONS = [
  { id: "01920000-0000-7000-8000-0000000000e1", label: "Lendo" },
  { id: "01920000-0000-7000-8000-0000000000e2", label: "Lido" },
];
const [READING, READ] = OPTIONS.map((option) => option.id) as [string, string];

describe("toWire and fromWire", () => {
  it.each([
    ["text", "Uma nota", "Uma nota"],
    ["number", 4.5, 4.5],
    ["boolean", false, false],
    ["date", "2026-10-05", "2026-10-05"],
    ["url", "https://example.test/a", "https://example.test/a"],
    ["select", READING, READING],
    ["multi_select", [READING, READ], [READING, READ]],
  ] as const)("round-trips a %s value", (kind, formValue, wire) => {
    expect(toWire(kind, formValue as never)).toEqual(wire);
    expect(fromWire(kind, wire)).toEqual(formValue);
  });

  it("sends a local date-time as an instant with an offset, and reads it back in the local zone", () => {
    const wire = toWire("date_time", "2026-10-05T14:30");

    expect(wire).toBe(new Date("2026-10-05T14:30").toISOString());
    expect(fromWire("date_time", wire)).toBe("2026-10-05T14:30");
  });

  it.each([
    ["text", ""],
    ["url", ""],
    ["date", ""],
    ["date_time", ""],
    ["number", null],
    ["boolean", null],
    ["select", null],
    ["multi_select", []],
  ] as const)("sends an empty %s as null, which removes the value", (kind, empty) => {
    expect(toWire(kind, empty as never)).toBeNull();
  });

  it("reads a stored value that does not fit the kind as empty", () => {
    expect(fromWire("number", "4")).toBeNull();
    expect(fromWire("boolean", "true")).toBeNull();
    expect(fromWire("text", null)).toBe("");
    expect(fromWire("multi_select", null)).toEqual([]);
  });
});

describe("propertyValueSchema", () => {
  it.each([
    ["date", "2026-02-30"],
    ["date", "05/10/2026"],
    ["url", "ftp://example.test"],
    ["url", "example.test"],
    ["text", "x".repeat(10_001)],
  ] as const)("refuses %s %s, as the server would", (kind, value) => {
    expect(propertyValueSchema(kind).safeParse(value).success).toBe(false);
  });

  it.each([
    ["date", "2026-02-28"],
    ["date", ""],
    ["url", "http://example.test"],
    ["number", null],
    ["boolean", true],
  ] as const)("accepts %s %s", (kind, value) => {
    expect(propertyValueSchema(kind).safeParse(value).success).toBe(true);
  });

  it("refuses a removed option until it is cleared", () => {
    const removed = "01920000-0000-7000-8000-0000000000ef";

    expect(propertyValueSchema("select", OPTIONS).safeParse(removed).success).toBe(false);
    expect(propertyValueSchema("select", OPTIONS).safeParse(null).success).toBe(true);
    expect(propertyValueSchema("multi_select", OPTIONS).safeParse([READING, removed]).success).toBe(false);
    expect(propertyValueSchema("multi_select", OPTIONS).safeParse([READING, READING]).success).toBe(false);
  });
});
