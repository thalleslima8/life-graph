import { zodResolver } from "@hookform/resolvers/zod";
import { useInfiniteQuery, useQuery, type InfiniteData, type UseInfiniteQueryResult, type UseQueryResult } from "@tanstack/react-query";
import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { Controller, useForm, useWatch } from "react-hook-form";
import { Link, useLocation } from "react-router";
import { z } from "zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { AddToCollectionMenu } from "@/features/collections/AddToCollectionMenu";
import { TagPicker } from "@/features/tags/TagPicker";
import {
  ANY_TYPE,
  BODY_MAX_LENGTH,
  NO_TYPE,
  RELATION_KIND_MAX_LENGTH,
  SOFT_RELATIONS_SECTION_ID,
  TITLE_MAX_LENGTH,
  isNotFound,
  isVersionConflict,
  nodeQuery,
  nodeRelationsQuery,
  nodeSoftRelationsQuery,
  nodesQuery,
  propertyDefinitionsQuery,
  typesQuery,
  useAcceptSoftRelation,
  useArchiveNode,
  useCreateRelation,
  useDeleteNode,
  useDeleteRelation,
  useEditBaseline,
  useIgnoreSoftRelation,
  useUpdateNode,
  versionOf,
  type NodeDetail,
  type NodePropertyValue,
  type PropertyDefinitionItem,
  type RelationItem,
  type SoftRelationItem,
  type TypeItem,
  type UpdateNodeInput,
} from "./graphData";
import {
  ACCEPT_LABEL,
  IGNORE_LABEL,
  INSPECTOR_CLOSE_LABEL,
  LOAD_ERROR_MESSAGE,
  LOADING_MESSAGE,
  NO_TYPE_LABEL,
  NODE_BODY_LABEL,
  REROOT_LABEL,
  SHOW_IN_GRAPH_LABEL,
  SOFT_RELATIONS_TITLE,
  STALE_NODE_MESSAGE,
  TYPE_UNAVAILABLE_LABEL,
  VERSION_CONFLICT_MESSAGE,
  acceptSoftRelationLabel,
  formatDateTime,
  ignoreSoftRelationLabel,
  softRelationExplanationText,
  writeErrorMessage,
} from "./graphMessages";
import { ConnectToNode } from "./ConnectToNode";
import { graphPath } from "./localGraph";
import { PromoteDialog } from "./PromoteDialog";
import { PropertyValueDisplay, PropertyValueEditor } from "./PropertyValueEditor";
import { fromWire, propertyValueSchema, toWire, type PropertyField, type PropertyValue } from "./propertyValues";

type HeadingLevel = 1 | 2 | 3 | 4 | 5;

export type NodeInspectorProps = {
  nodeId: string;
  /** The level of the Inspector's own heading; its sections sit one level below. */
  headingLevel?: HeadingLevel;
  /** Given, the Inspector shows a "Fechar" button. */
  onClose?: () => void;
  /** Given, "Centralizar aqui" shows this Node in the host's view; otherwise a "Ver no grafo" link. */
  onNavigate?: (nodeId: string) => void;
  /** No write control: no actions, no edit form, no Relation removal or creation. */
  readOnly?: boolean;
};

type InspectorOptions = { sectionHeading: HeadingTag; readOnly: boolean };

type HeadingTag = "h1" | "h2" | "h3" | "h4" | "h5" | "h6";

const InspectorOptionsContext = createContext<InspectorOptions>({ sectionHeading: "h2", readOnly: false });

function headingTagOf(level: number): HeadingTag {
  return `h${Math.min(level, 6)}` as HeadingTag;
}

/**
 * The Node Inspector: read, edit, relate, archive and delete one Node. States: loading,
 * ready, gone (a 404 for a Node already shown, maybe deleted), not found and error (DA-118).
 * Embeddable (DA-142): the host picks the heading level and may close it or show the Node in its view.
 */
export function NodeInspector({ nodeId, headingLevel = 2, onClose, onNavigate, readOnly = false }: NodeInspectorProps) {
  const node = useQuery(nodeQuery(nodeId));
  const options = useMemo(() => ({ sectionHeading: headingTagOf(headingLevel + 1), readOnly }), [headingLevel, readOnly]);
  const [title, content] = inspectorStateOf(node, onNavigate);

  // One shell for every state: the heading a host focused stays the same element.
  return (
    <InspectorOptionsContext.Provider value={options}>
      <InspectorShell title={title} heading={headingTagOf(headingLevel)} onClose={onClose}>
        {content}
      </InspectorShell>
    </InspectorOptionsContext.Provider>
  );
}

