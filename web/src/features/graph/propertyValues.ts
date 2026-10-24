import { z } from "zod";
import type { PropertyValueKind, SelectOption } from "./graphData";

/**
 * Property values between the form and the wire (DA-023, DA-118). The checks copy
 * `PropertyDefinition.Normalize` on the server, which always revalidates (FE-020).
 */

/** What a form holds: dates as `yyyy-MM-dd`, date-times as local `yyyy-MM-ddTHH:mm`, choices by option id. */
export type PropertyValue = string | number | boolean | string[] | null;

export type PropertyField = { name: string; valueKind: PropertyValueKind; options?: SelectOption[] };

// Mirrors GraphLimits on the server.
export const TEXT_VALUE_MAX_LENGTH = 10_000;
export const URL_VALUE_MAX_LENGTH = 2_048;

const DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;
const ABSOLUTE_HTTP_URL = /^https?:\/\//i;

const emptyText = z.literal("");

function isValidDate(text: string): boolean {
  if (!DATE_PATTERN.test(text)) {
    return false;
  }

  const [year, month, day] = text.split("-").map(Number) as [number, number, number];
  const date = new Date(Date.UTC(year, month - 1, day));
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
}

function isHttpUrl(text: string): boolean {
  if (!ABSOLUTE_HTTP_URL.test(text)) {
    return false;
  }

  try {
    new URL(text);
    return true;
  } catch {
    return false;
  }
}

const REMOVED_OPTION_MESSAGE = "Essa opção foi removida. Limpe ou escolha outra.";

/** The client-side check of one value; an empty value is always allowed (it removes the value). */
export function propertyValueSchema(kind: PropertyValueKind, options: SelectOption[] = []): z.ZodType<PropertyValue, PropertyValue> {
  const optionIds = new Set(options.map((option) => option.id));
  switch (kind) {
    case "text":
      return z.string().max(TEXT_VALUE_MAX_LENGTH, `Use no máximo ${TEXT_VALUE_MAX_LENGTH} caracteres.`);
    case "number":
      return z.number({ error: "Informe um número." }).finite("Informe um número.").nullable();
    case "boolean":
      return z.boolean().nullable();
    case "date":
      return z.union([emptyText, z.string().refine(isValidDate, "Informe uma data válida.")]);
    case "date_time":
      return z.union([emptyText, z.string().refine((text) => !Number.isNaN(new Date(text).getTime()), "Informe data e hora válidas.")]);
    case "url":
      return z.union([
        emptyText,
        z
          .string()
          .max(URL_VALUE_MAX_LENGTH, `Use no máximo ${URL_VALUE_MAX_LENGTH} caracteres.`)
          .refine(isHttpUrl, "Informe um endereço http ou https."),
      ]);
    case "select":
      return z
        .string()
        .refine((id) => optionIds.has(id), REMOVED_OPTION_MESSAGE)
        .nullable();
    case "multi_select":
      return z
        .array(z.string())
        .refine((ids) => ids.every((id) => optionIds.has(id)), REMOVED_OPTION_MESSAGE)
        .refine((ids) => new Set(ids).size === ids.length, "Escolha cada opção uma vez só.");
  }
}

function pad(value: number): string {
  return String(value).padStart(2, "0");
}

/** An instant as `<input type="datetime-local">` shows it, in the browser's zone. */
function toLocalDateTime(instant: Date): string {
  return `${instant.getFullYear()}-${pad(instant.getMonth() + 1)}-${pad(instant.getDate())}T${pad(instant.getHours())}:${pad(instant.getMinutes())}`;
}

/** The JSON to send; `null` removes the value. */
export function toWire(kind: PropertyValueKind, value: PropertyValue): unknown {
  if (value === null || value === "") {
    return null;
  }

  switch (kind) {
    case "date_time":
      // The local reading becomes an instant with an offset, which the server requires.
      return typeof value === "string" ? new Date(value).toISOString() : null;
    case "multi_select":
      return Array.isArray(value) && value.length > 0 ? value : null;
    default:
      return value;
  }
}

/** What a form holds for a stored value; anything that does not fit the kind reads as empty. */
export function fromWire(kind: PropertyValueKind, json: unknown): PropertyValue {
  switch (kind) {
    case "text":
    case "url":
    case "date":
      return typeof json === "string" ? json : emptyValueOf(kind);
    case "date_time": {
      const instant = typeof json === "string" ? new Date(json) : undefined;
      return instant && !Number.isNaN(instant.getTime()) ? toLocalDateTime(instant) : "";
    }
    case "number":
      return typeof json === "number" ? json : null;
    case "boolean":
      return typeof json === "boolean" ? json : null;
    case "select":
      return typeof json === "string" ? json : null;
    case "multi_select":
      return Array.isArray(json) ? json.filter((item): item is string => typeof item === "string") : [];
  }
}

/** The value of an untouched field of this kind. */
export function emptyValueOf(kind: PropertyValueKind): PropertyValue {
  switch (kind) {
    case "text":
    case "url":
    case "date":
    case "date_time":
      return "";
    case "multi_select":
      return [];
    default:
      return null;
  }
}
