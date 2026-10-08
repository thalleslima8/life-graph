import { zodResolver } from "@hookform/resolvers/zod";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Controller, useForm, useWatch } from "react-hook-form";
import { z } from "zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import {
  NAME_MAX_LENGTH,
  propertyDefinitionsQuery,
  toSchemaRefusal,
  typesQuery,
  useAttachPropertyToType,
  useCreatePropertyDefinition,
  useCreateType,
  useDeletePropertyDefinition,
  useDeleteType,
  useDetachPropertyFromType,
  useUpdatePropertyDefinition,
  useUpdateType,
  type PropertyDefinitionItem,
  type PropertyValueKind,
  type SelectOption,
  type TypeItem,
} from "./graphData";
import { LOAD_ERROR_MESSAGE, schemaRefusalMessage, VALUE_KIND_LABELS, writeErrorMessage } from "./graphMessages";

/** The minimal editor of Types and Property Definitions (8 value kinds, DA-016, DA-023); the full one is E8. */
export function OntologyPage() {
  const types = useQuery(typesQuery);
  const definitions = useQuery(propertyDefinitionsQuery);
  const createType = useCreateType();

  return (
    <section aria-labelledby="ontology-title" className="space-y-8">
      <h1 id="ontology-title" className="text-2xl font-semibold">
        Types e propriedades
      </h1>

      <section aria-labelledby="definitions-title" className="space-y-3">
        <h2 id="definitions-title" className="text-lg font-semibold">
          Propriedades
        </h2>
        <DefinitionForm />
        {definitions.isPending && <p role="status">Carregando…</p>}
        {definitions.isError && (
          <p role="alert" className="text-sm text-destructive">
            {LOAD_ERROR_MESSAGE}
          </p>
        )}
        {definitions.isSuccess &&
          (definitions.data.length === 0 ? (
            <p className="text-sm text-muted-foreground">Nenhuma propriedade.</p>
          ) : (
            <ul className="divide-y rounded-md border" aria-label="Propriedades">
              {definitions.data.map((definition) => (
                <DefinitionRow key={definition.id} definition={definition} />
              ))}
            </ul>
          ))}
      </section>

      <section aria-labelledby="types-title" className="space-y-3">
        <h2 id="types-title" className="text-lg font-semibold">
          Types
        </h2>
        <NameForm
          label="Novo Type"
          idPrefix="new-type"
          submitLabel="Criar Type"
          action={{ ...createType, submit: (name, onDone) => createType.mutate({ name }, { onSuccess: onDone }) }}
        />
        {types.isPending && <p role="status">Carregando…</p>}
        {types.isError && (
          <p role="alert" className="text-sm text-destructive">
            {LOAD_ERROR_MESSAGE}
          </p>
        )}
        {types.isSuccess &&
          (types.data.length === 0 ? (
            <p className="text-sm text-muted-foreground">Nenhum Type.</p>
          ) : (
            <ul className="divide-y rounded-md border" aria-label="Types">
              {types.data.map((type) => (
                <TypeRow key={type.id} type={type} definitions={definitions.data ?? []} />
              ))}
            </ul>
          ))}
      </section>
    </section>
  );
}

const nameSchema = z.object({
  name: z.string().trim().min(1, "Informe um nome.").max(NAME_MAX_LENGTH, `Use no máximo ${NAME_MAX_LENGTH} caracteres.`),
});

type NameSubmitter = {
  submit(name: string, onDone: () => void): void;
  isPending: boolean;
  error: unknown;
};