function inspectorStateOf(node: UseQueryResult<NodeDetail, unknown>, onNavigate?: (nodeId: string) => void): [string, ReactNode] {
  if (node.isError && isNotFound(node.error)) {
    // The data of an earlier read survives the failed refetch: the Node was shown, then went away.
    return node.data ? ["Node indisponível", <GoneState />] : ["Node não encontrado", <NotFoundState />];
  }

  if (node.isError) {
    return [
      "Node",
      <p role="alert" className="text-sm text-destructive">
        {LOAD_ERROR_MESSAGE}
      </p>,
    ];
  }

  if (node.isPending) {
    return ["Node", <p role="status">Carregando…</p>];
  }

  return [node.data.title, <ReadyInspector node={node.data} onNavigate={onNavigate} />];
}

function InspectorShell({ title, heading: Heading, onClose, children }: { title: string; heading: HeadingTag; onClose?: () => void; children: ReactNode }) {
  return (
    <section aria-labelledby="inspector-title" className="space-y-6">
      <div className="flex items-start justify-between gap-2">
        {/* tabIndex -1: a host moves focus here when it opens the Inspector (FE-032). */}
        <Heading id="inspector-title" tabIndex={-1} className="text-2xl font-semibold break-words">
          {title}
        </Heading>
        {onClose && (
          <Button type="button" variant="ghost" size="sm" onClick={onClose}>
            {INSPECTOR_CLOSE_LABEL}
          </Button>
        )}
      </div>
      {children}
    </section>
  );
}

function GoneState() {
  return (
    <p>
      Este Node não está mais disponível (pode ter sido apagado).{" "}
      <Link to="/changes" className="underline underline-offset-4">
        Desfazer em Recent Changes
      </Link>
      .
    </p>
  );
}

function NotFoundState() {
  return (
    <p>
      <Link to="/nodes" className="underline underline-offset-4">
        Voltar para os Nodes
      </Link>
    </p>
  );
}

function ReadyInspector({ node, onNavigate }: { node: NodeDetail; onNavigate?: (nodeId: string) => void }) {
  const { readOnly } = useContext(InspectorOptionsContext);

  return (
    <>
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        <dt className="text-muted-foreground">Type</dt>
        <dd>{node.typeId === null ? NO_TYPE_LABEL : (node.typeName ?? TYPE_UNAVAILABLE_LABEL)}</dd>
        <dt className="text-muted-foreground">Inbox</dt>
        <dd>{node.inboxEnteredAt ? `Desde ${formatDateTime(node.inboxEnteredAt)}` : "Fora da Inbox"}</dd>
        <dt className="text-muted-foreground">Alterado em</dt>
        <dd>{formatDateTime(node.updatedAt)}</dd>
      </dl>
      {onNavigate ? (
        <p>
          <Button type="button" variant="secondary" size="sm" onClick={() => onNavigate(node.id)}>
            {REROOT_LABEL}
          </Button>
        </p>
      ) : (
        !readOnly && (
          <p>
            <Link to={graphPath(node.id)} className="underline underline-offset-4">
              {SHOW_IN_GRAPH_LABEL}
            </Link>
          </p>
        )
      )}

      {!readOnly && <NodeActions node={node} />}
      <NodeProperties node={node} />
      <NodeTags node={node} />
      {!readOnly && <NodeCollections node={node} />}
      <NodeRelations node={node} />
      <NodeSoftRelations node={node} />
    </>
  );
}

function NodeTags({ node }: { node: NodeDetail }) {
  const { sectionHeading: Heading, readOnly } = useContext(InspectorOptionsContext);

  return (
    <section aria-labelledby="node-tags-title" className="space-y-2">
      <Heading id="node-tags-title" className="text-lg font-semibold">
        Tags
      </Heading>
      <TagPicker nodeId={node.id} tags={node.tags ?? []} readOnly={readOnly} />
    </section>
  );
}

/** Adds the Node to a manual Collection; hidden in the shared, read-only Inspector (Adjustment 1). */
function NodeCollections({ node }: { node: NodeDetail }) {
  const { sectionHeading: Heading } = useContext(InspectorOptionsContext);

  return (
    <section aria-labelledby="node-collections-title" className="space-y-2">
      <Heading id="node-collections-title" className="text-lg font-semibold">
        Collections
      </Heading>
      <AddToCollectionMenu nodeId={node.id} />
    </section>
  );
}

