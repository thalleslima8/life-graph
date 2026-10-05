import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { TEXT_VALUE_MAX_LENGTH, URL_VALUE_MAX_LENGTH, type PropertyField, type PropertyValue } from "./propertyValues";

/**
 * One Property value in a form (DA-118): a controlled component for React Hook Form's
 * `Controller`, owning the label, the error and the `aria-*` for the 8 value kinds (FE-033).
 */

type Props = {
  id: string;
  definition: PropertyField;
  value: PropertyValue;
  onChange(value: PropertyValue): void;
  onBlur?(): void;
  error?: string;
  disabled?: boolean;
};

const REMOVED_OPTION_LABEL = "(opção removida)";

export function PropertyValueEditor({ id, definition, value, onChange, onBlur, error, disabled }: Props) {
  const errorId = `${id}-error`;
  const invalid = error ? true : undefined;
  const describedBy = error ? errorId : undefined;
  const errorMessage = error && (
    <p id={errorId} role="alert" className="text-sm text-destructive">
      {error}
    </p>
  );

  if (definition.valueKind === "boolean") {
    return (
      <fieldset aria-describedby={describedBy} aria-invalid={invalid} className="space-y-2" disabled={disabled}>
        <legend className="text-sm font-medium">{definition.name}</legend>
        <div className="flex items-center gap-4 text-sm">
          {[
            { label: "Sim", choice: true },
            { label: "Não", choice: false },
          ].map(({ label, choice }) => (
            <label key={label} className="flex items-center gap-2">
              <input type="radio" name={id} checked={value === choice} onChange={() => onChange(choice)} onBlur={onBlur} />
              {label}
            </label>
          ))}
          {value !== null && (
            <Button type="button" variant="ghost" size="sm" onClick={() => onChange(null)} aria-label={`Limpar ${definition.name}`}>
              Limpar
            </Button>
          )}
        </div>
        {errorMessage}
      </fieldset>
    );
  }

  if (definition.valueKind === "multi_select") {
    const chosen = Array.isArray(value) ? value : [];
    const options = definition.options ?? [];
    const removed = chosen.filter((choice) => !options.some((option) => option.id === choice));
    const toggle = (optionId: string, checked: boolean) =>
      onChange(checked ? [...chosen, optionId] : chosen.filter((choice) => choice !== optionId));

    return (
      <fieldset aria-describedby={describedBy} aria-invalid={invalid} className="space-y-2" disabled={disabled}>
        <legend className="text-sm font-medium">{definition.name}</legend>
        <div className="flex flex-wrap gap-x-4 gap-y-2 text-sm">
          {options.map((option) => (
            <label key={option.id} className="flex items-center gap-2">
              <input
                type="checkbox"
                checked={chosen.includes(option.id)}
                onChange={(event) => toggle(option.id, event.target.checked)}
                onBlur={onBlur}
              />
              {option.label}
            </label>
          ))}
          {/* A removed option stays visible and can only be unchecked: the value is never dropped quietly. */}
          {removed.map((optionId) => (
            <label key={optionId} className="flex items-center gap-2 text-muted-foreground">
              <input type="checkbox" checked onChange={() => toggle(optionId, false)} onBlur={onBlur} />
              {REMOVED_OPTION_LABEL}
            </label>
          ))}
        </div>
        {errorMessage}
      </fieldset>
    );
  }

  if (definition.valueKind === "select") {
    const options = definition.options ?? [];
    const selected = typeof value === "string" ? value : "";
    const isRemoved = selected !== "" && !options.some((option) => option.id === selected);

    return (
      <div className="space-y-2">
        <Label htmlFor={id}>{definition.name}</Label>
        <div className="flex items-center gap-2">
          <Select value={selected} onValueChange={(next) => onChange(next)} disabled={disabled}>
            <SelectTrigger id={id} aria-invalid={invalid} aria-describedby={describedBy} onBlur={onBlur}>
              <SelectValue placeholder="Escolha uma opção" />
            </SelectTrigger>
            <SelectContent>
              {options.map((option) => (
                <SelectItem key={option.id} value={option.id}>
                  {option.label}
                </SelectItem>
              ))}
              {isRemoved && (
                <SelectItem value={selected} disabled>
                  {REMOVED_OPTION_LABEL}
                </SelectItem>
              )}
            </SelectContent>
          </Select>
          {selected !== "" && (
            <Button type="button" variant="ghost" size="sm" onClick={() => onChange(null)} aria-label={`Limpar ${definition.name}`} disabled={disabled}>
              Limpar
            </Button>
          )}
        </div>
        {errorMessage}
      </div>
    );
  }

  const common = {
    id,
    onBlur,
    disabled,
    "aria-invalid": invalid,
    "aria-describedby": describedBy,
  };

  return (
    <div className="space-y-2">
      <Label htmlFor={id}>{definition.name}</Label>
      {definition.valueKind === "text" && (
        <Textarea
          {...common}
          rows={2}
          maxLength={TEXT_VALUE_MAX_LENGTH}
          value={typeof value === "string" ? value : ""}
          onChange={(event) => onChange(event.target.value)}
        />
      )}
      {definition.valueKind === "number" && (
        <Input
          {...common}
          type="number"
          inputMode="decimal"
          step="any"
          value={typeof value === "number" ? value : ""}
          onChange={(event) => onChange(event.target.value === "" ? null : Number(event.target.value))}
        />
      )}
      {(definition.valueKind === "date" || definition.valueKind === "date_time" || definition.valueKind === "url") && (
        <Input
          {...common}
          type={definition.valueKind === "date" ? "date" : definition.valueKind === "date_time" ? "datetime-local" : "url"}
          maxLength={definition.valueKind === "url" ? URL_VALUE_MAX_LENGTH : undefined}
          value={typeof value === "string" ? value : ""}
          onChange={(event) => onChange(event.target.value)}
        />
      )}
      {errorMessage}
    </div>
  );
}

