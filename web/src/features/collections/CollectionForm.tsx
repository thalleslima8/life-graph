import { zodResolver } from "@hookform/resolvers/zod";
import { Controller, useForm, useWatch } from "react-hook-form";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { writeErrorMessage } from "@/features/graph/graphMessages";
import { NAME_MAX_LENGTH } from "@/features/graph/graphData";
import { CollectionCriteriaEditor } from "./CollectionCriteriaEditor";
import { NAV_FULL_HINT } from "./collectionMessages";
import { collectionFormSchema, type CollectionFormInput, type CollectionInput, type CollectionReviewItem } from "./collectionsData";

type Props = {
  idPrefix: string;
  label: string;
  submitLabel: string;
  initial?: CollectionFormInput;
  /** Editing: a manual Collection stays manual and a dynamic one stays dynamic (DA-164). */
  kindFixed?: boolean;
  /** The menu already shows 8 pinned Collections (DA-173). */
  navFull?: boolean;
  invalid?: CollectionReviewItem[] | null;
  action: { isPending: boolean; error: unknown; submit(input: CollectionInput, onDone: () => void): void };
};

const EMPTY: CollectionFormInput = { name: "", pinned: false };
const FIRST_CRITERIA: NonNullable<CollectionFormInput["criteria"]> = { version: 1, match: "all", criteria: [{ type: "type", typeId: "" }] };

/** Name, pinned and, for a dynamic Collection, its criteria. */
export function CollectionForm({
  idPrefix,
  label,
  submitLabel,
  initial = EMPTY,
  kindFixed = false,
  navFull = false,
  invalid,
  action,
}: Props) {
  const {
    control,
    register,
    handleSubmit,
    reset,
    setValue,
    formState: { errors },
  } = useForm<CollectionFormInput, unknown, CollectionInput>({ resolver: zodResolver(collectionFormSchema), defaultValues: initial });
  const criteria = useWatch({ control, name: "criteria" });
  const pinned = useWatch({ control, name: "pinned" });
  const criteriaError =
    errors.criteria?.criteria?.message ??
    errors.criteria?.criteria?.root?.message ??
    (errors.criteria?.criteria ? "Complete cada critério." : undefined);

  const onSubmit = handleSubmit((input) => {
    if (!action.isPending) {
      action.submit(input, () => reset(initial));
    }
  });

  return (
    <form noValidate onSubmit={onSubmit} aria-label={label} className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor={`${idPrefix}-name`}>Nome</Label>
        <Input
          id={`${idPrefix}-name`}
          maxLength={NAME_MAX_LENGTH}
          aria-invalid={errors.name ? true : undefined}
          aria-describedby={errors.name ? `${idPrefix}-name-error` : undefined}
          {...register("name")}
        />
        {errors.name && (
          <p id={`${idPrefix}-name-error`} role="alert" className="text-sm text-destructive">
            {errors.name.message}
          </p>
        )}
      </div>
      {!kindFixed && (
        <fieldset className="space-y-1">
          <legend className="text-sm font-medium">Tipo de Collection</legend>
          <label className="flex items-center gap-2 text-sm">
            <input
              type="radio"
              name={`${idPrefix}-kind`}
              checked={criteria === undefined}
              onChange={() => setValue("criteria", undefined)}
            />
            Manual: você escolhe os Nodes
          </label>
          <label className="flex items-center gap-2 text-sm">
            <input
              type="radio"
              name={`${idPrefix}-kind`}
              checked={criteria !== undefined}
              onChange={() => setValue("criteria", FIRST_CRITERIA)}
            />
            Dinâmica: os Nodes que atendem aos critérios
          </label>
        </fieldset>
      )}
      {criteria !== undefined && (
        <div className="space-y-2">
          <Controller
            name="criteria"
            control={control}
            render={({ field }) => (
              <CollectionCriteriaEditor value={field.value ?? FIRST_CRITERIA} onChange={field.onChange} invalid={invalid} />
            )}
          />
          {criteriaError && (
            <p role="alert" className="text-sm text-destructive">
              {criteriaError}
            </p>
          )}
        </div>
      )}
      <div className="space-y-1">
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" aria-describedby={navFull && !pinned ? `${idPrefix}-pin-hint` : undefined} {...register("pinned")} />
          Fixar no menu
        </label>
        {navFull && (
          <p id={`${idPrefix}-pin-hint`} className="text-sm text-muted-foreground">
            {NAV_FULL_HINT}
          </p>
        )}
      </div>
      <Button type="submit" disabled={action.isPending}>
        {submitLabel}
      </Button>
      {action.error !== null && action.error !== undefined && (
        <p role="alert" className="text-sm text-destructive">
          {writeErrorMessage(action.error)}
        </p>
      )}
    </form>
  );
}