/**
 * The edit form and Outras propriedades need the Types and Property Definitions: a stored
 * option is shown by its label, and the Type by its name. Until both load, no form.
 */
function NodeProperties({ node }: { node: NodeDetail }) {
  const { readOnly } = useContext(InspectorOptionsContext);
  const types = useQuery(typesQuery);
  const definitions = useQuery(propertyDefinitionsQuery);
  const definitionsById = useMemo(() => new Map((definitions.data ?? []).map((definition) => [definition.id, definition])), [definitions.data]);

  if (types.isError || definitions.isError) {
    const retry = () => {
      void types.refetch();
      void definitions.refetch();
    };
    return (
      <div className="space-y-2 rounded-md border p-4">
        <p role="alert" className="text-sm text-destructive">
          {LOAD_ERROR_MESSAGE}
        </p>
        <Button type="button" variant="secondary" size="sm" onClick={retry}>
          Tentar de novo
        </Button>
      </div>
    );
  }

  if (types.isPending || definitions.isPending) {
    return (
      <p role="status" className="rounded-md border p-4 text-sm">
        Carregando as propriedades…
      </p>
    );
  }

  return (
    <>
      {readOnly ? <NodeContent node={node} definitionsById={definitionsById} /> : <NodeEditForm node={node} types={types.data} definitionsById={definitionsById} />}
      <OtherProperties node={node} definitionsById={definitionsById} />
    </>
  );
}

/** A read-only Inspector's Conteúdo and Property values, in place of the edit form. */
function NodeContent({ node, definitionsById }: { node: NodeDetail; definitionsById: Map<string, PropertyDefinitionItem> }) {
  const { sectionHeading: Heading } = useContext(InspectorOptionsContext);

  return (
    <section aria-labelledby="node-content-title" className="space-y-2">
      <Heading id="node-content-title" className="text-lg font-semibold">
        {NODE_BODY_LABEL}
      </Heading>
      {node.body && <p className="text-sm whitespace-pre-wrap break-words">{node.body}</p>}
      <PropertyValues properties={node.properties} definitionsById={definitionsById} />
    </section>
  );
}

function PropertyValues({ properties, definitionsById }: { properties: NodePropertyValue[]; definitionsById: Map<string, PropertyDefinitionItem> }) {
  return (
    <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
      {properties.map((property) => (
        <div key={property.propertyDefinitionId} className="contents">
          <dt className="text-muted-foreground">{property.name}</dt>
          <dd>
            <PropertyValueDisplay definition={fieldOf(property, definitionsById)} value={shownValueOf(property, definitionsById)} />
          </dd>
        </div>
      ))}
    </dl>
  );
}

function NodeActions({ node }: { node: NodeDetail }) {
  const archive = useArchiveNode(node.id);
  const remove = useDeleteNode(node.id);
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const failure = archive.error ?? remove.error;

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap gap-2">
        {node.inInbox && (
          <Button type="button" variant="secondary" onClick={() => archive.mutate()} disabled={archive.isPending}>
            Arquivar
          </Button>
        )}
        {confirmingDelete ? (
          <>
            <Button type="button" onClick={() => remove.mutate(versionOf(node))} disabled={remove.isPending}>
              Confirmar exclusão
            </Button>
            <Button type="button" variant="ghost" onClick={() => setConfirmingDelete(false)}>
              Cancelar
            </Button>
          </>
        ) : (
          <Button type="button" variant="secondary" onClick={() => setConfirmingDelete(true)}>
            Apagar
          </Button>
        )}
      </div>
      {confirmingDelete && (
        <p className="text-sm text-muted-foreground">O Node e suas Relations vão para a lixeira; dá para desfazer por 30 dias em Recent Changes.</p>
      )}
      {failure && (
        <p role="alert" className="text-sm text-destructive">
          {writeErrorMessage(failure)}
        </p>
      )}
    </div>
  );
}

const HIDDEN_NODE_HINT = "Nenhum agente conectado vê este Node: ele fica fora de busca, contexto e relações.";
const HIDDEN_BY_TYPE_MESSAGE = "O Type deste Node já o oculta para agentes.";
const BECOMES_VISIBLE_MESSAGE =
  "Atenção: o Type atual oculta este Node para agentes e o novo Type não. Ao salvar, ele fica visível para os agentes conectados. Marque “Oculto para agentes” para mantê-lo oculto.";

