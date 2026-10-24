import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { useId } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import {
  ANY_TYPE,
  nodesQuery,
  propertyDefinitionsQuery,
  RELATION_KIND_MAX_LENGTH,
  typesQuery,
  type PropertyDefinitionItem,
} from "@/features/graph/graphData";
import { LOAD_ERROR_MESSAGE } from "@/features/graph/graphMessages";
import { PropertyValueEditor } from "@/features/graph/PropertyValueEditor";
import { emptyValueOf, fromWire, toWire } from "@/features/graph/propertyValues";
import { tagsQuery } from "@/features/tags/tagsData";
import { reviewText } from "./collectionMessages";
import { CRITERIA_MAX_COUNT, type CollectionCriteria, type CollectionReviewItem, type Criterion } from "./collectionsData";

type Props = {
  value: CollectionCriteria;
  onChange(value: CollectionCriteria): void;
  /** The criteria the server could not evaluate (DA-171): each one is marked with text and an icon. */
  invalid?: CollectionReviewItem[] | null;
};

type CriterionType = Criterion["type"];
type PropertyOperator = Extract<Criterion, { type: "property" }>["op"];

const CRITERION_TYPE_LABELS: Readonly<Record<CriterionType, string>> = {
  type: "Type",
  tag: "Tag",
  property: "Propriedade",
  relation: "Relation com um Node",
};

const OPERATOR_LABELS: Readonly<Record<PropertyOperator, string>> = { equals: "é igual a", contains: "contém", is_empty: "está vazia" };

/** The comparisons a value kind accepts: a MultiSelect is asked what it contains (DA-164). */
function operatorsOf(definition: PropertyDefinitionItem | undefined): PropertyOperator[] {
  return definition?.valueKind === "multi_select" ? ["contains", "is_empty"] : ["equals", "is_empty"];
}

function newCriterion(type: CriterionType): Criterion {
  switch (type) {
    case "type":
      return { type, typeId: "" };
    case "tag":
      return { type, tagId: "" };
    case "property":
      return { type, propertyId: "", op: "equals" };
    case "relation":
      return { type, nodeId: "", direction: "either" };
  }
}

/**
 * The criteria of a dynamic Collection (DA-164): all or any of a flat list of Type, Tag,
 * Property and Relation criteria. Controlled; the caller owns the form. Types, Tags and
 * Property Definitions come from the shared cache.
 */
export function CollectionCriteriaEditor({ value, onChange, invalid }: Props) {
  const matchId = useId();
  const replace = (index: number, criterion: Criterion) =>
    onChange({ ...value, criteria: value.criteria.map((current, position) => (position === index ? criterion : current)) });
  const remove = (index: number) => onChange({ ...value, criteria: value.criteria.filter((_, position) => position !== index) });

  return (
    <fieldset className="space-y-4 rounded-md border p-4">
      <legend className="px-1 text-sm font-medium">Critérios</legend>
      <div className="space-y-2">
        <Label htmlFor={matchId}>Um Node entra quando atende a</Label>
        <Select value={value.match} onValueChange={(match) => onChange({ ...value, match: match as CollectionCriteria["match"] })}>
          <SelectTrigger id={matchId} className="w-64">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">todos os critérios</SelectItem>
            <SelectItem value="any">qualquer um dos critérios</SelectItem>
          </SelectContent>
        </Select>
      </div>
      <ol className="space-y-3">
        {value.criteria.map((criterion, index) => (
          <li key={index}>
            <CriterionFields
              index={index}
              criterion={criterion}
              review={invalid?.find((item) => Number(item.criterionIndex) === index)}
              onChange={(next) => replace(index, next)}
              onRemove={() => remove(index)}
            />
          </li>
        ))}
      </ol>
      <Button
        type="button"
        variant="secondary"
        size="sm"
        disabled={value.criteria.length >= CRITERIA_MAX_COUNT}
        onClick={() => onChange({ ...value, criteria: [...value.criteria, newCriterion("type")] })}
      >
        Adicionar critério
      </Button>
    </fieldset>
  );
}

