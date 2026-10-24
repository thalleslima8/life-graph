import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import {
  propertyDefinitionsQuery,
  typesQuery,
  useAttachPropertyToType,
  useCreateType,
  useDeleteType,
  useDetachPropertyFromType,
  useUpdateType,
  type PropertyDefinitionItem,
  type TypeItem,
} from "./graphData";
import { LOAD_ERROR_MESSAGE, VALUE_KIND_LABELS } from "./graphMessages";
import { NameForm } from "./NameForm";
import { RefusalOrError, SystemMark } from "./ontologyParts";
import { PropertyDefinitionEditor, PropertyDefinitionRow } from "./PropertyDefinitionEditor";

/** The editor of Types and Property Definitions (8 value kinds, DA-016, DA-065, DA-066). */
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
        <PropertyDefinitionEditor />
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
                <PropertyDefinitionRow key={definition.id} definition={definition} />
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

const HIDDEN_TYPE_HINT = "Nenhum agente conectado vê os Nodes deste Type: eles ficam fora de busca, contexto e relações.";

function TypeRow({ type, definitions }: { type: TypeItem; definitions: PropertyDefinitionItem[] }) {
  const remove = useDeleteType(type.id);
  const attach = useAttachPropertyToType(type.id);
  const detach = useDetachPropertyFromType(type.id);
  const update = useUpdateType(type.id);
  const hide = useUpdateType(type.id);
  const [toAttach, setToAttach] = useState("");
  // A system Property belongs to its system Type only (DA-057), and an archived one is no longer offered (DA-167).
  const attachable = definitions.filter(
    (definition) =>
      !definition.isSystem && !definition.isArchived && !type.properties.some((property) => property.propertyDefinitionId === definition.id),
  );
  const attachId = `attach-${type.id}`;

  return (
    <li className="space-y-3 px-3 py-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="font-medium">
          {type.name} {type.isSystem && <SystemMark />}
        </h3>
        {!type.isSystem && (
          <Button type="button" variant="ghost" size="sm" onClick={() => remove.mutate()} disabled={remove.isPending} aria-label={`Apagar Type ${type.name}`}>
            Apagar
          </Button>
        )}
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

      {!type.isSystem && (
        <NameForm
          label={`Renomear ${type.name}`}
          idPrefix={`rename-${type.id}`}
          initialName={type.name}
          submitLabel="Renomear"
          action={{ ...update, submit: (name, onDone) => update.mutate({ name }, { onSuccess: onDone }) }}
        />
      )}

      {type.properties.length > 0 && (
        <ul className="space-y-1 text-sm" aria-label={`Propriedades de ${type.name}`}>
          {type.properties.map((property) => (
            <li key={property.propertyDefinitionId} className="flex items-center gap-2">
              <span>
                {property.name} <span className="text-muted-foreground">({VALUE_KIND_LABELS[property.valueKind]})</span>
              </span>
              {property.isSystem ? (
                <SystemMark />
              ) : (
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
              )}
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