type EditValues = { title: string; body: string; typeId: string; hiddenFromAgents: boolean; properties: Record<string, PropertyValue> };

// The server sets these system Properties of a Resource, never the person (DA-150): canonical_url
// follows url and fetch_status follows the fetch. System names are locked and reserved (DA-057).
const SERVER_SET_PROPERTY_NAMES = new Set(["canonical_url", "fetch_status"]);
const RESOURCE_KINDS = new Set(["web_page", "article", "video", "pdf", "image", "document", "other"]);
const RESOURCE_KIND_PROPERTY_NAME = "resource_kind";

function isSystemProperty(property: NodePropertyValue, definitionsById: Map<string, PropertyDefinitionItem>): boolean {
  return definitionsById.get(property.propertyDefinitionId)?.isSystem === true;
}

function isServerSet(property: NodePropertyValue, definitionsById: Map<string, PropertyDefinitionItem>): boolean {
  return isSystemProperty(property, definitionsById) && SERVER_SET_PROPERTY_NAMES.has(property.name);
}

/** A resource_kind outside the closed list reads as "other" (DA-056); any other value shows as stored. */
function shownValueOf(property: NodePropertyValue, definitionsById: Map<string, PropertyDefinitionItem>): NodePropertyValue["value"] {
  const isUnknownKind =
    property.name === RESOURCE_KIND_PROPERTY_NAME &&
    isSystemProperty(property, definitionsById) &&
    typeof property.value === "string" &&
    !RESOURCE_KINDS.has(property.value);
  return isUnknownKind ? "other" : property.value;
}

function fieldOf(property: NodePropertyValue, definitionsById: Map<string, PropertyDefinitionItem>): PropertyField {
  return { name: property.name, valueKind: property.valueKind, options: definitionsById.get(property.propertyDefinitionId)?.options };
}

function editValuesOf(node: NodeDetail): EditValues {
  return {
    title: node.title,
    body: node.body,
    typeId: node.typeId ?? NO_TYPE,
    hiddenFromAgents: node.hiddenFromAgents,
    properties: Object.fromEntries(node.properties.map((property) => [property.propertyDefinitionId, fromWire(property.valueKind, property.value)])),
  };
}

function editSchemaOf(node: NodeDetail, definitionsById: Map<string, PropertyDefinitionItem>) {
  return z.object({
    title: z.string().trim().min(1, "Informe um título.").max(TITLE_MAX_LENGTH, `Use no máximo ${TITLE_MAX_LENGTH} caracteres.`),
    body: z.string().max(BODY_MAX_LENGTH, `Use no máximo ${BODY_MAX_LENGTH} caracteres.`),
    typeId: z.string(),
    hiddenFromAgents: z.boolean(),
    properties: z.object(
      Object.fromEntries(
        node.properties.map((property) => [
          property.propertyDefinitionId,
          propertyValueSchema(property.valueKind, definitionsById.get(property.propertyDefinitionId)?.options),
        ]),
      ),
    ),
  });
}