type CriterionProps = {
  index: number;
  criterion: Criterion;
  review?: CollectionReviewItem;
  onChange(criterion: Criterion): void;
  onRemove(): void;
};

function CriterionFields({ index, criterion, review, onChange, onRemove }: CriterionProps) {
  const id = useId();
  const reviewId = `${id}-review`;
  const legend = `Critério ${index + 1}`;

  return (
    <fieldset aria-describedby={review ? reviewId : undefined} className="space-y-2 rounded-md border p-3">
      <legend className="px-1 text-sm font-medium">{legend}</legend>
      {review && (
        <p id={reviewId} className="text-sm text-destructive">
          <span aria-hidden="true">⚠ </span>
          {reviewText(review)}
        </p>
      )}
      <div className="flex flex-wrap items-end gap-2">
        <div className="space-y-1">
          <Label htmlFor={`${id}-type`}>Olhar</Label>
          <Select value={criterion.type} onValueChange={(type) => onChange(newCriterion(type as CriterionType))}>
            <SelectTrigger id={`${id}-type`} className="w-48">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {(Object.keys(CRITERION_TYPE_LABELS) as CriterionType[]).map((type) => (
                <SelectItem key={type} value={type}>
                  {CRITERION_TYPE_LABELS[type]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        {criterion.type === "type" && <TypeField id={`${id}-target`} criterion={criterion} onChange={onChange} />}
        {criterion.type === "tag" && <TagField id={`${id}-target`} criterion={criterion} onChange={onChange} />}
        {criterion.type === "property" && <PropertyFields id={`${id}-target`} criterion={criterion} onChange={onChange} />}
        {criterion.type === "relation" && <RelationFields id={`${id}-target`} criterion={criterion} onChange={onChange} />}
        <Button type="button" variant="ghost" size="sm" onClick={onRemove}>
          Remover <span className="sr-only">{legend}</span>
        </Button>
      </div>
    </fieldset>
  );
}

type Choice = { id: string; label: string };

/** A choice by id; an id the list no longer has stays selected and reads as missing. */
function ChoiceField({
  id,
  label,
  value,
  choices,
  onChange,
  unavailable,
}: {
  id: string;
  label: string;
  value: string;
  choices: Choice[] | undefined;
  onChange(value: string): void;
  unavailable: boolean;
}) {
  const missing = value !== "" && choices !== undefined && !choices.some((choice) => choice.id === value);

  return (
    <div className="space-y-1">
      <Label htmlFor={id}>{label}</Label>
      <Select value={value} onValueChange={onChange} disabled={unavailable}>
        <SelectTrigger id={id} className="w-56">
          <SelectValue placeholder={unavailable ? LOAD_ERROR_MESSAGE : "Escolha"} />
        </SelectTrigger>
        <SelectContent>
          {missing && <SelectItem value={value}>(não existe mais)</SelectItem>}
          {(choices ?? []).map((choice) => (
            <SelectItem key={choice.id} value={choice.id}>
              {choice.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
}

function TypeField({
  id,
  criterion,
  onChange,
}: {
  id: string;
  criterion: Extract<Criterion, { type: "type" }>;
  onChange(criterion: Criterion): void;
}) {
  const types = useQuery(typesQuery);
  return (
    <ChoiceField
      id={id}
      label="Type"
      value={criterion.typeId}
      choices={types.data?.map((type) => ({ id: type.id, label: type.name }))}
      unavailable={types.isError}
      onChange={(typeId) => onChange({ ...criterion, typeId })}
    />
  );
}

function TagField({
  id,
  criterion,
  onChange,
}: {
  id: string;
  criterion: Extract<Criterion, { type: "tag" }>;
  onChange(criterion: Criterion): void;
}) {
  const tags = useQuery(tagsQuery);
  return (
    <ChoiceField
      id={id}
      label="Tag"
      value={criterion.tagId}
      choices={tags.data?.map((tag) => ({ id: tag.id, label: `#${tag.name}` }))}
      unavailable={tags.isError}
      onChange={(tagId) => onChange({ ...criterion, tagId })}
    />
  );
}

function PropertyFields({
  id,
  criterion,
  onChange,
}: {
  id: string;
  criterion: Extract<Criterion, { type: "property" }>;
  onChange(criterion: Criterion): void;
}) {
  const definitions = useQuery(propertyDefinitionsQuery);
  const definition = definitions.data?.find((candidate) => candidate.id === criterion.propertyId);
  const operators = operatorsOf(definition);

  return (
    <>
      <ChoiceField
        id={id}
        label="Propriedade"
        value={criterion.propertyId}
        choices={definitions.data?.map((candidate) => ({ id: candidate.id, label: candidate.name }))}
        unavailable={definitions.isError}
        onChange={(propertyId) => {
          const chosen = definitions.data?.find((candidate) => candidate.id === propertyId);
          onChange({ type: "property", propertyId, op: operatorsOf(chosen)[0] ?? "equals" });
        }}
      />
      {definition && (
        <div className="space-y-1">
          <Label htmlFor={`${id}-op`}>Comparação</Label>
          <Select value={criterion.op} onValueChange={(op) => onChange({ ...criterion, op: op as PropertyOperator, value: undefined })}>
            <SelectTrigger id={`${id}-op`} className="w-40">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {operators.map((op) => (
                <SelectItem key={op} value={op}>
                  {OPERATOR_LABELS[op]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      )}
      {definition && criterion.op !== "is_empty" && (
        <PropertyValueField id={`${id}-value`} definition={definition} criterion={criterion} onChange={onChange} />
      )}
    </>
  );
}

/** The value to compare, edited like a Node's value; "contains" picks one option of the MultiSelect. */
function PropertyValueField({
  id,
  definition,
  criterion,
  onChange,
}: {
  id: string;
  definition: PropertyDefinitionItem;
  criterion: Extract<Criterion, { type: "property" }>;
  onChange(criterion: Criterion): void;
}) {
  const kind = criterion.op === "contains" ? "select" : definition.valueKind;
  const field = { name: "Valor", valueKind: kind, options: definition.options ?? [] };
  const value = criterion.value === undefined ? emptyValueOf(kind) : fromWire(kind, criterion.value);

  return (
    <div className="min-w-48">
      <PropertyValueEditor
        id={id}
        definition={field}
        value={value}
        onChange={(next) => onChange({ ...criterion, value: toWire(kind, next) ?? undefined })}
      />
    </div>
  );
}

function RelationFields({
  id,
  criterion,
  onChange,
}: {
  id: string;
  criterion: Extract<Criterion, { type: "relation" }>;
  onChange(criterion: Criterion): void;
}) {
  const candidates = useInfiniteQuery(nodesQuery({ type: ANY_TYPE, inbox: false }));
  const nodes = candidates.data?.pages.flatMap((page) => page.data);

  return (
    <>
      <div className="space-y-1">
        <ChoiceField
          id={id}
          label="Com o Node"
          value={criterion.nodeId}
          choices={nodes?.map((node) => ({ id: node.id, label: node.title }))}
          unavailable={candidates.isError}
          onChange={(nodeId) => onChange({ ...criterion, nodeId })}
        />
        {candidates.hasNextPage && (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => void candidates.fetchNextPage()}
            disabled={candidates.isFetchingNextPage}
          >
            {candidates.isFetchingNextPage ? "Carregando…" : "Carregar mais Nodes"}
          </Button>
        )}
      </div>
      <div className="space-y-1">
        <Label htmlFor={`${id}-kind`}>Tipo da Relation (opcional)</Label>
        <Input
          id={`${id}-kind`}
          className="w-48"
          maxLength={RELATION_KIND_MAX_LENGTH}
          value={criterion.relationKind ?? ""}
          onChange={(event) => onChange({ ...criterion, relationKind: event.target.value })}
        />
      </div>
    </>
  );
}
