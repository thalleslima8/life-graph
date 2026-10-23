import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { LOAD_ERROR_MESSAGE, writeErrorMessage } from "@/features/graph/graphMessages";
import { NameForm } from "@/features/graph/NameForm";
import { TAG_NAME_MAX_LENGTH, tagNameSchema, tagsQuery, useCreateTag, useDeleteTag, useRenameTag, type TagItem } from "./tagsData";

// Room for the leading #, which the name drops (DA-161).
const TAG_INPUT_MAX_LENGTH = TAG_NAME_MAX_LENGTH + 1;

/** The person's Tags: create, rename and delete, with how many Nodes each is on (DA-160). */
export function TagsPage() {
  const tags = useQuery(tagsQuery);
  const createTag = useCreateTag();

  return (
    <section aria-labelledby="tags-title" className="space-y-6">
      <h1 id="tags-title" className="text-2xl font-semibold">
        Tags
      </h1>
      <NameForm
        label="Nova Tag"
        idPrefix="new-tag"
        submitLabel="Criar Tag"
        schema={tagNameSchema}
        maxLength={TAG_INPUT_MAX_LENGTH}
        action={{ ...createTag, submit: (name, onDone) => createTag.mutate(name, { onSuccess: onDone }) }}
      />
      {tags.isPending && <p role="status">Carregando…</p>}
      {tags.isError && (
        <div className="space-y-2">
          <p role="alert" className="text-sm text-destructive">
            {LOAD_ERROR_MESSAGE}
          </p>
          <Button type="button" variant="secondary" size="sm" onClick={() => void tags.refetch()}>
            Tentar de novo
          </Button>
        </div>
      )}
      {tags.isSuccess &&
        (tags.data.length === 0 ? (
          <p className="text-sm text-muted-foreground">Nenhuma Tag ainda. Crie uma acima ou pelo Inspector de um Node.</p>
        ) : (
          <ul aria-label="Tags" className="divide-y rounded-md border">
            {tags.data.map((tag) => (
              <TagRow key={tag.id} tag={tag} />
            ))}
          </ul>
        ))}
    </section>
  );
}

function nodeCountText(count: number): string {
  return count === 1 ? "1 Node" : `${count} Nodes`;
}

function TagRow({ tag }: { tag: TagItem }) {
  const rename = useRenameTag(tag.id);
  const remove = useDeleteTag(tag.id);
  const [renaming, setRenaming] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const nodeCount = Number(tag.nodeCount);

  return (
    <li className="space-y-2 p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p>
          <span className="font-medium">#{tag.name}</span> <span className="text-sm text-muted-foreground">· {nodeCountText(nodeCount)}</span>
        </p>
        <div className="flex gap-2">
          <Button type="button" variant="ghost" size="sm" aria-expanded={renaming} onClick={() => setRenaming((open) => !open)}>
            Renomear <span className="sr-only">{tag.name}</span>
          </Button>
          <Button type="button" variant="ghost" size="sm" onClick={() => setConfirming(true)}>
            Apagar <span className="sr-only">{tag.name}</span>
          </Button>
        </div>
      </div>
      {renaming && (
        <NameForm
          label={`Novo nome de ${tag.name}`}
          idPrefix={`rename-${tag.id}`}
          initialName={tag.name}
          submitLabel="Salvar"
          schema={tagNameSchema}
          maxLength={TAG_INPUT_MAX_LENGTH}
          action={{ ...rename, submit: (name) => rename.mutate(name, { onSuccess: () => setRenaming(false) }) }}
        />
      )}
      <ConfirmDialog
        open={confirming}
        onOpenChange={(open) => {
          setConfirming(open);
          if (!open) {
            remove.reset();
          }
        }}
        title={`Apagar a Tag ${tag.name}?`}
        description={`Desvincula de ${nodeCountText(nodeCount)}; desfazível em Recent Changes.`}
        confirmLabel="Apagar Tag"
        pending={remove.isPending}
        error={remove.error ? writeErrorMessage(remove.error) : undefined}
        onConfirm={() => remove.mutate(undefined, { onSuccess: () => setConfirming(false) })}
      />
    </li>
  );
}
