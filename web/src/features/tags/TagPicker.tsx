import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useId, useState } from "react";
import { Button } from "@/components/ui/button";
import { Combobox, type ComboboxOption } from "@/components/ui/combobox";
import { Label } from "@/components/ui/label";
import { writeErrorMessage } from "@/features/graph/graphMessages";
import {
  createdTagIdOf,
  isTagNameConflict,
  tagKeyOf,
  tagNameSchema,
  tagsQuery,
  useCreateTag,
  useTagNode,
  useUntagNode,
  type NodeTag,
  type TagItem,
} from "./tagsData";

export const TAGS_UNAVAILABLE_MESSAGE = "Tags indisponíveis";
const CREATE_PREFIX = "create:";

type TagPickerProps = {
  nodeId: string;
  tags: NodeTag[];
  /** Only the chips: no field, no removal. */
  readOnly?: boolean;
};

/** The Node's Tags as chips, and a field to apply an existing Tag or create one (DA-160, DA-161). */
export function TagPicker({ nodeId, tags, readOnly = false }: TagPickerProps) {
  return (
    <div className="space-y-2">
      {tags.length === 0 ? (
        <p className="text-sm text-muted-foreground">Nenhuma Tag.</p>
      ) : (
        <ul aria-label="Tags do Node" className="flex flex-wrap gap-2">
          {tags.map((tag) => (
            <TagChip key={tag.id} nodeId={nodeId} tag={tag} readOnly={readOnly} />
          ))}
        </ul>
      )}
      {!readOnly && <AddTagField nodeId={nodeId} tags={tags} />}
    </div>
  );
}

function TagChip({ nodeId, tag, readOnly }: { nodeId: string; tag: NodeTag; readOnly: boolean }) {
  const untag = useUntagNode(nodeId);

  return (
    <li className="flex items-center gap-1 rounded-full border px-3 py-0.5 text-sm">
      <span>#{tag.name}</span>
      {!readOnly && (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="h-6 px-1"
          aria-label={`Remover a Tag ${tag.name}`}
          disabled={untag.isPending}
          onClick={() => untag.mutate(tag.id)}
        >
          ×
        </Button>
      )}
      {untag.error && (
        <span role="alert" className="text-destructive">
          {writeErrorMessage(untag.error)}
        </span>
      )}
    </li>
  );
}

function AddTagField({ nodeId, tags }: { nodeId: string; tags: NodeTag[] }) {
  const fieldId = useId();
  const queryClient = useQueryClient();
  const allTags = useQuery(tagsQuery);
  const createTag = useCreateTag();
  const tagNode = useTagNode(nodeId);
  const [text, setText] = useState("");

  const typedName = tagNameSchema.safeParse(text);
  const options = optionsFor(text, typedName.success ? typedName.data : null, allTags.data ?? [], tags);
  const pending = createTag.isPending || tagNode.isPending;
  const writeError = createTag.error ?? tagNode.error;

  const apply = (tagId: string) => tagNode.mutate(tagId, { onSuccess: () => setText("") });

  // Someone created the same name meanwhile: apply that Tag instead of failing.
  const applyExisting = async (name: string) => {
    const fresh = await queryClient.fetchQuery({ ...tagsQuery, staleTime: 0 });
    const existing = fresh.find((tag) => tagKeyOf(tag.name) === tagKeyOf(name));
    if (existing) {
      createTag.reset();
      apply(existing.id);
    }
  };

  const select = (option: ComboboxOption) => {
    if (!option.value.startsWith(CREATE_PREFIX)) {
      apply(option.value);
      return;
    }

    const name = option.value.slice(CREATE_PREFIX.length);
    createTag.mutate(name, {
      onSuccess: (receipt) => {
        const createdId = createdTagIdOf(receipt);
        if (createdId) {
          apply(createdId);
        }
      },
      onError: (error) => {
        if (isTagNameConflict(error)) {
          void applyExisting(name);
        }
      },
    });
  };

  const unavailable = allTags.isError;
  const hintId = `${fieldId}-hint`;
  const shownNameError = text.trim() !== "" && !typedName.success ? typedName.error.issues[0]?.message : undefined;

  return (
    <div className="space-y-1">
      <Label htmlFor={fieldId}>Adicionar Tag</Label>
      <Combobox
        id={fieldId}
        listLabel="Tags"
        inputValue={text}
        onInputChange={setText}
        options={options}
        onSelect={select}
        disabled={unavailable || pending}
        aria-describedby={unavailable || shownNameError ? hintId : undefined}
        aria-invalid={shownNameError ? true : undefined}
      />
      {unavailable && (
        <p id={hintId} className="text-sm text-muted-foreground">
          {TAGS_UNAVAILABLE_MESSAGE}
        </p>
      )}
      {!unavailable && shownNameError && (
        <p id={hintId} className="text-sm text-destructive">
          {shownNameError}
        </p>
      )}
      {writeError && !isTagNameConflict(writeError) && (
        <p role="alert" className="text-sm text-destructive">
          {writeErrorMessage(writeError)}
        </p>
      )}
    </div>
  );
}

/**
 * The Tags not yet on the Node whose name contains the typed text, then "Criar «x»" when no
 * Tag has that name ignoring case and accents (a match already on the Node offers nothing).
 */
function optionsFor(text: string, typedName: string | null, allTags: TagItem[], nodeTags: NodeTag[]): ComboboxOption[] {
  const onNode = new Set(nodeTags.map((tag) => tag.id));
  const typedKey = tagKeyOf(text.trim().replace(/^#/, "").trim());
  const matches = allTags
    .filter((tag) => !onNode.has(tag.id) && tagKeyOf(tag.name).includes(typedKey))
    .map((tag) => ({ value: tag.id, label: `#${tag.name}` }));

  const exists = typedName !== null && [...allTags, ...nodeTags].some((tag) => tagKeyOf(tag.name) === tagKeyOf(typedName));
  return typedName !== null && !exists ? [...matches, { value: `${CREATE_PREFIX}${typedName}`, label: `Criar «${typedName}»` }] : matches;
}