function NodeEditForm({
  node,
  types,
  definitionsById,
}: {
  node: NodeDetail;
  types: TypeItem[];
  definitionsById: Map<string, PropertyDefinitionItem>;
}) {
  const update = useUpdateNode(node.id);
  const schema = useMemo(() => editSchemaOf(node, definitionsById), [node, definitionsById]);
  const form = useForm<EditValues>({ resolver: zodResolver(schema), defaultValues: editValuesOf(node) });
  const {
    control,
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isDirty, dirtyFields },
  } = form;
  const base = useEditBaseline(node, isDirty);

  // The base moves only when the form may follow the Node: then the form shows that Node.
  const formVersion = useRef(base.baseVersion);
  useEffect(() => {
    if (base.baseVersion !== formVersion.current) {
      formVersion.current = base.baseVersion;
      reset(editValuesOf(node));
    }
  }, [base.baseVersion, node, reset]);

  const selectedTypeId = useWatch({ control, name: "typeId" });
  const typeChanged = selectedTypeId !== (node.typeId ?? NO_TYPE);
  const hiddenOnItsOwn = useWatch({ control, name: "hiddenFromAgents" });
  const isTypeHidden = (typeId: string | null) => types.some((type) => type.id === typeId && type.hiddenFromAgents);
  const selectedTypeHides = isTypeHidden(selectedTypeId);
  // DA-035: leaving a Type that hid the Node, for one that does not, shows it to agents.
  const becomesVisible = typeChanged && isTypeHidden(node.typeId) && !selectedTypeHides && !hiddenOnItsOwn;

  const onSubmit = handleSubmit((values) => {
    if (update.isPending || base.baseVersion === undefined) {
      return;
    }

    const input: UpdateNodeInput = { version: base.baseVersion };
    if (dirtyFields.title) {
      input.title = values.title;
    }

    if (dirtyFields.body) {
      input.body = values.body;
    }

    if (dirtyFields.hiddenFromAgents) {
      input.hiddenFromAgents = values.hiddenFromAgents;
    }

    if (typeChanged) {
      // The values of the current Type move to Outras propriedades and come back with it (DA-016).
      input.type = { id: values.typeId === NO_TYPE ? null : values.typeId };
    } else {
      const changed = node.properties.filter((property) => dirtyFields.properties?.[property.propertyDefinitionId]);
      if (changed.length > 0) {
        input.properties = Object.fromEntries(
          changed.map((property) => [property.propertyDefinitionId, toWire(property.valueKind, values.properties[property.propertyDefinitionId] ?? null)]),
        );
      }
    }

    update.mutate(input, {
      onSuccess: () => reset(values),
      onError: (error) => {
        for (const field of ["title", "body"] as const) {
          const message = error.fieldErrors[field]?.[0];
          if (message) {
            setError(field, { message });
          }
        }
      },
    });
  });

  const errorId = update.isError ? "node-edit-error" : undefined;
  const { sectionHeading: Heading } = useContext(InspectorOptionsContext);

  return (
    <form noValidate onSubmit={onSubmit} aria-labelledby="node-edit-title" aria-describedby={errorId} className="space-y-4 rounded-md border p-4">
      <Heading id="node-edit-title" className="text-lg font-semibold">
        Editar
      </Heading>

      {base.isStale && (
        <div role="status" className="flex flex-wrap items-center gap-2 rounded-md border px-3 py-2 text-sm">
          <span>{STALE_NODE_MESSAGE}</span>
          <Button type="button" variant="secondary" size="sm" onClick={base.rebase}>
            Recarregar (descarta suas mudanças)
          </Button>
        </div>
      )}

      <div className="space-y-2">
        <Label htmlFor="node-title">Título</Label>
        <Input
          id="node-title"
          maxLength={TITLE_MAX_LENGTH}
          aria-invalid={errors.title ? true : undefined}
          aria-describedby={errors.title ? "node-title-error" : undefined}
          {...register("title")}
        />
        {errors.title && (
          <p id="node-title-error" role="alert" className="text-sm text-destructive">
            {errors.title.message}
          </p>
        )}
      </div>

      <div className="space-y-2">
        <Label htmlFor="node-body">{NODE_BODY_LABEL}</Label>
        <Textarea
          id="node-body"
          rows={6}
          maxLength={BODY_MAX_LENGTH}
          aria-invalid={errors.body ? true : undefined}
          aria-describedby={errors.body ? "node-body-error" : undefined}
          {...register("body")}
        />
        {errors.body && (
          <p id="node-body-error" role="alert" className="text-sm text-destructive">
            {errors.body.message}
          </p>
        )}
      </div>

      <div className="space-y-2">
        <Label htmlFor="node-type">Type</Label>
        <Controller
          name="typeId"
          control={control}
          render={({ field }) => (
            <Select value={field.value} onValueChange={field.onChange}>
              <SelectTrigger id="node-type" onBlur={field.onBlur}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={NO_TYPE}>{NO_TYPE_LABEL}</SelectItem>
                {types.map((type) => (
                  <SelectItem key={type.id} value={type.id}>
                    {type.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
        {typeChanged && (
          <p className="text-sm text-muted-foreground">
            Ao trocar o Type, os valores que o novo Type não tem ficam em Outras propriedades e voltam se o Node voltar a um Type que os tenha.
          </p>
        )}
        {becomesVisible && (
          <p role="status" className="text-sm font-medium">
            {BECOMES_VISIBLE_MESSAGE}
          </p>
        )}
      </div>

      <div className="space-y-1">
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" aria-describedby="node-hidden-hint" {...register("hiddenFromAgents")} />
          Oculto para agentes
        </label>
        <p id="node-hidden-hint" className="text-xs text-muted-foreground">
          {selectedTypeHides ? HIDDEN_BY_TYPE_MESSAGE : HIDDEN_NODE_HINT}
        </p>
      </div>

      {!typeChanged && node.properties.length > 0 && (
        <div className="space-y-4">
          {node.properties.map((property) =>
            isServerSet(property, definitionsById) ? (
              <div key={property.propertyDefinitionId} className="space-y-1 text-sm">
                <p className="font-medium">{property.name}</p>
                <PropertyValueDisplay definition={fieldOf(property, definitionsById)} value={property.value} />
              </div>
            ) : (
              <Controller
                key={property.propertyDefinitionId}
                name={`properties.${property.propertyDefinitionId}`}
                control={control}
                render={({ field, fieldState }) => (
                  <PropertyValueEditor
                    id={`prop-${property.propertyDefinitionId}`}
                    definition={fieldOf(property, definitionsById)}
                    value={field.value ?? null}
                    onChange={field.onChange}
                    onBlur={field.onBlur}
                    error={fieldState.error?.message}
                  />
                )}
              />
            ),
          )}
        </div>
      )}

      {update.isError && (
        <p id="node-edit-error" role="alert" className="text-sm text-destructive">
          {isVersionConflict(update.error) ? VERSION_CONFLICT_MESSAGE : writeErrorMessage(update.error)}
        </p>
      )}

      <Button type="submit" disabled={update.isPending || !isDirty}>
        {update.isPending ? "Salvando…" : "Salvar"}
      </Button>
    </form>
  );
}

function OtherProperties({ node, definitionsById }: { node: NodeDetail; definitionsById: Map<string, PropertyDefinitionItem> }) {
  const { sectionHeading: Heading, readOnly } = useContext(InspectorOptionsContext);
  const properties = node.otherProperties;
  if (properties.length === 0) {
    return null;
  }

  return (
    <section aria-labelledby="other-properties-title" className="space-y-2">
      <Heading id="other-properties-title" className="text-lg font-semibold">
        Outras propriedades
      </Heading>
      <p className="text-sm text-muted-foreground">Valores guardados de um Type anterior. Voltam sozinhos se o Node voltar a um Type que os tenha.</p>
      {readOnly ? (
        <PropertyValues properties={properties} definitionsById={definitionsById} />
      ) : (
        <ul className="space-y-2">
          {properties.map((property) => (
            <li key={property.propertyDefinitionId} className="space-y-1 text-sm">
              <PropertyValues properties={[property]} definitionsById={definitionsById} />
              <PromoteDialog node={node} property={property} />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function NodeRelations({ node }: { node: NodeDetail }) {
  const relations = useQuery(nodeRelationsQuery(node.id));
  const { sectionHeading: Heading, readOnly } = useContext(InspectorOptionsContext);

  return (
    <section aria-labelledby="relations-title" className="space-y-3">
      <Heading id="relations-title" tabIndex={-1} className="text-lg font-semibold">
        Relations
      </Heading>
      {relations.isPending && <p role="status">Carregando…</p>}
      {relations.isError && (
        <p role="alert" className="text-sm text-destructive">
          {LOAD_ERROR_MESSAGE}
        </p>
      )}
      {relations.isSuccess &&
        (relations.data.data.length === 0 ? (
          <p className="text-sm text-muted-foreground">Nenhuma Relation.</p>
        ) : (
          <ul className="divide-y rounded-md border" aria-label="Relations">
            {relations.data.data.map((relation) => (
              <RelationRow key={relation.id} relation={relation} nodeId={node.id} />
            ))}
          </ul>
        ))}
      {!readOnly && <CreateRelationForm node={node} />}
      {!readOnly && <ConnectToNode nodeId={node.id} isAccountRoot={node.isAccountRoot} />}
    </section>
  );
}

function RelationRow({ relation, nodeId }: { relation: RelationItem; nodeId: string }) {
  const remove = useDeleteRelation();
  const { readOnly } = useContext(InspectorOptionsContext);
  const isOutgoing = relation.sourceNodeId === nodeId;
  const otherId = isOutgoing ? relation.targetNodeId : relation.sourceNodeId;
  const otherTitle = isOutgoing ? relation.targetNodeTitle : relation.sourceNodeTitle;

  return (
    <li className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm">
      <span>
        {isOutgoing ? `${relation.kind} → ` : `← ${relation.kind} · `}
        <Link to={`/nodes/${otherId}`} className="underline underline-offset-4">
          {otherTitle}
        </Link>
      </span>
      <span className="flex items-center gap-2">
        {remove.isError && (
          <span role="alert" className="text-destructive">
            {writeErrorMessage(remove.error)}
          </span>
        )}
        {!readOnly && (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => remove.mutate(relation.id)}
            disabled={remove.isPending}
            aria-label={`Remover Relation ${relation.kind} com ${otherTitle}`}
          >
            Remover
          </Button>
        )}
      </span>
    </li>
  );
}

/**
 * The Node's pending Soft Relations (DA-179): apart from the Hard list, hidden when there are
 * none or the Node is not found (the Inspector's own 404 owns the page). No optimistic removal:
 * a resolved row leaves on the refetch, and focus follows it (FE-032).
 */
function NodeSoftRelations({ node }: { node: NodeDetail }) {
  const softRelations = useQuery(nodeSoftRelationsQuery(node.id));
  const { sectionHeading: Heading } = useContext(InspectorOptionsContext);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const sectionRef = useRef<HTMLElement>(null);
  // The row an Accept or Ignore resolved: once a refetch leaves it out, focus follows it.
  const resolvedIdRef = useRef<string | null>(null);
  const items = softRelations.data?.data;
  const { hash } = useLocation();
  const hasItems = items !== undefined && items.length > 0;

  // Arriving from the Inbox's link: the section is the target, once it has rows to show.
  useEffect(() => {
    if (hash === `#${SOFT_RELATIONS_SECTION_ID}` && hasItems) {
      sectionRef.current?.focus();
    }
  }, [hash, hasItems]);

  useEffect(() => {
    const resolvedId = resolvedIdRef.current;
    if (resolvedId === null || items === undefined || items.some((item) => item.id === resolvedId)) {
      return;
    }

    resolvedIdRef.current = null;
    const target = items.length > 0 ? headingRef.current : document.getElementById("relations-title");
    target?.focus();
  }, [items]);

  if (softRelations.isError && isNotFound(softRelations.error)) {
    return null;
  }

  if (softRelations.isSuccess && softRelations.data.data.length === 0) {
    return null;
  }

  return (
    <section id={SOFT_RELATIONS_SECTION_ID} ref={sectionRef} tabIndex={-1} aria-labelledby="soft-relations-title" className="space-y-3">
      <Heading id="soft-relations-title" ref={headingRef} tabIndex={-1} className="text-lg font-semibold">
        {SOFT_RELATIONS_TITLE}
      </Heading>
      {softRelations.isPending && <p role="status">{LOADING_MESSAGE}</p>}
      {softRelations.isError && (
        <p role="alert" className="text-sm text-destructive">
          {LOAD_ERROR_MESSAGE}
        </p>
      )}
      {softRelations.isSuccess && (
        <ul className="divide-y rounded-md border border-dashed" aria-label={SOFT_RELATIONS_TITLE}>
          {softRelations.data.data.map((item) => (
            <SoftRelationRow key={item.id} item={item} nodeId={node.id} onResolved={() => (resolvedIdRef.current = item.id)} />
          ))}
        </ul>
      )}
    </section>
  );
}

function SoftRelationRow({ item, nodeId, onResolved }: { item: SoftRelationItem; nodeId: string; onResolved: () => void }) {
  const accept = useAcceptSoftRelation(nodeId);
  const ignore = useIgnoreSoftRelation(nodeId);
  const { readOnly } = useContext(InspectorOptionsContext);
  const isPending = accept.isPending || ignore.isPending;
  const failed = accept.isError ? accept.error : ignore.isError ? ignore.error : null;

  return (
    <li className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm">
      <span>
        <Link to={`/nodes/${item.nodeId}`} className="underline underline-offset-4">
          {item.nodeTitle}
        </Link>
        <span className="text-muted-foreground"> · {softRelationExplanationText(item.explanation)}</span>
      </span>
      <span className="flex items-center gap-2">
        {failed !== null && (
          <span role="alert" className="text-destructive">
            {writeErrorMessage(failed)}
          </span>
        )}
        {!readOnly && (
          <>
            <Button
              type="button"
              variant="secondary"
              size="sm"
              onClick={() => {
                ignore.reset();
                accept.mutate(item.id, { onSuccess: onResolved });
              }}
              disabled={isPending}
              aria-label={acceptSoftRelationLabel(item.nodeTitle)}
            >
              {ACCEPT_LABEL}
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => {
                accept.reset();
                ignore.mutate(item.id, { onSuccess: onResolved });
              }}
              disabled={isPending}
              aria-label={ignoreSoftRelationLabel(item.nodeTitle)}
            >
              {IGNORE_LABEL}
            </Button>
          </>
        )}
      </span>
    </li>
  );
}

const relationSchema = z.object({
  kind: z.string().trim().min(1, "Informe o tipo da Relation.").max(RELATION_KIND_MAX_LENGTH, `Use no máximo ${RELATION_KIND_MAX_LENGTH} caracteres.`),
  targetNodeId: z.string().min(1, "Escolha o Node."),
});

type RelationValues = z.infer<typeof relationSchema>;

function CreateRelationForm({ node }: { node: NodeDetail }) {
  const candidates = useInfiniteQuery(nodesQuery({ type: ANY_TYPE, inbox: false }));
  const create = useCreateRelation();
  const {
    control,
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<RelationValues>({ resolver: zodResolver(relationSchema), defaultValues: { kind: "", targetNodeId: "" } });
  const others = (candidates.data?.pages ?? []).flatMap((page) => page.data).filter((candidate) => candidate.id !== node.id);

  const onSubmit = handleSubmit((values) => {
    if (create.isPending) {
      return;
    }

    create.mutate({ sourceNodeId: node.id, targetNodeId: values.targetNodeId, kind: values.kind }, { onSuccess: () => reset() });
  });

  return (
    <form noValidate onSubmit={onSubmit} aria-label="Nova Relation" className="flex flex-wrap items-start gap-2">
      <div className="space-y-2">
        <Label htmlFor="relation-kind">Tipo da Relation</Label>
        <Input
          id="relation-kind"
          maxLength={RELATION_KIND_MAX_LENGTH}
          aria-invalid={errors.kind ? true : undefined}
          aria-describedby={errors.kind ? "relation-kind-error" : undefined}
          {...register("kind")}
        />
        {errors.kind && (
          <p id="relation-kind-error" role="alert" className="text-sm text-destructive">
            {errors.kind.message}
          </p>
        )}
      </div>
      <div className="w-56 space-y-2">
        <Label htmlFor="relation-target">Com o Node</Label>
        <Controller
          name="targetNodeId"
          control={control}
          render={({ field }) => (
            <Select value={field.value} onValueChange={field.onChange}>
              <SelectTrigger
                id="relation-target"
                onBlur={field.onBlur}
                aria-invalid={errors.targetNodeId ? true : undefined}
                aria-describedby={errors.targetNodeId ? "relation-target-error" : undefined}
              >
                <SelectValue placeholder="Escolha um Node" />
              </SelectTrigger>
              <SelectContent>
                {others.map((candidate) => (
                  <SelectItem key={candidate.id} value={candidate.id}>
                    {candidate.title}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
        {errors.targetNodeId && (
          <p id="relation-target-error" role="alert" className="text-sm text-destructive">
            {errors.targetNodeId.message}
          </p>
        )}
        <RelationCandidatesState candidates={candidates} isEmpty={others.length === 0} />
      </div>
      <Button type="submit" className="mt-6" disabled={create.isPending || !candidates.isSuccess}>
        Relacionar
      </Button>
      {create.isError && (
        <p role="alert" className="w-full text-sm text-destructive">
          {writeErrorMessage(create.error)}
        </p>
      )}
    </form>
  );
}

/** Where the Nodes to relate to stand: loading, failed, none, or more to load (FE-014). */
function RelationCandidatesState({
  candidates,
  isEmpty,
}: {
  candidates: UseInfiniteQueryResult<InfiniteData<{ page: { nextCursor?: string | null } }>>;
  isEmpty: boolean;
}) {
  if (candidates.isPending) {
    return (
      <p role="status" className="text-sm text-muted-foreground">
        Carregando Nodes…
      </p>
    );
  }

  if (candidates.isError) {
    return (
      <div className="space-y-1">
        <p role="alert" className="text-sm text-destructive">
          {LOAD_ERROR_MESSAGE}
        </p>
        <Button type="button" variant="secondary" size="sm" onClick={() => void candidates.refetch()}>
          Tentar de novo
        </Button>
      </div>
    );
  }

  return (
    <>
      {isEmpty && !candidates.hasNextPage && <p className="text-sm text-muted-foreground">Nenhum outro Node para relacionar.</p>}
      {candidates.hasNextPage && (
        <Button type="button" variant="ghost" size="sm" onClick={() => void candidates.fetchNextPage()} disabled={candidates.isFetchingNextPage}>
          {candidates.isFetchingNextPage ? "Carregando…" : "Carregar mais Nodes"}
        </Button>
      )}
    </>
  );
}
