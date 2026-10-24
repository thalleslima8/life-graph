import { zodResolver } from "@hookform/resolvers/zod";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Controller, useFieldArray, useForm, useWatch } from "react-hook-form";
import { z } from "zod";
import { isApiError } from "@/api/client";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { Dialog, DialogClose, DialogContent } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import {
  ALLOWED_KIND_TRANSITIONS,
  holdsValues,
  isPropertyUsageChanged,
  NAME_MAX_LENGTH,
  propertyDefinitionUsageQuery,
  SELECT_OPTION_LABEL_MAX_LENGTH,
  SELECT_OPTIONS_MAX_COUNT,
  useArchivePropertyDefinition,
  useCreatePropertyDefinition,
  useDeletePropertyDefinition,
  useUpdatePropertyDefinition,
  type PropertyDefinitionInput,
  type PropertyDefinitionItem,
  type PropertyDefinitionUsage,
  type PropertyValueKind,
} from "./graphData";
import { VALUE_KIND_LABELS, writeErrorMessage } from "./graphMessages";
import { RefusalOrError, SystemMark } from "./ontologyParts";

/**
 * The editor of Property Definitions (DA-065, DA-066, DA-165..DA-168): create and edit, Select
 * options with stable ids that can be archived and restored, archive and restore of the
 * definition, and delete of a loose one after confirming how many Nodes lose their value.
 */

const VALUE_KINDS = Object.keys(VALUE_KIND_LABELS) as PropertyValueKind[];

/** The refusals about the value kind itself, shown on the kind field. */
const KIND_REFUSAL_CODES = new Set(["graph.property_kind_change_blocked", "graph.property_rewrite_too_large"]);

function hasChoices(kind: PropertyValueKind): boolean {
  return kind === "select" || kind === "multi_select";
}

const countFormat = new Intl.NumberFormat("pt-BR");

function nodesCounted(count: number): string {
  return count === 1 ? "1 Node" : `${countFormat.format(count)} Nodes`;
}

/** "Usada em N Types" (DA-066): editing the definition changes it on all of them. */
function usedInTypesLabel(typeCount: number): string {
  if (typeCount === 0) {
    return "Em nenhum Type";
  }

  return typeCount === 1 ? "Usada em 1 Type" : `Usada em ${countFormat.format(typeCount)} Types`;
}

const optionSchema = z.object({
  optionId: z.string().optional(),
  label: z
    .string()
    .trim()
    .min(1, "Informe o rótulo da opção.")
    .max(SELECT_OPTION_LABEL_MAX_LENGTH, `Use no máximo ${SELECT_OPTION_LABEL_MAX_LENGTH} caracteres.`),
  archived: z.boolean(),
});

const definitionSchema = z
  .object({
    name: z.string().trim().min(1, "Informe um nome.").max(NAME_MAX_LENGTH, `Use no máximo ${NAME_MAX_LENGTH} caracteres.`),
    valueKind: z.enum(VALUE_KINDS as [PropertyValueKind, ...PropertyValueKind[]]),
    options: z.array(optionSchema).max(SELECT_OPTIONS_MAX_COUNT, `Use no máximo ${SELECT_OPTIONS_MAX_COUNT} opções.`),
  })
  .superRefine((values, context) => {
    if (!hasChoices(values.valueKind)) {
      return;
    }

    const seen = new Set<string>();
    values.options.forEach((option, index) => {
      const key = option.label.trim().toLocaleLowerCase("pt-BR");
      if (seen.has(key)) {
        context.addIssue({ code: "custom", path: ["options", index, "label"], message: "Esta opção já existe." });
      }

      seen.add(key);
    });
  });

type DefinitionValues = z.infer<typeof definitionSchema>;

function formValuesOf(definition: PropertyDefinitionItem | undefined): DefinitionValues {
  return {
    name: definition?.name ?? "",
    valueKind: definition?.valueKind ?? "text",
    options: (definition?.options ?? []).map((option) => ({
      optionId: option.id,
      label: option.label,
      archived: option.archived ?? false,
    })),
  };
}

function inputOf(values: DefinitionValues): PropertyDefinitionInput {
  return {
    name: values.name,
    valueKind: values.valueKind,
    options: hasChoices(values.valueKind)
      ? values.options.map((option) => ({
          ...(option.optionId ? { id: option.optionId } : {}),
          label: option.label,
          ...(option.archived ? { archived: true } : {}),
        }))
      : [],
  };
}