const dateFormat = new Intl.DateTimeFormat("pt-BR", { dateStyle: "medium" });
const dateTimeFormat = new Intl.DateTimeFormat("pt-BR", { dateStyle: "medium", timeStyle: "short" });
const numberFormat = new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 20 });

const REMOVED_OPTION_DISPLAY = "Opção removida";

function optionLabel(definition: PropertyField, optionId: unknown): string {
  return definition.options?.find((option) => option.id === optionId)?.label ?? REMOVED_OPTION_DISPLAY;
}

/** A stored value as text, for Outras propriedades and read-only places (FE-062: formatted by locale). */
export function PropertyValueDisplay({ definition, value }: { definition: PropertyField; value: unknown }) {
  if (value === null || value === undefined || value === "" || (Array.isArray(value) && value.length === 0)) {
    return <span className="text-muted-foreground">—</span>;
  }

  switch (definition.valueKind) {
    case "number":
      return <span>{typeof value === "number" ? numberFormat.format(value) : String(value)}</span>;
    case "boolean":
      return <span>{value === true ? "Sim" : "Não"}</span>;
    case "date": {
      const [year, month, day] = String(value).split("-").map(Number);
      const date = year && month && day ? new Date(year, month - 1, day) : undefined;
      return <span>{date ? dateFormat.format(date) : String(value)}</span>;
    }
    case "date_time": {
      const instant = new Date(String(value));
      return <span>{Number.isNaN(instant.getTime()) ? String(value) : dateTimeFormat.format(instant)}</span>;
    }
    case "url": {
      const url = String(value);
      return /^https?:\/\//i.test(url) ? (
        <a href={url} target="_blank" rel="noopener noreferrer" className="underline underline-offset-4 break-all">
          {url}
        </a>
      ) : (
        <span className="break-all">{url}</span>
      );
    }
    case "select":
      return <span>{optionLabel(definition, value)}</span>;
    case "multi_select":
      return <span>{(Array.isArray(value) ? value : [value]).map((choice) => optionLabel(definition, choice)).join(", ")}</span>;
    default:
      return <span className="whitespace-pre-wrap">{String(value)}</span>;
  }
}