/** One name field and a button: create a Type, or rename one. */
function NameForm({
  label,
  idPrefix,
  initialName = "",
  submitLabel = "Criar",
  action,
}: {
  label: string;
  idPrefix: string;
  initialName?: string;
  submitLabel?: string;
  action: NameSubmitter;
}) {
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<{ name: string }>({ resolver: zodResolver(nameSchema), defaultValues: { name: initialName } });
  const fieldId = `${idPrefix}-name`;

  const onSubmit = handleSubmit(({ name }) => {
    if (!action.isPending) {
      action.submit(name, () => reset({ name: initialName ? name : "" }));
    }
  });

  return (
    <form noValidate onSubmit={onSubmit} aria-label={label} className="space-y-2">
      <Label htmlFor={fieldId}>{label}</Label>
      <div className="flex gap-2">
        <Input
          id={fieldId}
          maxLength={NAME_MAX_LENGTH}
          aria-invalid={errors.name ? true : undefined}
          aria-describedby={errors.name ? `${fieldId}-error` : undefined}
          {...register("name")}
        />
        <Button type="submit" variant="secondary" disabled={action.isPending}>
          {submitLabel}
        </Button>
      </div>
      {errors.name && (
        <p id={`${fieldId}-error`} role="alert" className="text-sm text-destructive">
          {errors.name.message}
        </p>
      )}
      {action.error !== null && action.error !== undefined && (
        <p role="alert" className="text-sm text-destructive">
          {writeErrorMessage(action.error)}
        </p>
      )}
    </form>
  );
}

function RefusalOrError({ error }: { error: unknown }) {
  if (error === null || error === undefined) {
    return null;
  }

  const refusal = toSchemaRefusal(error);
  return (
    <p role="alert" className="text-sm text-destructive">
      {refusal ? schemaRefusalMessage(refusal) : writeErrorMessage(error)}
    </p>
  );
}

const HIDDEN_TYPE_HINT = "Nenhum agente conectado vê os Nodes deste Type: eles ficam fora de busca, contexto e relações.";

