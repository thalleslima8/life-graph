import * as PopoverPrimitive from "@radix-ui/react-popover";
import { useId, useState, type ComponentProps, type KeyboardEvent } from "react";
import { cn } from "@/lib/utils";

export type ComboboxOption = { value: string; label: string };

type ComboboxProps = Omit<ComponentProps<"input">, "value" | "onChange" | "onSelect" | "role"> & {
  /** The typed text; the caller filters `options` by it. */
  inputValue: string;
  onInputChange: (value: string) => void;
  options: ComboboxOption[];
  onSelect: (option: ComboboxOption) => void;
  /** The accessible name of the list of options. */
  listLabel: string;
};

/**
 * A text field with a list of suggestions (WAI-ARIA combobox with listbox): ↑ and ↓ move
 * through the options, Enter picks one, Esc closes the list. Focus stays in the field.
 */
export function Combobox({ inputValue, onInputChange, options, onSelect, listLabel, className, disabled, onKeyDown, ...inputProps }: ComboboxProps) {
  const listId = useId();
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(0);
  const expanded = open && !disabled && options.length > 0;
  const active = expanded ? options[Math.min(activeIndex, options.length - 1)] : undefined;
  const optionId = (index: number) => `${listId}-option-${index}`;

  const pick = (option: ComboboxOption) => {
    onSelect(option);
    setOpen(false);
    setActiveIndex(0);
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    onKeyDown?.(event);
    if (event.defaultPrevented) {
      return;
    }

    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      if (!expanded) {
        setOpen(true);
        setActiveIndex(0);
        return;
      }

      const step = event.key === "ArrowDown" ? 1 : -1;
      setActiveIndex((current) => (Math.min(current, options.length - 1) + step + options.length) % options.length);
    } else if (event.key === "Enter" && active) {
      event.preventDefault();
      pick(active);
    } else if (event.key === "Escape" && expanded) {
      event.preventDefault();
      setOpen(false);
    }
  };

  return (
    <PopoverPrimitive.Root open={expanded} onOpenChange={setOpen}>
      <PopoverPrimitive.Anchor asChild>
        <input
          type="text"
          role="combobox"
          autoComplete="off"
          aria-autocomplete="list"
          aria-expanded={expanded}
          aria-controls={expanded ? listId : undefined}
          aria-activedescendant={active ? optionId(options.indexOf(active)) : undefined}
          value={inputValue}
          disabled={disabled}
          onChange={(event) => {
            onInputChange(event.target.value);
            setOpen(true);
            setActiveIndex(0);
          }}
          onFocus={() => setOpen(true)}
          onKeyDown={handleKeyDown}
          className={cn(
            "flex h-9 w-full rounded-md border border-input bg-transparent px-3 py-1 text-base shadow-xs transition-colors placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50 aria-invalid:border-destructive md:text-sm",
            className,
          )}
          {...inputProps}
        />
      </PopoverPrimitive.Anchor>
      <PopoverPrimitive.Portal>
        <PopoverPrimitive.Content
          align="start"
          sideOffset={4}
          // The field keeps focus; typing goes on while the list is open.
          onOpenAutoFocus={(event) => event.preventDefault()}
          onCloseAutoFocus={(event) => event.preventDefault()}
          onInteractOutside={(event) => {
            if (event.target instanceof Element && event.target.closest("[role=combobox]")) {
              event.preventDefault();
            }
          }}
          className="z-50 max-h-72 min-w-[var(--radix-popover-trigger-width)] overflow-y-auto rounded-md border bg-popover p-1 text-popover-foreground shadow-md"
        >
          <ul id={listId} role="listbox" aria-label={listLabel}>
            {options.map((option, index) => (
              <li
                key={option.value}
                id={optionId(index)}
                role="option"
                aria-selected={option === active}
                // Keeps focus in the field: a click picks without blurring it.
                onMouseDown={(event) => event.preventDefault()}
                onClick={() => pick(option)}
                className={cn("cursor-default select-none rounded-sm px-2 py-1.5 text-sm", option === active && "bg-accent text-accent-foreground ring-1 ring-ring")}
              >
                {option.label}
              </li>
            ))}
          </ul>
        </PopoverPrimitive.Content>
      </PopoverPrimitive.Portal>
    </PopoverPrimitive.Root>
  );
}