/** Values are known to be absent only once the usage says so; until then only the safe changes are offered. */
function isKnownEmpty(usage: PropertyDefinitionUsage | undefined): boolean {
  return usage !== undefined && !holdsValues(usage);
}

/** Creates a definition, or edits one when `definition` is given; its usage is read while the editor is open. */
export function PropertyDefinitionEditor({ definition, onDone }: { definition?: PropertyDefinitionItem; onDone?: () => void }) {
  const create = useCreatePropertyDefinition();
  const update = useUpdatePropertyDefinition(definition?.id ?? "");
  const usage = useQuery(propertyDefinitionUsageQuery(definition?.id ?? "", { enabled: definition !== undefined }));
  const action = definition ? update : create;
  const idPrefix = definition ? `definition-${definition.id}` : "new-definition";
  const [awaitingMidnightConfirmation, setAwaitingMidnightConfirmation] = useState<PropertyDefinitionInput | null>(null);
  const {
    control,
    register,
    handleSubmit,
    reset,
    setValue,
    formState: { errors },
  } = useForm<DefinitionValues>({ resolver: zodResolver(definitionSchema), defaultValues: formValuesOf(definition) });
  const { fields, append, remove } = useFieldArray({ control, name: "options" });
  const valueKind = useWatch({ control, name: "valueKind" });
  const options = useWatch({ control, name: "options" });

  const restricted = definition !== undefined && !isKnownEmpty(usage.data);
  const offeredKinds = definition && restricted ? [definition.valueKind, ...ALLOWED_KIND_TRANSITIONS[definition.valueKind]] : VALUE_KINDS;

  const save = (input: PropertyDefinitionInput) => {
    const onSuccess = () => {
      if (!definition) {
        reset();
      }

      onDone?.();
    };
    if (definition) {
      update.mutate(input, { onSuccess });
    } else {
      create.mutate(input, { onSuccess });
    }
  };

  const onSubmit = handleSubmit((values) => {
    if (action.isPending) {
      return;
    }

    const input = inputOf(values);
    // A date becomes midnight UTC (DA-165): the person confirms before the values are rewritten.
    if (definition?.valueKind === "date" && input.valueKind === "date_time" && !isKnownEmpty(usage.data)) {
      setAwaitingMidnightConfirmation(input);
      return;
    }

    save(input);
  });

  const kindRefusal =
    isApiError(action.error) && action.error.code !== undefined && KIND_REFUSAL_CODES.has(action.error.code) ? action.error : null;
  const kindErrorId = `${idPrefix}-kind-error`;
  const kindHintId = `${idPrefix}-kind-hint`;
  const kindDescribedBy =
    [restricted ? kindHintId : undefined, kindRefusal ? kindErrorId : undefined].filter(Boolean).join(" ") || undefined;
  const valueCount = usage.data ? usage.data.nodeCount + usage.data.tombstoneCount : undefined;

  return (
    <form
      noValidate
      onSubmit={onSubmit}
      aria-label={definition ? `Propriedade ${definition.name}` : "Criar propriedade"}
      className="space-y-3"
    >
      <div className="flex flex-wrap items-start gap-2">
        <div className="space-y-2">
          <Label htmlFor={`${idPrefix}-name`}>{definition ? "Nome" : "Nova propriedade"}</Label>
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
        <div className="w-48 space-y-2">
          <Label htmlFor={`${idPrefix}-kind`}>Tipo de valor</Label>
          <Controller
            name="valueKind"
            control={control}
            render={({ field }) => (
              <Select value={field.value} onValueChange={field.onChange}>
                <SelectTrigger
                  id={`${idPrefix}-kind`}
                  onBlur={field.onBlur}
                  aria-invalid={kindRefusal ? true : undefined}
                  aria-describedby={kindDescribedBy}
                >
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {offeredKinds.map((kind) => (
                    <SelectItem key={kind} value={kind}>
                      {VALUE_KIND_LABELS[kind]}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          />
          {restricted && (
            <p id={kindHintId} className="text-xs text-muted-foreground">
              {usage.data
                ? "Há Nodes com valores: só as mudanças que convertem sem perda."
                : "Só as mudanças que convertem sem perda enquanto a contagem não estiver disponível."}
            </p>
          )}
          {kindRefusal && (
            <p id={kindErrorId} role="alert" className="text-sm text-destructive">
              {writeErrorMessage(kindRefusal)}
            </p>
          )}
        </div>
      </div>

      {hasChoices(valueKind) && (
        <fieldset className="space-y-2">
          <legend className="text-sm font-medium">Opções</legend>
          {fields.length > 0 && (
            <ul className="space-y-2">
              {fields.map((field, index) => {
                const option = options[index] ?? field;
                const optionName = option.label.trim() || String(index + 1);
                const chosenBy = option.optionId ? (usage.data?.optionCounts[option.optionId] ?? 0) : 0;
                const inputId = `${idPrefix}-option-${index}`;
                const countId = `${inputId}-count`;
                const labelError = errors.options?.[index]?.label;
                return (
                  <li key={field.id} className="flex flex-wrap items-start gap-2">
                    <div className="space-y-1">
                      <Label htmlFor={inputId} className="text-xs">
                        Opção {index + 1}
                        {option.archived && " (arquivada)"}
                      </Label>
                      <Input
                        id={inputId}
                        maxLength={SELECT_OPTION_LABEL_MAX_LENGTH}
                        aria-invalid={labelError ? true : undefined}
                        aria-describedby={labelError ? `${inputId}-error` : undefined}
                        {...register(`options.${index}.label`)}
                      />
                      {labelError && (
                        <p id={`${inputId}-error`} role="alert" className="text-sm text-destructive">
                          {labelError.message}
                        </p>
                      )}
                      {chosenBy > 0 && (
                        <p id={countId} className="text-xs text-muted-foreground">
                          Escolhida por {nodesCounted(chosenBy)}, apagados incluídos: arquive em vez de remover.
                        </p>
                      )}
                    </div>
                    {option.optionId && (
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        className="mt-5"
                        onClick={() => setValue(`options.${index}.archived`, !option.archived, { shouldDirty: true })}
                        aria-label={`${option.archived ? "Restaurar" : "Arquivar"} opção ${optionName}`}
                      >
                        {option.archived ? "Restaurar" : "Arquivar"}
                      </Button>
                    )}
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      className="mt-5"
                      onClick={() => remove(index)}
                      disabled={chosenBy > 0}
                      aria-describedby={chosenBy > 0 ? countId : undefined}
                      aria-label={`Remover opção ${optionName}`}
                    >
                      Remover
                    </Button>
                  </li>
                );
              })}
            </ul>
          )}
          {errors.options?.message && (
            <p role="alert" className="text-sm text-destructive">
              {errors.options.message}
            </p>
          )}
          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={() => append({ label: "", archived: false })}
            disabled={fields.length >= SELECT_OPTIONS_MAX_COUNT}
          >
            Adicionar opção
          </Button>
        </fieldset>
      )}

      <Button type="submit" variant="secondary" disabled={action.isPending}>
        {definition ? "Salvar" : "Criar propriedade"}
      </Button>
      <RefusalOrError error={kindRefusal ? null : action.error} />

      <ConfirmDialog
        open={awaitingMidnightConfirmation !== null}
        onOpenChange={(open) => !open && setAwaitingMidnightConfirmation(null)}
        title="Converter Data em Data e hora?"
        description={
          valueCount === undefined
            ? "Cada data guardada passa a ser essa data à meia-noite UTC. Dá para desfazer depois."
            : `As datas de ${nodesCounted(valueCount)}, apagados incluídos, passam a ser essa data à meia-noite UTC. Dá para desfazer depois.`
        }
        confirmLabel="Converter"
        pending={update.isPending}
        onConfirm={() => {
          if (awaitingMidnightConfirmation) {
            save(awaitingMidnightConfirmation);
          }

          setAwaitingMidnightConfirmation(null);
        }}
      />
    </form>
  );
}

/** One definition in the list: its kind, options and Types, and the actions on it. */
export function PropertyDefinitionRow({ definition }: { definition: PropertyDefinitionItem }) {
  const archive = useArchivePropertyDefinition(definition.id);
  const [isEditing, setIsEditing] = useState(false);
  const [isDeleting, setIsDeleting] = useState(false);
  const optionLabels = definition.options.map((option) => (option.archived ? `${option.label} (arquivada)` : option.label));

  return (
    <li className="space-y-2 px-3 py-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <p>
            <span className="font-medium">{definition.name}</span>{" "}
            {definition.isArchived && <span className="rounded border px-1.5 text-xs">Arquivada</span>}{" "}
            <span className="text-sm text-muted-foreground">
              ({VALUE_KIND_LABELS[definition.valueKind]}
              {optionLabels.length > 0 && `: ${optionLabels.join(", ")}`})
            </span>
          </p>
          <p className="text-xs text-muted-foreground">{usedInTypesLabel(Number(definition.typeCount))}</p>
        </div>
        {definition.isSystem ? (
          <SystemMark />
        ) : (
          <span className="flex gap-1">
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => setIsEditing((editing) => !editing)}
              aria-expanded={isEditing}
              aria-label={`${isEditing ? "Fechar" : "Editar"} ${definition.name}`}
            >
              {isEditing ? "Fechar" : "Editar"}
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => archive.mutate(!definition.isArchived)}
              disabled={archive.isPending}
              aria-label={`${definition.isArchived ? "Restaurar" : "Arquivar"} propriedade ${definition.name}`}
            >
              {definition.isArchived ? "Restaurar" : "Arquivar"}
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => setIsDeleting(true)}
              aria-label={`Apagar propriedade ${definition.name}`}
            >
              Apagar
            </Button>
          </span>
        )}
      </div>
      <RefusalOrError error={archive.error} />
      {isEditing && <PropertyDefinitionEditor definition={definition} onDone={() => setIsEditing(false)} />}
      {isDeleting && <DeleteDefinitionDialog definition={definition} onOpenChange={setIsDeleting} />}
    </li>
  );
}

const dateFormat = new Intl.DateTimeFormat("pt-BR", { dateStyle: "long" });

/** Why the definition cannot be deleted now, or null when it can (DA-066, DA-168). */
function deleteBlockOf(usage: PropertyDefinitionUsage): string | null {
  if (usage.typeCount > 0) {
    return `${usedInTypesLabel(usage.typeCount)}: desanexe-a de todos antes de apagar.`;
  }

  if (usage.tombstoneCount > 0) {
    const holders =
      usage.tombstoneCount === 1
        ? "1 Node apagado ainda guarda um valor"
        : `${countFormat.format(usage.tombstoneCount)} Nodes apagados ainda guardam valores`;
    const purge = usage.lastPurgeAt ? ` O último é removido de vez em ${dateFormat.format(new Date(usage.lastPurgeAt))}.` : "";
    return `${holders} e ainda podem ser restaurados.${purge} Para apagar antes, faça o Purge desses Nodes.`;
  }

  return null;
}

/** Confirms the delete with the count the usage read gave; a changed count reads it again and asks once more. */
function DeleteDefinitionDialog({
  definition,
  onOpenChange,
}: {
  definition: PropertyDefinitionItem;
  onOpenChange: (open: boolean) => void;
}) {
  const usage = useQuery(propertyDefinitionUsageQuery(definition.id, { enabled: true }));
  const remove = useDeletePropertyDefinition(definition.id);
  const block = usage.data ? deleteBlockOf(usage.data) : null;
  const canDelete = usage.isSuccess && !usage.isFetching && block === null;

  const confirm = () => {
    if (!usage.data || !canDelete || remove.isPending) {
      return;
    }

    remove.mutate(
      { expectedCount: usage.data.nodeCount },
      {
        onSuccess: () => onOpenChange(false),
        onError: (error) => {
          if (isPropertyUsageChanged(error)) {
            void usage.refetch();
          }
        },
      },
    );
  };

  return (
    <Dialog open onOpenChange={onOpenChange}>
      <DialogContent title={`Apagar a propriedade ${definition.name}?`}>
        {usage.isPending && <p role="status">Contagem indisponível enquanto carrega…</p>}
        {usage.isError && (
          <div className="space-y-2">
            <p role="alert" className="text-sm text-destructive">
              Contagem indisponível: não dá para apagar sem saber quantos Nodes perdem o valor.
            </p>
            <Button type="button" variant="secondary" size="sm" onClick={() => void usage.refetch()}>
              Tentar de novo
            </Button>
          </div>
        )}
        {usage.data &&
          (block ? (
            <p className="text-sm">{block}</p>
          ) : (
            <p className="text-sm">
              {usage.data.nodeCount === 0
                ? "Nenhum Node guarda valor nesta propriedade. Dá para desfazer depois."
                : `Os valores de ${nodesCounted(usage.data.nodeCount)} serão removidos. Dá para desfazer depois.`}
            </p>
          ))}
        {remove.error &&
          (isPropertyUsageChanged(remove.error) ? (
            <p role="alert" className="text-sm text-destructive">
              {writeErrorMessage(remove.error)}
            </p>
          ) : (
            <RefusalOrError error={remove.error} />
          ))}
        <div className="flex justify-end gap-2">
          <DialogClose asChild>
            <Button type="button" variant="ghost">
              Cancelar
            </Button>
          </DialogClose>
          <Button type="button" onClick={confirm} disabled={!canDelete || remove.isPending}>
            Apagar propriedade
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