function TypeRow({ type, definitions }: { type: TypeItem; definitions: PropertyDefinitionItem[] }) {
  const remove = useDeleteType(type.id);
  const attach = useAttachPropertyToType(type.id);
  const detach = useDetachPropertyFromType(type.id);
  const update = useUpdateType(type.id);
  const hide = useUpdateType(type.id);
  const [toAttach, setToAttach] = useState("");
  const attachable = definitions.filter((definition) => !type.properties.some((property) => property.propertyDefinitionId === definition.id));
  const attachId = `attach-${type.id}`;

  return (
    <li className="space-y-3 px-3 py-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="font-medium">{type.name}</h3>
        <Button type="button" variant="ghost" size="sm" onClick={() => remove.mutate()} disabled={remove.isPending} aria-label={`Apagar Type ${type.name}`}>
          Apagar
        </Button>
      </div>
      <RefusalOrError error={remove.error} />

      <div className="space-y-1">
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={type.hiddenFromAgents}
            disabled={hide.isPending}
            onChange={(event) => hide.mutate({ hiddenFromAgents: event.target.checked })}
            aria-describedby={`hidden-${type.id}-hint`}
          />
          Oculto para agentes
        </label>
        <p id={`hidden-${type.id}-hint`} className="text-xs text-muted-foreground">
          {HIDDEN_TYPE_HINT}
        </p>
        <RefusalOrError error={hide.error} />
      </div>

      <NameForm
        label={`Renomear ${type.name}`}
        idPrefix={`rename-${type.id}`}
        initialName={type.name}
        submitLabel="Renomear"
        action={{ ...update, submit: (name, onDone) => update.mutate({ name }, { onSuccess: onDone }) }}
      />

      {type.properties.length > 0 && (
        <ul className="space-y-1 text-sm" aria-label={`Propriedades de ${type.name}`}>
          {type.properties.map((property) => (
            <li key={property.propertyDefinitionId} className="flex items-center gap-2">
              <span>
                {property.name} <span className="text-muted-foreground">({VALUE_KIND_LABELS[property.valueKind]})</span>
              </span>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={() => detach.mutate(property.propertyDefinitionId)}
                disabled={detach.isPending}
                aria-label={`Desanexar ${property.name} de ${type.name}`}
              >
                Desanexar
              </Button>
            </li>
          ))}
        </ul>
      )}

      {attachable.length > 0 && (
        <div className="flex flex-wrap items-end gap-2">
          <div className="w-56 space-y-2">
            <Label htmlFor={attachId}>Anexar propriedade</Label>
            <Select value={toAttach} onValueChange={setToAttach}>
              <SelectTrigger id={attachId}>
                <SelectValue placeholder="Escolha" />
              </SelectTrigger>
              <SelectContent>
                {attachable.map((definition) => (
                  <SelectItem key={definition.id} value={definition.id}>
                    {definition.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <Button
            type="button"
            variant="secondary"
            disabled={toAttach === "" || attach.isPending}
            onClick={() => attach.mutate(toAttach, { onSuccess: () => setToAttach("") })}
          >
            Anexar
          </Button>
        </div>
      )}
      <RefusalOrError error={attach.error ?? detach.error ?? update.error} />
    </li>
  );
}

const VALUE_KINDS = Object.keys(VALUE_KIND_LABELS) as PropertyValueKind[];

function hasChoices(kind: PropertyValueKind): boolean {
  return kind === "select" || kind === "multi_select";
}

const definitionSchema = z.object({
  name: z.string().trim().min(1, "Informe um nome.").max(NAME_MAX_LENGTH, `Use no máximo ${NAME_MAX_LENGTH} caracteres.`),
  valueKind: z.enum(VALUE_KINDS as [PropertyValueKind, ...PropertyValueKind[]]),
  options: z.string(),
});

type DefinitionValues = z.infer<typeof definitionSchema>;

/** One option per line; a label already in use keeps its option id, so the values that chose it stay. */
function optionsFrom(text: string, current: SelectOption[]): { id?: string; label: string }[] {
  const labels = [...new Set(text.split("\n").map((line) => line.trim()).filter(Boolean))];
  return labels.map((label) => {
    const existing = current.find((option) => option.label.toLowerCase() === label.toLowerCase());
    return existing ? { id: existing.id, label } : { label };
  });
}

/** Creates a definition, or edits one when `definition` is given. */
function DefinitionForm({ definition, onDone }: { definition?: PropertyDefinitionItem; onDone?: () => void }) {
  const create = useCreatePropertyDefinition();
  const update = useUpdatePropertyDefinition(definition?.id ?? "");
  const action = definition ? update : create;
  const idPrefix = definition ? `definition-${definition.id}` : "new-definition";
  const {
    control,
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<DefinitionValues>({
    resolver: zodResolver(definitionSchema),
    defaultValues: {
      name: definition?.name ?? "",
      valueKind: definition?.valueKind ?? "text",
      options: (definition?.options ?? []).map((option) => option.label).join("\n"),
    },
  });
  const valueKind = useWatch({ control, name: "valueKind" });

  const onSubmit = handleSubmit((values) => {
    if (action.isPending) {
      return;
    }

    const input = {
      name: values.name,
      valueKind: values.valueKind,
      options: hasChoices(values.valueKind) ? optionsFrom(values.options, definition?.options ?? []) : [],
    };
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
  });

  return (
    <form noValidate onSubmit={onSubmit} aria-label={definition ? `Propriedade ${definition.name}` : "Criar propriedade"} className="space-y-3">
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
                <SelectTrigger id={`${idPrefix}-kind`} onBlur={field.onBlur}>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {VALUE_KINDS.map((kind) => (
                    <SelectItem key={kind} value={kind}>
                      {VALUE_KIND_LABELS[kind]}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          />
        </div>
      </div>
      {hasChoices(valueKind) && (
        <div className="space-y-2">
          <Label htmlFor={`${idPrefix}-options`}>Opções (uma por linha)</Label>
          <Textarea id={`${idPrefix}-options`} rows={3} {...register("options")} />
        </div>
      )}
      <Button type="submit" variant="secondary" disabled={action.isPending}>
        {definition ? "Salvar" : "Criar propriedade"}
      </Button>
      <RefusalOrError error={action.error} />
    </form>
  );
}

function DefinitionRow({ definition }: { definition: PropertyDefinitionItem }) {
  const remove = useDeletePropertyDefinition(definition.id);
  const [isEditing, setIsEditing] = useState(false);

  return (
    <li className="space-y-2 px-3 py-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p>
          <span className="font-medium">{definition.name}</span>{" "}
          <span className="text-sm text-muted-foreground">
            ({VALUE_KIND_LABELS[definition.valueKind]}
            {definition.options.length > 0 && `: ${definition.options.map((option) => option.label).join(", ")}`})
          </span>
        </p>
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
            onClick={() => remove.mutate()}
            disabled={remove.isPending}
            aria-label={`Apagar propriedade ${definition.name}`}
          >
            Apagar
          </Button>
        </span>
      </div>
      <RefusalOrError error={remove.error} />
      {isEditing && <DefinitionForm definition={definition} onDone={() => setIsEditing(false)} />}
    </li>
  );
}
